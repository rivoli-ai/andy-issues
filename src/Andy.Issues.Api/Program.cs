// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Api.Hubs;
using Andy.Issues.Api.Infrastructure;
// HostEnvironmentExtensions.IsEmbedded / IsLocalOrEmbedded
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.External;
using Andy.Issues.Infrastructure.Messaging;
using Andy.Issues.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// --- Database ---
builder.Services.AddAppDatabase(builder.Configuration);

// --- Messaging (ADR 0001) ---
// InMemory is the default for local dev and tests. Story 15.7 will add a
// NATS branch selected via Messaging:Provider = "Nats".
builder.Services.AddIssuesMessaging(builder.Configuration);

// --- Authentication (Andy Auth) ---
var andyAuthAuthority = builder.Configuration["AndyAuth:Authority"] ?? "";
if (!string.IsNullOrEmpty(andyAuthAuthority))
{
    var audience = builder.Configuration["AndyAuth:Audience"] ?? "urn:andy-issues-api";
    builder.Services.AddAuthentication("Bearer")
        .AddJwtBearer("Bearer", options =>
        {
            options.Authority = andyAuthAuthority;
            options.Audience = audience;
            // HTTPS metadata requirement is off in every non-production
            // mode: Development talks to `dotnet run`'s self-signed cert,
            // Docker uses plain http inside the compose network, Embedded
            // goes through Conductor's localhost HTTP proxy (port 9100).
            options.RequireHttpsMetadata = !builder.Environment.IsLocalOrEmbedded();

            // Permissive SSL validation is strictly a `dotnet run`
            // concession — Conductor never hits self-signed certs.
            if (builder.Environment.IsDevelopment())
            {
                options.BackchannelHttpHandler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };
                // Mode 1 legacy: some callers still ship tokens issued by
                // the hardcoded https://localhost:5001/ issuer. Only
                // accept this relaxed list in Development.
                options.TokenValidationParameters.ValidIssuers = new[]
                {
                    andyAuthAuthority, andyAuthAuthority.TrimEnd('/') + "/",
                    "https://localhost:5001", "https://localhost:5001/"
                };
            }
            else
            {
                // Docker + Embedded both expect strict issuer matching
                // against the configured `AndyAuth:Authority`. No
                // hardcoded localhost:5001 fallback; the token's `iss`
                // must match what the proxy advertises in OIDC discovery.
                options.TokenValidationParameters.ValidIssuers = new[]
                {
                    andyAuthAuthority, andyAuthAuthority.TrimEnd('/') + "/"
                };
            }
        });
    // AllowAll policy is a `dotnet run` concession — developers hit the
    // API with curl and a placeholder bearer token. It is EXPLICITLY
    // NOT applied in Embedded mode because Conductor now mints real
    // JWTs against the embedded andy-auth and real RBAC enforcement
    // is required to keep `[Authorize]` attributes load-bearing inside
    // the shipping desktop app.
    if (builder.Environment.IsDevelopment())
    {
        builder.Services.AddAuthorization(options =>
        {
            var allowAll = new AuthorizationPolicyBuilder()
                .RequireAssertion(_ => true)
                .Build();
            options.DefaultPolicy = allowAll;
            options.FallbackPolicy = allowAll;
        });
    }
    else
    {
        builder.Services.AddAuthorization();
    }
}
else
{
    builder.Services.AddAuthentication();
    builder.Services.AddAuthorization(options =>
    {
        options.DefaultPolicy = new AuthorizationPolicyBuilder()
            .RequireAssertion(_ => true)
            .Build();
    });
}

// --- RBAC (Andy.Rbac.Client) ---
var rbacBaseUrl = builder.Configuration["Rbac:ApiBaseUrl"];
if (!string.IsNullOrEmpty(rbacBaseUrl) && builder.Environment.IsDevelopment())
{
    builder.Services.ConfigureHttpClientDefaults(b =>
    {
        b.ConfigurePrimaryHttpMessageHandler(() =>
            new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
    });
}

// --- HTTP infrastructure ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<BearerForwardingHandler>();

// --- Andy Settings (centralized configuration) ---
var settingsBaseUrl = builder.Configuration["AndySettings:ApiBaseUrl"];
if (!string.IsNullOrEmpty(settingsBaseUrl))
{
    builder.Services.AddHttpClient("AndySettings", client =>
    {
        client.BaseAddress = new Uri(settingsBaseUrl);
    })
    .AddHttpMessageHandler<BearerForwardingHandler>();
    builder.Services.AddScoped<IAndySettingsClient, AndySettingsClient>();
}
else
{
    builder.Services.AddScoped<IAndySettingsClient, LocalSettingsClient>();
}

// --- Services ---
builder.Services.AddScoped<IUserDirectory, UserDirectoryService>();
builder.Services.AddScoped<IRepositoryAccessGuard, RepositoryAccessGuard>();
builder.Services.AddScoped<IRepositoryService, RepositoryService>();
builder.Services.AddScoped<IBacklogSequenceAllocator, BacklogSequenceAllocator>();
builder.Services.AddScoped<IBacklogService, BacklogService>();
builder.Services.AddScoped<IIssueService, IssueService>();
// Z8 — IDocsClient is stubbed until andy-docs Epic AJ ships. The
// stub accepts any well-formed UUID pair so attachment endpoints work
// end-to-end today; real verification + metadata fetch lands when the
// AJ contract is published.
builder.Services.AddSingleton<IDocsClient, StubDocsClient>();
builder.Services.AddScoped<IBacklogAzureDevOpsSyncService, BacklogAzureDevOpsSyncService>();
builder.Services.AddScoped<IBacklogGitHubImportService, BacklogGitHubImportService>();
builder.Services.AddSingleton<IBoardNotifier, SignalRBoardNotifier>();
builder.Services.AddSignalR();

// --- andy-containers client ---
// andy-issues never manages containers directly — every call goes through andy-containers.
// Cloud deployments set AndyContainers:BaseUrl explicitly and the BearerForwardingHandler
// propagates the caller's JWT from the ambient HttpContext. Conductor-embedded mode can
// override IContainersClient directly, so tests and Conductor need no HTTP stack at all.
var andyContainersBaseUrl = builder.Configuration["AndyContainers:BaseUrl"]
    ?? "http://andy-containers.local/";
builder.Services.AddHttpClient<IContainersClient, AndyContainersClientAdapter>(client =>
    {
        client.BaseAddress = new Uri(andyContainersBaseUrl);
    })
    .AddHttpMessageHandler<BearerForwardingHandler>();

builder.Services.AddScoped<ISandboxService, SandboxService>();
builder.Services.AddScoped<IArtifactFeedService, ArtifactFeedService>();
builder.Services.AddScoped<IMcpConfigService, McpConfigService>();
builder.Services.AddHttpClient<IMcpToolDiscoveryClient, McpToolDiscoveryClient>();
builder.Services.AddScoped<IPermissionChecker, ClaimsPermissionChecker>();
builder.Services.AddScoped<IPullRequestService, PullRequestService>();
builder.Services.AddScoped<IDraftBacklogGenerator, DraftBacklogGenerator>();
builder.Services.AddScoped<IBacklogAiService, BacklogAiService>();
builder.Services.AddScoped<ISecretStore, SecretStore>();
builder.Services.AddScoped<ILinkedProviderService, LinkedProviderService>();
builder.Services.AddScoped<ILlmSettingService, LlmSettingService>();
builder.Services.AddHttpClient<IGitHubClient, GitHubClient>();
builder.Services.AddHttpClient<IAzureDevOpsClient, AzureDevOpsClient>();

// --- andy-code-index client ---
var codeIndexBaseUrl = builder.Configuration["AndyCodeIndex:ApiBaseUrl"];
if (!string.IsNullOrEmpty(codeIndexBaseUrl))
{
    builder.Services.AddHttpClient<ICodeIndexClient, CodeIndexClient>(client =>
    {
        client.BaseAddress = new Uri(codeIndexBaseUrl);
    });
}
else
{
    builder.Services.AddSingleton<ICodeIndexClient, NullCodeIndexClient>();
}
builder.Services.AddHostedService<AzureDevOpsBacklogPullJob>();
builder.Services.AddDataProtection();

// --- OpenTelemetry ---
var otelServiceName = builder.Configuration["OpenTelemetry:ServiceName"] ?? "andy-issues-api";
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(res => res.AddService(otelServiceName))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation()
               .AddHttpClientInstrumentation()
               .AddEntityFrameworkCoreInstrumentation();
        if (!string.IsNullOrEmpty(otlpEndpoint))
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation()
               .AddHttpClientInstrumentation()
               .AddRuntimeInstrumentation();
        if (!string.IsNullOrEmpty(otlpEndpoint))
            metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    });

// --- Swagger ---
builder.Services.AddControllers(options =>
    {
        // Translate UnauthorizedAccessException → 401 (see issue #65).
        options.Filters.Add<Andy.Issues.Api.Auth.UnauthorizedExceptionFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Andy Issues API",
        Version = "v1",
        Description = "Issues management service"
    });

    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT Authorization header using the Bearer scheme."
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// --- CORS ---
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy.WithOrigins(
                "http://localhost:4200",
                "https://localhost:4200",
                "http://localhost:4203",
                "https://localhost:4203")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });

    options.AddPolicy("AllowMcpClients", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// --- gRPC ---
builder.Services.AddGrpc();

// --- MCP Server ---
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

// --- Middleware ---
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("AllowAngularApp");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// --- SignalR hubs ---
app.MapHub<BoardHub>(BoardHub.Path);

// --- gRPC endpoints ---
app.MapGrpcService<Andy.Issues.Api.GrpcServices.RepositoriesGrpcService>();
app.MapGrpcService<Andy.Issues.Api.GrpcServices.BacklogGrpcService>();
app.MapGrpcService<Andy.Issues.Api.GrpcServices.SandboxesGrpcService>();

// --- MCP endpoint ---
app.MapMcp("/mcp")
    .RequireCors("AllowMcpClients")
    .RequireAuthorization();

// --- Health check ---
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .AllowAnonymous();

app.MapFallbackToFile("index.html");

// --- Auto-migrate in development AND embedded modes ---
// Embedded mode is a single-user desktop app — there is no ops team to
// run `dotnet ef database update` on first launch. Auto-migrate so the
// SQLite schema is created inline, same as dotnet-run development.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (app.Environment.IsLocalOrEmbedded() && !string.IsNullOrEmpty(connectionString))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsNpgsql())
        await db.Database.MigrateAsync();
    else if (db.Database.IsSqlite())
        await db.Database.EnsureCreatedAsync();
}

app.Run();

// Make Program accessible for WebApplicationFactory in integration tests
public partial class Program { }
