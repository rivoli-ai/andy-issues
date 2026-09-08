// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;

namespace Andy.Issues.Api.Auth;

// Dedicated to this credential-returning route. Never log request/response bodies
// or exception details. Buffer its small response until the outcome audit commits.
public sealed class AiConfigAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IServiceScopeFactory scopes)
    {
        if (!context.Request.Path.Equals(new PathString("/api/ai-config"), StringComparison.OrdinalIgnoreCase)
            && !context.Request.Path.Equals(new PathString("/api/ai-config/"), StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }
        context.Response.Headers.CacheControl = "no-store";
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Remove("ETag");
            return Task.CompletedTask;
        });
        var attemptId = Guid.NewGuid();
        var userId = "anonymous";
        if (context.User.Identity?.IsAuthenticated == true)
        {
            try { userId = context.User.RequireUserId(); }
            catch (UnauthorizedAccessException) { userId = "unidentified"; }
        }
        var resource = Guid.TryParse(context.Request.Query["repositoryId"], out var repositoryId)
            ? repositoryId.ToString() : "default";
        async Task Audit(string phase, int? status = null)
        {
            // Independent context and bounded token: aborted callers cannot suppress auditing.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditLog.Add(new AuditLogEntry
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Action = "ai-config.read." + phase,
                ResourceType = "ai-config",
                ResourceId = resource,
                Details = JsonSerializer.Serialize(new { attemptId, status })
            });
            await db.SaveChangesAsync(timeout.Token);
        }
        try { await Audit("attempt"); }
        catch { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            try { await next(context); }
            catch
            {
                buffer.SetLength(0);
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            }
            try { await Audit("outcome", context.Response.StatusCode); }
            catch
            {
                buffer.SetLength(0);
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            }
            context.Response.Body = originalBody;
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
        }
        finally { context.Response.Body = originalBody; }
    }
}
