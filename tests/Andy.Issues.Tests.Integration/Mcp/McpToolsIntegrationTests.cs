// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text;
using System.Text.Json;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Mcp;

public class McpToolsIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public McpToolsIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task McpEndpoint_Initialize_RespondsWithCapabilities()
    {
        var (body, sessionId) = await SendJsonRpcRawAsync(null, "initialize", new
        {
            protocolVersion = "2025-03-26",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0.0" }
        }, id: 1);

        Assert.NotNull(sessionId);
        Assert.Contains("serverInfo", body);
    }

    [Fact]
    public async Task ToolsList_ContainsServiceTools()
    {
        var sessionId = await InitializeSessionAsync();
        var tools = await ListToolsAsync(sessionId);

        // MCP SDK converts PascalCase method names to snake_case
        var names = tools.Select(t => t.GetProperty("name").GetString()).ToList();

        Assert.Contains("list_repositories", names);
        Assert.Contains("create_repository", names);
        Assert.Contains("list_backlog", names);
        Assert.Contains("create_epic", names);
        Assert.Contains("create_sandbox", names);
        Assert.Contains("list_mcp_configs", names);
        Assert.Contains("list_enabled_artifact_feeds", names);
        Assert.Contains("generate_draft_backlog", names);
        // Z9 — triage tools
        Assert.Contains("issue_get", names);
        Assert.Contains("issue_list", names);
        Assert.Contains("issue_triage", names);
        // Help tools should also be present
        Assert.Contains("list_help_topics", names);
    }

    [Fact]
    public async Task CallTool_ListRepositories_ReturnsJson()
    {
        await SeedRepoAsync("mcp-list-test");
        var sessionId = await InitializeSessionAsync();

        var result = await CallToolAsync(sessionId, "list_repositories", new
        {
            scope = "mine",
            page = 1,
            pageSize = 50
        });

        Assert.Contains("mcp-list-test", result);
    }

    [Fact]
    public async Task CallTool_GetRepository_ReturnsJson()
    {
        var repoId = await SeedRepoAsync("mcp-get-test");
        var sessionId = await InitializeSessionAsync();

        var result = await CallToolAsync(sessionId, "get_repository", new
        {
            repositoryId = repoId.ToString()
        });

        Assert.Contains("mcp-get-test", result);
    }

    [Fact]
    public async Task CallTool_GetRepository_NotFound_ReturnsMessage()
    {
        var sessionId = await InitializeSessionAsync();

        var result = await CallToolAsync(sessionId, "get_repository", new
        {
            repositoryId = Guid.NewGuid().ToString()
        });

        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallTool_CreateEpic_CreatesAndReturnsJson()
    {
        var repoId = await SeedRepoAsync("mcp-epic-test");
        var sessionId = await InitializeSessionAsync();

        var result = await CallToolAsync(sessionId, "create_epic", new
        {
            repositoryId = repoId.ToString(),
            title = "MCP-Created Epic",
            description = "Created via MCP tool"
        });

        Assert.Contains("MCP-Created Epic", result);
    }

    [Fact]
    public async Task CallTool_CreateRepository_CreatesRow()
    {
        var sessionId = await InitializeSessionAsync();
        var cloneUrl = $"https://github.com/acme/mcp-created-{Guid.NewGuid():N}.git";

        var created = await CallToolAsync(sessionId, "create_repository", new
        {
            name = "mcp-created",
            provider = "github",
            cloneUrl,
            description = (string?)null,
            defaultBranch = (string?)null,
            externalId = (string?)null
        });

        Assert.Contains("mcp-created", created);

        var list = await CallToolAsync(sessionId, "list_repositories", new
        {
            scope = "mine",
            page = 1,
            pageSize = 50
        });
        Assert.Contains(cloneUrl, list);
    }

    [Fact]
    public async Task CallTool_CreateRepository_DuplicateIsIdempotent()
    {
        var sessionId = await InitializeSessionAsync();
        var cloneUrl = $"https://github.com/acme/dup-{Guid.NewGuid():N}.git";

        var first = await CallToolAsync(sessionId, "create_repository", new
        {
            name = "dup-repo",
            provider = "github",
            cloneUrl,
            description = (string?)null,
            defaultBranch = (string?)null,
            externalId = (string?)null
        });
        var second = await CallToolAsync(sessionId, "create_repository", new
        {
            name = "dup-repo",
            provider = "github",
            cloneUrl,
            description = (string?)null,
            defaultBranch = (string?)null,
            externalId = (string?)null
        });

        // Both calls return the same repository record.
        var firstId = ExtractId(first);
        var secondId = ExtractId(second);
        Assert.Equal(firstId, secondId);
    }

    private static string ExtractId(string toolOutput)
    {
        using var doc = JsonDocument.Parse(toolOutput);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task CallTool_DeleteRepository_ReturnsDeleted()
    {
        var repoId = await SeedRepoAsync("mcp-delete-test");
        var sessionId = await InitializeSessionAsync();

        var result = await CallToolAsync(sessionId, "delete_repository", new
        {
            repositoryId = repoId.ToString()
        });

        Assert.Contains("deleted", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallTool_ListSandboxes_ReturnsJson()
    {
        var sessionId = await InitializeSessionAsync();

        var result = await CallToolAsync(sessionId, "list_sandboxes", new { });

        Assert.NotNull(result);
    }

    // ── Issues / Triage (Z9) ────────────────────────────────────────

    [Fact]
    public async Task CallTool_IssueList_ReturnsPagedResult()
    {
        await SeedIssueAsync("mcp-list-issue");
        var sessionId = await InitializeSessionAsync();

        var result = await CallToolAsync(sessionId, "issue_list", new
        {
            triageState = (string?)null,
            page = 1,
            pageSize = 50
        });

        Assert.Contains("mcp-list-issue", result);
        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.GetProperty("totalCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task CallTool_IssueGet_ReturnsIssue()
    {
        var issueId = await SeedIssueAsync("mcp-get-issue");
        var sessionId = await InitializeSessionAsync();

        var result = await CallToolAsync(sessionId, "issue_get", new
        {
            issueId = issueId.ToString()
        });

        Assert.Contains("mcp-get-issue", result);
    }

    [Fact]
    public async Task CallTool_IssueTriage_TransitionsToTriaging()
    {
        var issueId = await SeedIssueAsync("mcp-triage-issue");
        var sessionId = await InitializeSessionAsync();

        var result = await CallToolAsync(sessionId, "issue_triage", new
        {
            issueId = issueId.ToString()
        });

        Assert.Contains("Triaging", result);
    }

    [Fact]
    public async Task CallTool_IssueTriage_FromInvalidState_ReturnsErrorString()
    {
        // Create issue in Accepted state — start_triage from there is invalid.
        var issueId = await SeedIssueAsync("mcp-bad-triage", state: TriageState.Accepted);
        var sessionId = await InitializeSessionAsync();

        var result = await CallToolAsync(sessionId, "issue_triage", new
        {
            issueId = issueId.ToString()
        });

        Assert.Contains("Invalid triage transition", result);
    }

    [Fact]
    public async Task CallTool_ListEnabledArtifactFeeds_ReturnsJson()
    {
        var sessionId = await InitializeSessionAsync();

        var result = await CallToolAsync(sessionId, "list_enabled_artifact_feeds", new { });

        Assert.NotNull(result);
    }

    // ── MCP Protocol Helpers ────────────────────────────────────────

    private async Task<string> InitializeSessionAsync()
    {
        var response = await SendJsonRpcAsync(null, "initialize", new
        {
            protocolVersion = "2025-03-26",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0.0" }
        }, id: 1);

        var sessionId = response.sessionId;

        // Send initialized notification
        await SendNotificationAsync(sessionId, "notifications/initialized");

        return sessionId!;
    }

    private async Task<List<JsonElement>> ListToolsAsync(string sessionId)
    {
        var response = await SendJsonRpcAsync(sessionId, "tools/list", new { }, id: 2);
        var result = response.body.GetProperty("result");
        return result.GetProperty("tools").EnumerateArray().ToList();
    }

    private async Task<string> CallToolAsync(string sessionId, string toolName, object arguments)
    {
        var response = await SendJsonRpcAsync(sessionId, "tools/call", new
        {
            name = toolName,
            arguments
        }, id: 3);

        var result = response.body.GetProperty("result");
        var content = result.GetProperty("content").EnumerateArray().First();
        return content.GetProperty("text").GetString()!;
    }

    private async Task<(string body, string? sessionId)> SendJsonRpcRawAsync(
        string? sessionId, string method, object @params, int id)
    {
        var payload = new { jsonrpc = "2.0", id, method, @params };

        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (sessionId is not null)
            request.Headers.Add("Mcp-Session-Id", sessionId);

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var newSessionId = response.Headers.TryGetValues("Mcp-Session-Id", out var values)
            ? values.First()
            : sessionId;

        var body = await response.Content.ReadAsStringAsync();
        return (body, newSessionId);
    }

    private async Task<(JsonElement body, string? sessionId)> SendJsonRpcAsync(
        string? sessionId, string method, object @params, int id)
    {
        var payload = new { jsonrpc = "2.0", id, method, @params };

        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (sessionId is not null)
            request.Headers.Add("Mcp-Session-Id", sessionId);

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var newSessionId = response.Headers.TryGetValues("Mcp-Session-Id", out var values)
            ? values.First()
            : sessionId;

        var contentType = response.Content.Headers.ContentType?.MediaType;

        if (contentType == "text/event-stream")
        {
            var body = await ParseSseResponseAsync(response);
            return (body, newSessionId);
        }

        // Direct JSON response
        var json = await response.Content.ReadAsStringAsync();
        return (JsonDocument.Parse(json).RootElement, newSessionId);
    }

    private async Task SendNotificationAsync(string? sessionId, string method)
    {
        var payload = new { jsonrpc = "2.0", method };

        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };
        if (sessionId is not null)
            request.Headers.Add("Mcp-Session-Id", sessionId);

        await _client.SendAsync(request);
    }

    private static async Task<JsonElement> ParseSseResponseAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        var lastData = string.Empty;

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (line is null) break;
            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                var data = line[6..];
                if (data.Contains("\"result\"") || data.Contains("\"error\""))
                    lastData = data;
            }
        }

        return JsonDocument.Parse(lastData).RootElement;
    }

    private async Task<Guid> SeedIssueAsync(string title, TriageState state = TriageState.NeedsTriage)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var issue = new Issue
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "dev-user",
            Title = title
        };
        db.Issues.Add(issue);
        await db.SaveChangesAsync();

        // Drive the entity through StartTriage / CompleteTriage etc. to
        // reach the requested state — the entity rejects illegal jumps.
        if (state != TriageState.NeedsTriage)
        {
            issue.StartTriage();
            if (state != TriageState.Triaging)
            {
                issue.CompleteTriage("dev-user");
                if (state == TriageState.Accepted) issue.Accept("dev-user");
                else if (state == TriageState.Rejected) issue.Reject("dev-user");
            }
            await db.SaveChangesAsync();
        }

        return issue.Id;
    }

    private async Task<Guid> SeedRepoAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "dev-user",
            Name = name,
            CloneUrl = $"https://github.com/test/{name}.git"
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();
        return repo.Id;
    }
}
