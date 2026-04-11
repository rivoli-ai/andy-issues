// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Entities;
using Xunit;

namespace Andy.Issues.Tests.Unit.Entities;

public class RepositoryTests
{
    [Fact]
    public void AddShare_IsIdempotent_ForSameUser()
    {
        var repo = new Repository { Id = Guid.NewGuid(), OwnerUserId = "owner" };

        var first = repo.AddShare("alice", "owner");
        var second = repo.AddShare("alice", "owner");

        Assert.Single(repo.Shares);
        Assert.Same(first, second);
    }

    [Fact]
    public void AddShare_AddsDistinctSharesForDifferentUsers()
    {
        var repo = new Repository { Id = Guid.NewGuid(), OwnerUserId = "owner" };

        repo.AddShare("alice", "owner");
        repo.AddShare("bob", "owner");

        Assert.Equal(2, repo.Shares.Count);
    }

    [Fact]
    public void HasAzureIdentity_ReturnsFalse_WhenAnyFieldIsMissing()
    {
        var repo = new Repository
        {
            AzureClientId = "cid",
            AzureClientSecret = "secret",
            // TenantId missing
        };

        Assert.False(repo.HasAzureIdentity);
    }

    [Fact]
    public void HasAzureIdentity_ReturnsTrue_WhenAllRequiredFieldsSet()
    {
        var repo = new Repository
        {
            AzureClientId = "cid",
            AzureClientSecret = "secret",
            AzureTenantId = "tenant"
        };

        Assert.True(repo.HasAzureIdentity);
    }
}
