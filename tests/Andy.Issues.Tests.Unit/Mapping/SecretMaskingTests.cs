// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Reflection;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Mapping;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Xunit;

namespace Andy.Issues.Tests.Unit.Mapping;

public class SecretMaskingTests
{
    [Fact]
    public void RepositoryDto_DoesNotExposeAzureSecret()
    {
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "owner",
            Name = "demo",
            Provider = RepositoryProvider.GitHub,
            CloneUrl = "https://example.com/demo.git",
            AzureClientId = "cid",
            AzureClientSecret = "VERY-SECRET",
            AzureTenantId = "tenant"
        };

        var dto = repo.ToDto();

        Assert.True(dto.HasAzureIdentity);
        // The record's property list must not contain any "Secret" / "ClientId" field.
        var propNames = typeof(RepositoryDto).GetProperties()
            .Select(p => p.Name).ToList();
        Assert.DoesNotContain("AzureClientSecret", propNames);
        Assert.DoesNotContain("AzureClientId", propNames);
        Assert.DoesNotContain("AzureTenantId", propNames);
        Assert.DoesNotContain("AzureSubscriptionId", propNames);
    }

    [Fact]
    public void McpServerConfigDto_MasksEnvironmentAndHeaders()
    {
        var cfg = new McpServerConfig
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "u",
            Name = "remote-srv",
            Type = McpServerType.Remote,
            Url = "https://mcp.example.com",
            EnvironmentJson = "{\"TOKEN\":\"very-secret\"}",
            HeadersJson = "{\"Authorization\":\"Bearer xyz\"}"
        };

        var dto = cfg.ToDto();

        // The outbound DTO only carries presence booleans, not the values.
        Assert.True(dto.HasEnvironment);
        Assert.True(dto.HasHeaders);
        var propNames = typeof(McpServerConfigDto).GetProperties()
            .Select(p => p.Name).ToList();
        Assert.DoesNotContain("EnvironmentJson", propNames);
        Assert.DoesNotContain("HeadersJson", propNames);
    }

    [Fact]
    public void McpServerConfigDto_HasEnvironmentFalse_WhenEmptyJson()
    {
        var cfg = new McpServerConfig
        {
            Id = Guid.NewGuid(),
            Name = "empty",
            Type = McpServerType.Stdio,
            EnvironmentJson = null,
            HeadersJson = ""
        };

        var dto = cfg.ToDto();
        Assert.False(dto.HasEnvironment);
        Assert.False(dto.HasHeaders);
    }

    [Fact]
    public void LinkedProviderDto_DoesNotExposeTokens()
    {
        var linked = new LinkedProvider
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "u",
            Provider = LinkedProviderKind.GitHub,
            AccessToken = "ghp_SECRET",
            RefreshToken = "rtok_SECRET"
        };

        var dto = linked.ToDto();
        var propNames = typeof(LinkedProviderDto).GetProperties()
            .Select(p => p.Name).ToList();
        Assert.DoesNotContain("AccessToken", propNames);
        Assert.DoesNotContain("RefreshToken", propNames);

        // And no string field anywhere in the DTO contains the secret value.
        var props = typeof(LinkedProviderDto).GetProperties(
            BindingFlags.Public | BindingFlags.Instance);
        foreach (var p in props)
        {
            var value = p.GetValue(dto)?.ToString();
            if (value is null) continue;
            Assert.DoesNotContain("SECRET", value);
        }
    }

    [Fact]
    public void LlmSettingDto_DoesNotExposeApiKey()
    {
        var llm = new LlmSetting
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "u",
            Name = "default",
            Provider = LlmProvider.Anthropic,
            ApiKey = "sk-ant-SECRET",
            Model = "claude-opus-4-6"
        };

        var dto = llm.ToDto();
        var propNames = typeof(LlmSettingDto).GetProperties()
            .Select(p => p.Name).ToList();
        Assert.DoesNotContain("ApiKey", propNames);
    }
}
