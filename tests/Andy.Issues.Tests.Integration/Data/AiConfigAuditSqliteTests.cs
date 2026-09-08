// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Security.Claims;
using Andy.Issues.Api.Auth;
using Andy.Issues.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Data;

public class AiConfigAuditSqliteTests
{
    [Fact]
    public async Task AttemptAndOutcome_PersistAcrossContexts_BeforeResponseIsReleased()
    {
        var path = Path.Combine(Path.GetTempPath(), $"andy-ai-audit-{Guid.NewGuid()}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={path};Pooling=False"));
            await using var provider = services.BuildServiceProvider();
            using (var scope = provider.CreateScope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/ai-config";
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "owner")], "Test"));
            using var output = new MemoryStream();
            context.Response.Body = output;
            var middleware = new AiConfigAccessMiddleware(async ctx =>
            {
                using var scope = provider.CreateScope();
                Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLog.CountAsync());
                await ctx.Response.WriteAsync("synthetic-key");
                Assert.Equal(0, output.Length);
            });
            await middleware.InvokeAsync(context, provider.GetRequiredService<IServiceScopeFactory>());
            using var finalScope = provider.CreateScope();
            var entries = await finalScope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLog.ToListAsync();
            Assert.Equal(2, entries.Count);
            Assert.All(entries, x => Assert.DoesNotContain("synthetic-key", x.Details!));
            Assert.True(output.Length > 0);
        }
        finally { File.Delete(path); }
    }
}
