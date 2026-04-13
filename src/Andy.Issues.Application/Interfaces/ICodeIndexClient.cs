// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Interfaces;

public enum CodeIndexRegistrationOutcome
{
    Registered = 0,
    AlreadyRegistered = 1,
    InvalidUrl = 2,
    ServiceUnavailable = 3
}

public record CodeIndexRegistrationResult(
    CodeIndexRegistrationOutcome Outcome,
    string? IndexId,
    string? Error);

public enum CodeIndexQueryOutcome
{
    Ok = 0,
    NotFound = 1,
    ServiceUnavailable = 2
}

public record CodeIndexStatusResult(
    CodeIndexQueryOutcome Outcome,
    string? Status,
    string? Summary,
    string? Error);

/// <summary>
/// Talks to the external andy-code-index service to register repositories
/// for indexing, check indexing status, and deregister. Typed outcomes
/// surface structured errors so callers can map to appropriate HTTP responses.
/// </summary>
public interface ICodeIndexClient
{
    Task<CodeIndexRegistrationResult> RegisterAsync(
        string cloneUrl,
        string defaultBranch,
        CancellationToken ct = default);

    Task<CodeIndexStatusResult> GetStatusAsync(
        string cloneUrl,
        CancellationToken ct = default);

    Task<bool> DeregisterAsync(
        string cloneUrl,
        CancellationToken ct = default);
}
