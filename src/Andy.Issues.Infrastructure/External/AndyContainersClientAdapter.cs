// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Andy.Issues.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.External;

/// <summary>
/// Adapts the andy-containers REST surface to the Application-layer
/// <see cref="IContainersClient"/> abstraction. We deliberately own the HttpClient
/// ourselves rather than depending on the sealed upstream ContainersClient so that we
/// can pass EnvironmentVariables through to <c>api/containers</c> (the upstream client
/// library's signature does not currently accept them, even though the server-side DTO
/// does). andy-issues never drives containers directly; this stays a thin HTTP wrapper.
/// </summary>
public class AndyContainersClientAdapter : IContainersClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;
    private readonly ILogger<AndyContainersClientAdapter> _logger;

    public AndyContainersClientAdapter(HttpClient http, ILogger<AndyContainersClientAdapter> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<ContainerInfo> CreateContainerAsync(
        string name,
        string templateCode,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["templateCode"] = templateCode,
            ["source"] = "Cli"
        };
        if (environmentVariables is { Count: > 0 })
            payload["environmentVariables"] = environmentVariables.ToDictionary(kv => kv.Key, kv => kv.Value);

        using var response = await _http.PostAsJsonAsync("api/containers", payload, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("andy-containers POST api/containers failed: {Status} {Body}",
                response.StatusCode, body);
            throw new HttpRequestException(
                $"andy-containers returned {(int)response.StatusCode}: {body}",
                null,
                response.StatusCode);
        }

        var dto = await response.Content.ReadFromJsonAsync<ContainerDto>(JsonOptions, ct);
        return ToInfo(dto!);
    }

    public async Task<ContainerInfo?> GetContainerAsync(string containerId, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"api/containers/{Uri.EscapeDataString(containerId)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("andy-containers GET api/containers/{Id} failed: {Status}",
                containerId, response.StatusCode);
            return null;
        }

        var dto = await response.Content.ReadFromJsonAsync<ContainerDto>(JsonOptions, ct);
        return dto is null ? null : ToInfo(dto);
    }

    public async Task DestroyContainerAsync(string containerId, CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync($"api/containers/{Uri.EscapeDataString(containerId)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return;
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"andy-containers DELETE api/containers/{containerId} returned {(int)response.StatusCode}: {body}",
                null,
                response.StatusCode);
        }
    }

    public async Task<ContainerConnectionInfo?> GetConnectionInfoAsync(string containerId, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(
            $"api/containers/{Uri.EscapeDataString(containerId)}/connection", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) return null;

        var dto = await response.Content.ReadFromJsonAsync<ConnectionDto>(JsonOptions, ct);
        if (dto is null) return null;
        return new ContainerConnectionInfo(
            dto.IpAddress,
            dto.SshEndpoint,
            dto.IdeEndpoint,
            dto.VncEndpoint,
            dto.PortMappings);
    }

    public async Task<ContainerExecResult> ExecAsync(string containerId, string command, CancellationToken ct = default)
    {
        var payload = new { command };
        using var response = await _http.PostAsJsonAsync(
            $"api/containers/{Uri.EscapeDataString(containerId)}/exec", payload, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"andy-containers POST exec returned {(int)response.StatusCode}: {body}",
                null,
                response.StatusCode);
        }

        var dto = await response.Content.ReadFromJsonAsync<ExecDto>(JsonOptions, ct);
        return new ContainerExecResult(dto?.ExitCode ?? -1, dto?.StdOut, dto?.StdErr);
    }

    private static ContainerInfo ToInfo(ContainerDto dto) =>
        new(dto.Id ?? "", dto.Name ?? "", dto.Status ?? "", dto.IdeEndpoint, dto.VncEndpoint);

    // Payload shapes mirroring the andy-containers REST surface. Kept private to the adapter
    // so the rest of the code stays typed against the Application-layer ContainerInfo record.
    private sealed record ContainerDto(
        string? Id,
        string? Name,
        string? Status,
        string? IdeEndpoint,
        string? VncEndpoint);

    private sealed record ConnectionDto(
        string? IpAddress,
        string? SshEndpoint,
        string? IdeEndpoint,
        string? VncEndpoint,
        Dictionary<string, int>? PortMappings);

    private sealed record ExecDto(int ExitCode, string? StdOut, string? StdErr);
}
