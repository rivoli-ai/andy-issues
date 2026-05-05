// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.PullRequests;
using Xunit;

namespace Andy.Issues.Tests.Unit.PullRequests;

public class PullRequestUrlParserTests
{
    [Theory]
    [InlineData("https://github.com/acme/widget/pull/42", "acme", "widget", 42)]
    [InlineData("https://github.com/Acme-Org/Wid_get/pull/1", "Acme-Org", "Wid_get", 1)]
    public void GitHub_PullUrl_Parses(string url, string owner, string repo, int n)
    {
        var parsed = PullRequestUrlParser.TryParse(url);
        var gh = Assert.IsType<ParsedGitHubPullRequestUrl>(parsed);
        Assert.Equal(owner, gh.Owner);
        Assert.Equal(repo, gh.Repo);
        Assert.Equal(n, gh.Number);
    }

    [Theory]
    [InlineData(
        "https://dev.azure.com/myorg/myproject/_git/myrepo/pullrequest/17",
        "myorg", "myproject", "myrepo", 17)]
    public void AzureDevOps_Cloud_PullUrl_Parses(
        string url, string org, string project, string repo, int n)
    {
        var parsed = PullRequestUrlParser.TryParse(url);
        var ado = Assert.IsType<ParsedAzureDevOpsPullRequestUrl>(parsed);
        Assert.Equal(org, ado.Organization);
        Assert.Equal(project, ado.Project);
        Assert.Equal(repo, ado.Repository);
        Assert.Equal(n, ado.Number);
    }

    [Theory]
    [InlineData(
        "https://contoso.visualstudio.com/myproject/_git/myrepo/pullrequest/9",
        "contoso", "myproject", "myrepo", 9)]
    public void AzureDevOps_Legacy_PullUrl_Parses(
        string url, string org, string project, string repo, int n)
    {
        var parsed = PullRequestUrlParser.TryParse(url);
        var ado = Assert.IsType<ParsedAzureDevOpsPullRequestUrl>(parsed);
        Assert.Equal(org, ado.Organization);
        Assert.Equal(project, ado.Project);
        Assert.Equal(repo, ado.Repository);
        Assert.Equal(n, ado.Number);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a url")]
    [InlineData("https://gitlab.com/acme/widget/-/merge_requests/42")]
    [InlineData("https://github.com/acme/widget/issues/42")]
    [InlineData("https://github.com/acme/widget/pull/abc")]
    [InlineData("ftp://github.com/acme/widget/pull/42")]
    public void Unrecognised_ReturnsNull(string? url)
    {
        Assert.Null(PullRequestUrlParser.TryParse(url));
    }
}
