// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Auth.M2MClient;
using Andy.Issues.Api.Hubs;
using Andy.Issues.Api.Infrastructure;
using Andy.Issues.Api.Telemetry;
// HostEnvironmentExtensions.IsEmbedded / IsLocalOrEmbedded
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Estimation;
using Andy.Issues.Infrastructure.External;
using Andy.Issues.Infrastructure.Messaging;
using Andy.Issues.Infrastructure.Services;
using Andy.Telemetry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
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

// Epic IDP (rivoli-ai/conductor#1246). Register the M2M + OBO token
// providers so DelegatedBearerHandler can resolve its dependencies.
// Idempotent — no-op if already registered.
builder.Services.AddAndyAuthM2M(builder.Configuration);

// M2M bearer attach is conditional on AndyAuth.ClientId being configured
// — same posture as andy-agents and andy-policies. In production
// (embedded Conductor + cloud) ClientId/Scope/SecretEnvVar are set and
// the handler mints a bearer for every outbound call. In tests +
// legacy-bypass mode the handler is skipped so unauthenticated stubs
// keep working.
var attachBearer = !string.IsNullOrWhiteSpace(builder.Configuration["AndyAuth:ClientId"]);

// --- Andy Settings (centralized configuration) ---
const string SettingsAudience = "urn:andy-settings-api";
var settingsBaseUrl = builder.Configuration["AndySettings:ApiBaseUrl"];
if (!string.IsNullOrEmpty(settingsBaseUrl))
{
    var settingsClient = builder.Services.AddHttpClient("AndySettings", client =>
    {
        client.BaseAddress = new Uri(settingsBaseUrl);
    });
    if (attachBearer)
    {
        settingsClient.AddHttpMessageHandler(sp => new DelegatedBearerHandler(
            sp.GetRequiredService<IDelegatedTokenProvider>(),
            sp.GetRequiredService<IServiceTokenProvider>(),
            sp.GetRequiredService<IHttpContextAccessor>(),
            SettingsAudience,
            sp.GetRequiredService<ILogger<DelegatedBearerHandler>>()));
    }
    builder.Services.AddScoped<IAndySettingsClient, AndySettingsClient>();
}
else
{
    builder.Services.AddScoped<IAndySettingsClient, LocalSettingsClient>();
}

// --- Services ---
builder.Services.AddScoped<IUserDirectory, UserDirectoryService>();
builder.Services.AddScoped<IRepositoryAccessGuard, RepositoryAccessGuard>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IAgentRulesService, AgentRulesService>();
builder.Services.AddScoped<IPullRequestStatusService, PullRequestStatusService>();
builder.Services.AddScoped<IRepositoryService, RepositoryService>();
builder.Services.AddScoped<IBacklogSequenceAllocator, BacklogSequenceAllocator>();
builder.Services.AddScoped<IBacklogService, BacklogService>();
builder.Services.AddScoped<IIssueService, IssueService>();
// #164 — when AndyDocs:BaseUrl is set, swap the stub for the real
// HTTP adapter against andy-docs (Epic AJ shipped). Otherwise keep
// the stub for tests / Conductor-embedded mode where andy-docs is
// not running. The OBO-aware DelegatedBearerHandler exchanges the
// caller's JWT for a bearer audience-scoped to andy-docs.
const string DocsAudience = "urn:andy-docs-api";
var andyDocsBaseUrl = builder.Configuration["AndyDocs:BaseUrl"];
if (!string.IsNullOrWhiteSpace(andyDocsBaseUrl))
{
    var docsClient = builder.Services.AddHttpClient<IDocsClient, AndyDocsClientAdapter>(client =>
    {
        client.BaseAddress = new Uri(andyDocsBaseUrl);
    });
    if (attachBearer)
    {
        docsClient.AddHttpMessageHandler(sp => new DelegatedBearerHandler(
            sp.GetRequiredService<IDelegatedTokenProvider>(),
            sp.GetRequiredService<IServiceTokenProvider>(),
            sp.GetRequiredService<IHttpContextAccessor>(),
            DocsAudience,
            sp.GetRequiredService<ILogger<DelegatedBearerHandler>>()));
    }
}
else
{
    builder.Services.AddSingleton<IDocsClient, StubDocsClient>();
}
// Z7 — cold-start triage estimator. Loads per-template seed defaults
// from an embedded JSON file; learned-model retraining lands once
// andy-tasks AI6 starts emitting training samples (cross-repo).
builder.Services.AddSingleton<ITriageEstimator, TriageEstimator>();
// Z2 — config-backed triage agent resolver. Reads `Triage:AgentId`
// from configuration; dynamic discovery against andy-agents lands
// once Epic W is fully ramped.
builder.Services.AddSingleton<IAgentsClient, ConfigAgentsClient>();
builder.Services.AddScoped<IBacklogAzureDevOpsSyncService, BacklogAzureDevOpsSyncService>();
builder.Services.AddScoped<IBacklogGitHubImportService, BacklogGitHubImportService>();
builder.Services.AddSingleton<IBoardNotifier, SignalRBoardNotifier>();
builder.Services.AddSignalR();

// --- andy-containers client ---
// andy-issues never manages containers directly — every call goes through andy-containers.
// Cloud deployments set AndyContainers:BaseUrl explicitly and the DelegatedBearerHandler
// exchanges the caller's JWT for a bearer audience-scoped to andy-containers. Conductor-
// embedded mode can override IContainersClient directly, so tests and Conductor need no
// HTTP stack at all.
const string ContainersAudience = "urn:andy-containers-api";
var andyContainersBaseUrl = builder.Configuration["AndyContainers:BaseUrl"]
    ?? "http://andy-containers.local/";
var containersClient = builder.Services.AddHttpClient<IContainersClient, AndyContainersClientAdapter>(client =>
{
    client.BaseAddress = new Uri(andyContainersBaseUrl);
});
if (attachBearer)
{
    containersClient.AddHttpMessageHandler(sp => new DelegatedBearerHandler(
        sp.GetRequiredService<IDelegatedTokenProvider>(),
        sp.GetRequiredService<IServiceTokenProvider>(),
        sp.GetRequiredService<IHttpContextAccessor>(),
        ContainersAudience,
        sp.GetRequiredService<ILogger<DelegatedBearerHandler>>()));
}

builder.Services.AddScoped<ISandboxService, SandboxService>();
builder.Services.AddScoped<IArtifactFeedService, ArtifactFeedService>();
builder.Services.AddScoped<IMcpConfigService, McpConfigService>();
builder.Services.AddHttpClient<IMcpToolDiscoveryClient, McpToolDiscoveryClient>();
builder.Services.AddScoped<IPermissionChecker, ClaimsPermissionChecker>();
builder.Services.AddScoped<IPullRequestService, PullRequestService>();
builder.Services.AddScoped<IBacklogGenerationTracker, BacklogGenerationTracker>();
builder.Services.AddScoped<IDraftBacklogGenerator, DraftBacklogGenerator>();
builder.Services.AddScoped<IBacklogAiService, BacklogAiService>();
builder.Services.AddScoped<ISecretStore, SecretStore>();
builder.Services.AddScoped<ILinkedProviderService, LinkedProviderService>();
builder.Services.AddScoped<ILlmSettingService, LlmSettingService>();
builder.Services.AddHttpClient<IGitHubClient, GitHubClient>();
builder.Services.AddHttpClient<IAzureDevOpsClient, AzureDevOpsClient>();

// --- andy-code-index client ---
// Epic IDP (rivoli-ai/conductor#1246). The audit flagged
// CodeIndexClient as a latent risk: it ran unauthenticated, which
// works today because andy-code-index doesn't gate per-user RBAC,
// but would 403 the moment it tightens. The OBO-aware
// DelegatedBearerHandler (audience: urn:andy-code-index-api)
// preserves user identity end-to-end.
const string CodeIndexAudience = "urn:andy-code-index-api";
var codeIndexBaseUrl = builder.Configuration["AndyCodeIndex:ApiBaseUrl"];
if (!string.IsNullOrEmpty(codeIndexBaseUrl))
{
    var codeIndexClient = builder.Services.AddHttpClient<ICodeIndexClient, CodeIndexClient>(client =>
    {
        client.BaseAddress = new Uri(codeIndexBaseUrl);
    });
    if (attachBearer)
    {
        codeIndexClient.AddHttpMessageHandler(sp => new DelegatedBearerHandler(
            sp.GetRequiredService<IDelegatedTokenProvider>(),
            sp.GetRequiredService<IServiceTokenProvider>(),
            sp.GetRequiredService<IHttpContextAccessor>(),
            CodeIndexAudience,
            sp.GetRequiredService<ILogger<DelegatedBearerHandler>>()));
    }
}
else
{
    builder.Services.AddSingleton<ICodeIndexClient, NullCodeIndexClient>();
}
builder.Services.AddHostedService<AzureDevOpsBacklogPullJob>();
builder.Services.AddDataProtection();

// --- OpenTelemetry (via Andy.Telemetry) ---
// OT5 (rivoli-ai/conductor#1263). Replaces the per-service OpenTelemetry
// hand-roll with the shared library so every Andy service shares the same
// attribute set, propagator stack, and OTLP export config. UnifiedProxy
// already emits server-side request spans, so AspNetCore instrumentation
// stays off here to avoid double-counting.
builder.Services.AddAndyTelemetry(builder.Configuration, o =>
{
    if (string.IsNullOrWhiteSpace(o.ServiceName))
        o.ServiceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "andy-issues";
    if (string.IsNullOrWhiteSpace(o.OtlpEndpoint))
        o.OtlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
    if (string.IsNullOrWhiteSpace(o.Protocol) || o.Protocol == "grpc")
    {
        var envProtocol = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL");
        if (!string.IsNullOrWhiteSpace(envProtocol))
            o.Protocol = envProtocol;
    }
    o.ActivitySources.Add(IssuesTelemetry.ActivitySourceName);
    o.Meters.Add(IssuesTelemetry.MeterName);
    o.EnableAspNetCoreInstrumentation = false;
    o.EnableHttpClientInstrumentation = true;
});
// EF Core tracing is service-specific (not bundled in Andy.Telemetry).
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddEntityFrameworkCoreInstrumentation());

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
// HC.8.1 of rivoli-ai/conductor#1245: expose the OpenAPI
// document in every environment so Conductor's in-app Help Center
// can ingest /openapi.json from the bundled service. The Swagger
// UI itself stays development-only.
app.UseSwagger();
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI();
}
// Stable alias so every andy-* service exposes the same
// path. HC.8.1 of rivoli-ai/conductor#1245.
app.MapGet("/openapi.json", () => Results.Redirect("/swagger/v1/swagger.json"))
    .ExcludeFromDescription();

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

// --- Prometheus metrics scraping (via Andy.Telemetry) ---
// OT5 (rivoli-ai/conductor#1263). Exposes /metrics for the Conductor
// scraper; OTLP push is independent.
app.MapAndyTelemetry();

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
