// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Enums;

namespace Andy.Issues.Application.Interfaces;

public enum CreateLlmSettingResult
{
    Created = 0,
    InvalidProvider = 1,
    InvalidBaseUrl = 2
}

public enum UpdateLlmSettingResult
{
    Updated = 0,
    NotFound = 1,
    InvalidProvider = 2,
    InvalidBaseUrl = 3
}

/// <summary>
/// Per-user CRUD for <see cref="Andy.Issues.Domain.Entities.LlmSetting"/>
/// rows. The Conductor G3 per-repository settings sheet calls this to
/// manage LLM provider/model/base-url/api-key tuples that individual
/// repositories can opt into via <c>Repository.LlmSettingId</c>.
/// API keys go through <see cref="ISecretStore"/>; the <c>ApiKey</c>
/// column only ever stores a reference key (never plaintext) and the
/// outbound DTO never exposes the value.
/// Cross-user reads/writes return <c>NotFound</c> so the endpoint
/// cannot be used to probe for other users' setting IDs.
/// </summary>
public interface ILlmSettingService
{
    Task<IReadOnlyList<LlmSettingDto>> ListAsync(
        string ownerUserId,
        CancellationToken ct = default);

    Task<LlmSettingDto?> GetAsync(
        Guid id,
        string ownerUserId,
        CancellationToken ct = default);

    Task<(CreateLlmSettingResult Result, LlmSettingDto? Dto)> CreateAsync(
        CreateLlmSettingRequest request,
        string ownerUserId,
        CancellationToken ct = default);

    Task<(UpdateLlmSettingResult Result, LlmSettingDto? Dto)> UpdateAsync(
        Guid id,
        UpdateLlmSettingRequest request,
        string ownerUserId,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(
        Guid id,
        string ownerUserId,
        CancellationToken ct = default);

    Task<bool> SetDefaultAsync(
        Guid id,
        string ownerUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Live connectivity check. Makes a trivial generation call
    /// against the provider and reports whether the key + model +
    /// base URL combination is actually usable. Provider-side
    /// failures (auth, quota, model-not-found) are returned as
    /// <see cref="TestLlmSettingOutcome.ProviderRejected"/> with a
    /// human-readable message rather than bubbling as exceptions —
    /// the UI renders them as a red banner.
    /// </summary>
    Task<(TestLlmSettingOutcome Outcome, string? Message)> TestAsync(
        Guid id,
        string ownerUserId,
        CancellationToken ct = default);
}
