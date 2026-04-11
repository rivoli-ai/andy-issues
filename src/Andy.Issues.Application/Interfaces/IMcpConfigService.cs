// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Interfaces;

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;

/// <summary>
/// Server-side view of an MCP server config including secret fields. This record is
/// only used on the sandbox-injection path inside the service layer — it intentionally
/// bypasses <see cref="Andy.Issues.Application.Dtos.McpServerConfigDto"/>, which masks
/// EnvironmentJson and HeadersJson for outbound API responses. Never return an instance
/// of this record to an HTTP/gRPC/MCP client.
/// </summary>
public record McpServerConfigFull(
    Guid Id,
    string Name,
    string? Description,
    string Type,
    bool Enabled,
    string? Command,
    string? ArgumentsJson,
    string? EnvironmentJson,
    string? Url,
    string? HeadersJson);

public enum McpConfigOutcome
{
    Ok = 0,
    NotFound = 1,
    Forbidden = 2,
    Invalid = 3,
    Conflict = 4
}

public record McpConfigResult(
    McpConfigOutcome Outcome,
    McpServerConfigDto? Dto,
    string? Error);

public interface IMcpConfigService
{
    /// <summary>
    /// Returns every enabled MCP server config that the given user can see — personal
    /// configs (<c>OwnerUserId == userId</c>) plus every shared config. Results include
    /// unmasked env/headers JSON and MUST NOT be surfaced to outbound API responses.
    /// </summary>
    Task<IReadOnlyList<McpServerConfigFull>> GetEnabledForUserAsync(
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Lists every MCP config visible to the user: their personal configs plus every
    /// shared config. Secrets are always masked; these DTOs are safe for outbound API
    /// responses and for any caller regardless of admin status.
    /// </summary>
    Task<IReadOnlyList<McpServerConfigDto>> ListForUserAsync(
        string userId,
        CancellationToken ct = default);

    Task<McpServerConfigDto?> GetAsync(Guid id, string userId, bool isAdmin, CancellationToken ct = default);

    Task<McpConfigResult> CreateAsync(
        CreateMcpServerConfigRequest request,
        string userId,
        bool isAdmin,
        CancellationToken ct = default);

    Task<McpConfigResult> UpdateAsync(
        Guid id,
        UpdateMcpServerConfigRequest request,
        string userId,
        bool isAdmin,
        CancellationToken ct = default);

    Task<McpConfigResult> ToggleAsync(Guid id, string userId, bool isAdmin, CancellationToken ct = default);

    Task<McpConfigOutcome> DeleteAsync(Guid id, string userId, bool isAdmin, CancellationToken ct = default);
}
