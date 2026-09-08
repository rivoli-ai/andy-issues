// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

public class AiConfigControllerTests
{
    private const string Key = "synthetic-ai-config-test-key";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Owner_ReadsEncryptedDefaultOrOverride_WithMetadataOnlyAudit(bool useOverride)
    {
        var logs = new CapturedLogs();
        await using var root = new TestWebApplicationFactory();
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var client = factory.CreateClient();
        var (settingId, repositoryId) = await Seed(factory.Services, useOverride);
        var response = await client.GetAsync("/api/ai-config" + (useOverride ? $"?repositoryId={repositoryId}" : ""));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        NoCache(response);
        var config = await response.Content.ReadFromJsonAsync<AiConfigDto>();
        Assert.Equal(Key, config!.ApiKey);
        Assert.Equal("openai", config.Provider);
        Assert.Equal("test-model", config.Model);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.DoesNotContain(Key, (await db.LlmSettings.FindAsync(settingId))!.ApiKey);
        var audit = await db.AuditLog.Where(x => x.ResourceType == "ai-config").ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.All(audit, x => Assert.DoesNotContain(Key, JsonSerializer.Serialize(x)));
        Assert.Equal(200, JsonDocument.Parse(audit.Single(x => x.Action.EndsWith("outcome")).Details!).RootElement.GetProperty("status").GetInt32());
        var ordinary = await client.GetStringAsync($"/api/llm-settings/{settingId}");
        Assert.DoesNotContain(Key, ordinary);
        Assert.DoesNotContain("apiKey", ordinary);
        Assert.All(logs.Messages, line => Assert.DoesNotContain(Key, line));
    }

    [Fact]
    public async Task Limit_IsPerCaller_AndRejectedCallsAreAudited()
    {
        await using var root = new TestWebApplicationFactory();
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddHttpContextAccessor();
            services.AddSingleton<IClaimsTransformation, HeaderIdentity>();
        }));
        using var client = factory.CreateClient();
        await Seed(factory.Services);
        for (var i = 0; i < 10; i++) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ai-config")).StatusCode);
        var response = await client.GetAsync("/api/ai-config");
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode); NoCache(response);
        using var scope = factory.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLog.ToListAsync();
        Assert.Equal(22, rows.Count);
        Assert.Contains(rows, x => x.Details!.Contains("429"));
        client.DefaultRequestHeaders.Add("Test-User", "other");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/ai-config")).StatusCode);
    }

    [Theory]
    [InlineData(false, HttpStatusCode.Unauthorized)]
    [InlineData(true, HttpStatusCode.NotFound)]
    public async Task AnonymousOrForeignCaller_CannotRead(bool authenticated, HttpStatusCode expected)
    {
        await using var root = new TestWebApplicationFactory();
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                if (authenticated) services.AddSingleton<IClaimsTransformation>(new Identity(true));
                else services.AddAuthentication("NoAuth").AddScheme<AuthenticationSchemeOptions, NoAuthHandler>("NoAuth", _ => { });
            }));
        using var client = factory.CreateClient();
        await Seed(factory.Services);
        var response = await client.GetAsync("/api/ai-config");
        Assert.Equal(expected, response.StatusCode); NoCache(response);
        Assert.DoesNotContain(Key, await response.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLog.CountAsync());
    }

    [Theory]
    [InlineData("attempt")]
    [InlineData("outcome")]
    public async Task AuditWriteFailure_DoesNotReleaseKey(string phase)
    {
        await using var root = new TestWebApplicationFactory();
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<AppDbContext>(options => options.AddInterceptors(new FailAudit(phase)))));
        using var client = factory.CreateClient();
        await Seed(factory.Services);
        var response = await client.GetAsync("/api/ai-config");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); NoCache(response);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CorruptKeyAndInvalidQuery_ReturnGenericAuditedFailures()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var (id, _) = await Seed(factory.Services);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.LlmSettings.FindAsync(id))!.ApiKey = "protected::llm:v1:corrupt";
            await db.SaveChangesAsync();
        }
        var corrupt = await client.GetAsync("/api/ai-config");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, corrupt.StatusCode); NoCache(corrupt);
        Assert.Equal("", await corrupt.Content.ReadAsStringAsync());
        var invalid = await client.GetAsync("/api/ai-config?repositoryId=bad-id");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); NoCache(invalid);
    }

    [Fact]
    public void OpenApi_DescribesSensitivityAndSecurity()
    {
        using var factory = new TestWebApplicationFactory();
        var schema = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var operation = schema.Paths["/api/ai-config"].Operations!.Single().Value;
        Assert.Contains("RAW API KEY", operation.Description);
        Assert.Contains("no-store", operation.Description);
        Assert.True(schema.Security!.Count > 0);
        Assert.Contains("503", operation.Responses!.Keys);
    }

    private static void NoCache(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Null(response.Headers.ETag);
    }
    private static async Task<(Guid, Guid)> Seed(IServiceProvider services, bool useOverride = false)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var key = await scope.ServiceProvider.GetRequiredService<ILlmSecretStore>().StoreAsync("test", Key);
        var setting = new LlmSetting { Id = Guid.NewGuid(), OwnerUserId = TestAuthHandler.UserId, Name = "Test", ApiKey = key, IsDefault = !useOverride, Model = "test-model" };
        var repository = new Repository { Id = Guid.NewGuid(), OwnerUserId = TestAuthHandler.UserId, LlmSettingId = useOverride ? setting.Id : null };
        db.AddRange(setting, repository);
        await db.SaveChangesAsync();
        return (setting.Id, repository.Id);
    }
    private sealed class Identity(bool authenticated) : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal) => Task.FromResult(new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("sub", "other-user")], authenticated ? "Test" : null)));
    }
    private sealed class HeaderIdentity(IHttpContextAccessor accessor) : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal) => Task.FromResult(
            accessor.HttpContext!.Request.Headers.ContainsKey("Test-User")
                ? new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "other")], "Test")) : principal);
    }
    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Capture(Messages);
        public void Dispose() { }
        private sealed class Capture(ConcurrentQueue<string> messages) : ILogger
        {
            public bool IsEnabled(LogLevel logLevel) => true;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => messages.Enqueue(formatter(state, exception));
        }
    }
    private sealed class NoAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }
    private sealed class FailAudit(string phase) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AuditLogEntry>().Any(x => x.Entity.Action == "ai-config.read." + phase))
                throw new InvalidOperationException("Synthetic audit failure");
            return ValueTask.FromResult(result);
        }
    }
}
