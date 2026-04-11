// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;

namespace Andy.Issues.Domain.Entities;

public class Repository
{
    public Guid Id { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public RepositoryProvider Provider { get; set; }
    public string CloneUrl { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = "main";
    public string? ExternalId { get; set; }

    public Guid? LlmSettingId { get; set; }
    public LlmSetting? LlmSetting { get; set; }

    public string? AzureClientId { get; set; }
    public string? AzureClientSecret { get; set; }
    public string? AzureTenantId { get; set; }
    public string? AzureSubscriptionId { get; set; }

    public CodeIndexStatus CodeIndexStatus { get; set; } = CodeIndexStatus.NotIndexed;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public List<RepositoryShare> Shares { get; set; } = new();
    public List<Epic> Epics { get; set; } = new();

    public bool HasAzureIdentity =>
        !string.IsNullOrEmpty(AzureClientId) &&
        !string.IsNullOrEmpty(AzureClientSecret) &&
        !string.IsNullOrEmpty(AzureTenantId);

    public RepositoryShare AddShare(string sharedWithUserId, string grantedByUserId)
    {
        var existing = Shares.FirstOrDefault(s => s.SharedWithUserId == sharedWithUserId);
        if (existing is not null)
            return existing;

        var share = new RepositoryShare
        {
            Id = Guid.NewGuid(),
            RepositoryId = Id,
            SharedWithUserId = sharedWithUserId,
            GrantedByUserId = grantedByUserId,
            GrantedAt = DateTimeOffset.UtcNow
        };
        Shares.Add(share);
        return share;
    }
}
