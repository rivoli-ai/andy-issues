// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;

namespace Andy.Issues.Application.Interfaces;

public enum UpsertLinkedProviderResult
{
    Created = 0,
    Updated = 1,
    InvalidProvider = 2
}

public enum LinkPatResult
{
    Linked = 0,
    InvalidProvider = 1,
    InvalidPat = 2
}

public interface ILinkedProviderService
{
    Task<(LinkPatResult Result, LinkedProviderDto? Dto)> LinkPatAsync(
        LinkPatRequest request,
        string ownerUserId,
        CancellationToken ct = default);

    Task<(UpsertLinkedProviderResult Result, LinkedProviderDto? Dto)> UpsertAsync(
        CreateLinkedProviderRequest request,
        string ownerUserId,
        CancellationToken ct = default);

    Task<IReadOnlyList<LinkedProviderDto>> ListAsync(
        string ownerUserId,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(
        string provider,
        string ownerUserId,
        CancellationToken ct = default);
}
