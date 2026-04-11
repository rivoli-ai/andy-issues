// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Mapping;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.Issues.Infrastructure.Services;

public class McpConfigService : IMcpConfigService
{
    private readonly AppDbContext _db;
    private readonly IMcpToolDiscoveryClient? _toolDiscovery;

    public McpConfigService(AppDbContext db, IMcpToolDiscoveryClient? toolDiscovery = null)
    {
        _db = db;
        _toolDiscovery = toolDiscovery;
    }

    public async Task<IReadOnlyList<McpServerConfigFull>> GetEnabledForUserAsync(
        string userId,
        CancellationToken ct = default)
    {
        var rows = await _db.McpServerConfigs
            .AsNoTracking()
            .Where(c => c.Enabled && (c.OwnerUserId == userId || c.IsShared))
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        return rows
            .Select(c => new McpServerConfigFull(
                c.Id,
                c.Name,
                c.Description,
                c.Type.ToString(),
                c.Enabled,
                c.Command,
                c.ArgumentsJson,
                c.EnvironmentJson,
                c.Url,
                c.HeadersJson))
            .ToList();
    }

    public async Task<IReadOnlyList<McpServerConfigDto>> ListForUserAsync(
        string userId,
        CancellationToken ct = default)
    {
        var rows = await _db.McpServerConfigs
            .AsNoTracking()
            .Where(c => c.OwnerUserId == userId || c.IsShared)
            .OrderBy(c => c.IsShared)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);
        return rows.Select(r => r.ToDto()).ToList();
    }

    public async Task<McpServerConfigDto?> GetAsync(Guid id, string userId, bool isAdmin, CancellationToken ct = default)
    {
        var row = await _db.McpServerConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return null;
        if (!CanView(row, userId, isAdmin)) return null;
        return row.ToDto();
    }

    public async Task<McpConfigResult> CreateAsync(
        CreateMcpServerConfigRequest request,
        string userId,
        bool isAdmin,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<McpServerType>(request.Type, ignoreCase: true, out var type))
            return Invalid($"Unknown type '{request.Type}'. Use 'stdio' or 'remote'.");

        // Shape validation: stdio needs a command, remote needs a url.
        if (type == McpServerType.Stdio && string.IsNullOrWhiteSpace(request.Command))
            return Invalid("stdio MCP configs require a Command.");
        if (type == McpServerType.Remote && string.IsNullOrWhiteSpace(request.Url))
            return Invalid("remote MCP configs require a Url.");

        var wantsShared = request.IsShared == true;
        if (wantsShared && !isAdmin)
            return Forbidden("Creating shared MCP configs requires mcp:admin.");

        // Unique by (OwnerUserId, Name) — the DB has a unique index on that pair.
        var scopeOwner = wantsShared ? null : userId;
        var duplicate = await _db.McpServerConfigs
            .AnyAsync(c => c.OwnerUserId == scopeOwner && c.Name == request.Name, ct);
        if (duplicate)
            return new McpConfigResult(McpConfigOutcome.Conflict, null,
                $"An MCP config named '{request.Name}' already exists in this scope.");

        var entity = new McpServerConfig
        {
            Id = Guid.NewGuid(),
            OwnerUserId = scopeOwner,
            IsShared = wantsShared,
            Name = request.Name,
            Description = request.Description,
            Type = type,
            Command = request.Command,
            ArgumentsJson = request.ArgumentsJson,
            EnvironmentJson = request.EnvironmentJson,
            Url = request.Url,
            HeadersJson = request.HeadersJson,
            Enabled = true
        };
        _db.McpServerConfigs.Add(entity);
        await _db.SaveChangesAsync(ct);
        return Ok(entity);
    }

    public async Task<McpConfigResult> UpdateAsync(
        Guid id,
        UpdateMcpServerConfigRequest request,
        string userId,
        bool isAdmin,
        CancellationToken ct = default)
    {
        var row = await _db.McpServerConfigs.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return NotFound();
        if (!CanMutate(row, userId, isAdmin)) return Forbidden("Not allowed to mutate this MCP config.");

        if (request.Name is not null) row.Name = request.Name;
        if (request.Description is not null) row.Description = request.Description;
        if (request.Enabled is not null) row.Enabled = request.Enabled.Value;
        if (request.Command is not null) row.Command = request.Command;
        if (request.ArgumentsJson is not null) row.ArgumentsJson = request.ArgumentsJson;
        if (request.EnvironmentJson is not null) row.EnvironmentJson = request.EnvironmentJson;
        if (request.Url is not null) row.Url = request.Url;
        if (request.HeadersJson is not null) row.HeadersJson = request.HeadersJson;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(row);
    }

    public async Task<McpConfigResult> ToggleAsync(Guid id, string userId, bool isAdmin, CancellationToken ct = default)
    {
        var row = await _db.McpServerConfigs.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return NotFound();
        if (!CanMutate(row, userId, isAdmin)) return Forbidden("Not allowed to mutate this MCP config.");

        row.Enabled = !row.Enabled;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(row);
    }

    public async Task<McpConfigOutcome> DeleteAsync(Guid id, string userId, bool isAdmin, CancellationToken ct = default)
    {
        var row = await _db.McpServerConfigs.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return McpConfigOutcome.NotFound;
        if (!CanMutate(row, userId, isAdmin)) return McpConfigOutcome.Forbidden;

        _db.McpServerConfigs.Remove(row);
        await _db.SaveChangesAsync(ct);
        return McpConfigOutcome.Ok;
    }

    public async Task<McpToolDiscoveryEndpointResult> DiscoverToolsAsync(
        Guid id,
        string userId,
        bool isAdmin,
        CancellationToken ct = default)
    {
        if (_toolDiscovery is null)
            throw new InvalidOperationException(
                "McpConfigService was constructed without an IMcpToolDiscoveryClient — DiscoverToolsAsync is not available.");

        var row = await _db.McpServerConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null)
            return new McpToolDiscoveryEndpointResult(McpToolDiscoveryEndpointOutcome.NotFound, null, null, null);
        // Mutation-style authorization: the tools list can include sensitive descriptions,
        // so we require the same permissions as a mutation rather than just view.
        if (!CanMutate(row, userId, isAdmin))
            return new McpToolDiscoveryEndpointResult(McpToolDiscoveryEndpointOutcome.Forbidden, null, null, null);

        if (row.Type != McpServerType.Remote || string.IsNullOrWhiteSpace(row.Url))
            return new McpToolDiscoveryEndpointResult(
                McpToolDiscoveryEndpointOutcome.NotRemote,
                null,
                null,
                "Tool discovery is only supported for remote MCP configs.");

        var result = await _toolDiscovery.DiscoverAsync(row.Url, row.HeadersJson, ct);
        if (result.Outcome == McpToolDiscoveryOutcome.Ok)
        {
            return new McpToolDiscoveryEndpointResult(
                McpToolDiscoveryEndpointOutcome.Ok,
                result.Tools,
                result.Outcome,
                null);
        }

        return new McpToolDiscoveryEndpointResult(
            McpToolDiscoveryEndpointOutcome.DiscoveryFailed,
            null,
            result.Outcome,
            result.Error);
    }

    private static bool CanView(McpServerConfig row, string userId, bool isAdmin) =>
        row.IsShared || row.OwnerUserId == userId || isAdmin;

    private static bool CanMutate(McpServerConfig row, string userId, bool isAdmin)
    {
        if (row.IsShared) return isAdmin;
        return row.OwnerUserId == userId;
    }

    private static McpConfigResult Ok(McpServerConfig entity) =>
        new(McpConfigOutcome.Ok, entity.ToDto(), null);

    private static McpConfigResult NotFound() => new(McpConfigOutcome.NotFound, null, null);
    private static McpConfigResult Forbidden(string reason) => new(McpConfigOutcome.Forbidden, null, reason);
    private static McpConfigResult Invalid(string reason) => new(McpConfigOutcome.Invalid, null, reason);
}
