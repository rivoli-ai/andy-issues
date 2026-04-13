// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Api.Protos;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.GrpcServices;

public class RepositoriesGrpcTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private readonly TestWebApplicationFactory _factory;
    private readonly GrpcChannel _channel;
    private readonly RepositoriesService.RepositoriesServiceClient _client;

    public RepositoriesGrpcTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        var httpClient = factory.CreateDefaultClient();
        _channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient
        });
        _client = new RepositoriesService.RepositoriesServiceClient(_channel);
    }

    public void Dispose()
    {
        _channel.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task List_ReturnsRepositories()
    {
        await SeedRepoAsync("grpc-list-repo");

        var response = await _client.ListAsync(new ListRepositoriesRequest
        {
            Scope = "mine",
            Page = 1,
            PageSize = 50
        });

        Assert.True(response.Items.Count >= 1);
    }

    [Fact]
    public async Task Get_ReturnsRepository()
    {
        var id = await SeedRepoAsync("grpc-get-repo");

        var response = await _client.GetAsync(new GetRepositoryRequest { Id = id.ToString() });

        Assert.Equal("grpc-get-repo", response.Repository.Name);
    }

    [Fact]
    public async Task Get_NotFound_ThrowsRpcException()
    {
        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() =>
            _client.GetAsync(new GetRepositoryRequest { Id = Guid.NewGuid().ToString() }).ResponseAsync);

        Assert.Equal(Grpc.Core.StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Delete_ReturnsDeletedTrue()
    {
        var id = await SeedRepoAsync("grpc-delete-repo");

        var response = await _client.DeleteAsync(new DeleteRepositoryRequest { Id = id.ToString() });

        Assert.True(response.Deleted);
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
