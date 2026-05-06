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
/// Real andy-docs HTTP adapter (#164). Replaces <see cref="StubDocsClient"/>
/// once andy-docs Epic AJ shipped (AJ1 blob storage, AJ2 DocumentLink
/// schema, AJ7 committed OpenAPI, AJ8 docs MCP, etc).
/// </summary>
/// <remarks>
/// Wire surface mapped:
///   <c>VerifyLinkAsync</c> → <c>GET /api/links?targetType=&amp;targetId=</c>
///     and matches the returned <c>DocumentLinkDto.Id</c> against the
///     caller's <c>linkId</c>. The andy-docs surface has no
///     <c>GET /api/links/{id}</c> endpoint today; the by-target query
///     is the canonical way to verify a link is real and points where
///     we expect.
///
///   <c>GetMetadataAsync</c> → <c>GET /api/documents/{id}</c>. The
///     current <c>DocumentDto</c> exposes <c>Name</c> only — no MIME
///     type or byte size. We populate <see cref="DocsMetadata.FileName"/>
///     from <c>Name</c>, leave <see cref="DocsMetadata.ContentType"/>
///     empty, and <see cref="DocsMetadata.SizeBytes"/> null until those
///     fields exist on the upstream contract.
/// </remarks>
public class AndyDocsClientAdapter : IDocsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly ILogger<AndyDocsClientAdapter> _logger;

    public AndyDocsClientAdapter(HttpClient http, ILogger<AndyDocsClientAdapter> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<bool> VerifyLinkAsync(
        Guid linkId,
        string expectedTargetType,
        Guid expectedTargetId,
        CancellationToken ct = default)
    {
        // andy-docs `DocumentLinkTargetType` is enum-typed (Issue=1, Task=2,
        // Run=3, Goal=4) but the controller parses case-insensitively
        // from the query string, so passing our caller's literal
        // ("issue", "task", …) round-trips cleanly.
        var url =
            $"api/links?targetType={Uri.EscapeDataString(expectedTargetType)}" +
            $"&targetId={expectedTargetId}";

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(url, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "andy-docs GET {Url} network failure", url);
            return false;
        }

        try
        {
            if (response.StatusCode == HttpStatusCode.NotFound) return false;
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "andy-docs GET {Url} failed with {Status}",
                    url, response.StatusCode);
                return false;
            }

            var dtos = await response.Content
                .ReadFromJsonAsync<DocumentLinkDto[]>(JsonOptions, ct);
            if (dtos is null) return false;

            return dtos.Any(d => d.Id == linkId);
        }
        finally
        {
            response.Dispose();
        }
    }

    public async Task<DocsMetadata?> GetMetadataAsync(
        Guid documentId, CancellationToken ct = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync($"api/documents/{documentId}", ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "andy-docs GET api/documents/{DocumentId} network failure", documentId);
            return null;
        }

        try
        {
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "andy-docs GET api/documents/{DocumentId} failed with {Status}",
                    documentId, response.StatusCode);
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<DocumentDto>(JsonOptions, ct);
            if (dto is null) return null;

            return new DocsMetadata(
                DocumentId: dto.Id,
                FileName: dto.Name ?? string.Empty,
                ContentType: string.Empty,
                SizeBytes: null);
        }
        finally
        {
            response.Dispose();
        }
    }

    // Local mirrors of the andy-docs DTO shapes. We keep them private
    // to the adapter so consumers stay typed against the
    // Application-layer abstractions; the small surface keeps the
    // codegen overhead negligible until AJ7 OpenAPI codegen lands.
    private sealed record DocumentLinkDto(
        Guid Id,
        Guid DocumentId,
        string TargetType,
        string TargetId,
        string Role,
        DateTime CreatedAt,
        Guid CreatedBy);

    private sealed record DocumentDto(
        Guid Id,
        Guid? ParentFolderId,
        string? Name,
        string? ContentHash,
        string? Title,
        string? Content,
        DateTime CreatedAt);
}
