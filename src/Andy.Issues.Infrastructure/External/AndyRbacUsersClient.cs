// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net.Http.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Andy.Issues.Infrastructure.External;

/// <summary>Read-through RBAC projection; membership is never copied into the Issues database.</summary>
public sealed class AndyRbacUsersClient(IHttpClientFactory clients, IMemoryCache cache) : IAndyRbacUsersClient
{
    public async Task<AdminUsersDto> ListAsync(string userId, string? query, string? role, int skip, int take, CancellationToken ct = default)
    {
        query = query?.Trim();
        role = role?.Trim();
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 200);
        var key = (nameof(AndyRbacUsersClient), userId, query, role, skip, take);
        if (cache.TryGetValue<AdminUsersDto>(key, out var cached)) return cached!;
        var http = clients.CreateClient("AndyRbacUsers");
        if (http.BaseAddress is null) throw new HttpRequestException("Andy RBAC endpoint is not configured.");
        using var response = await http.GetAsync($"api/applications/by-code/andy-issues/users?query={Uri.EscapeDataString(query ?? "")}&role={Uri.EscapeDataString(role ?? "")}&skip={skip}&take={take}", ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AdminUsersDto>(ct)
            ?? throw new HttpRequestException("Andy RBAC returned an empty response.");
        cache.Set(key, result, TimeSpan.FromSeconds(30));
        return result;
    }
}
