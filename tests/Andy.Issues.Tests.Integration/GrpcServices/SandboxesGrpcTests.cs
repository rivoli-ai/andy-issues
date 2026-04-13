// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Api.Protos;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.GrpcServices;

public class SandboxesGrpcTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private readonly TestWebApplicationFactory _factory;
    private readonly GrpcChannel _channel;
    private readonly SandboxesService.SandboxesServiceClient _client;

    public SandboxesGrpcTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        var httpClient = factory.CreateDefaultClient();
        _channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient
        });
        _client = new SandboxesService.SandboxesServiceClient(_channel);
    }

    public void Dispose()
    {
        _channel.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Create_ReturnsSandbox()
    {
        var repoId = await SeedRepoAsync();

        var sandbox = await _client.CreateAsync(new CreateSandboxGrpcRequest
        {
            RepositoryId = repoId.ToString(),
            Branch = "feature/grpc-test"
        });

        Assert.NotEmpty(sandbox.Id);
        Assert.Equal(repoId.ToString(), sandbox.RepositoryId);
        Assert.Equal("feature/grpc-test", sandbox.Branch);
    }

    [Fact]
    public async Task List_ReturnsSandboxes()
    {
        var repoId = await SeedRepoAsync();
        await _client.CreateAsync(new CreateSandboxGrpcRequest
        {
            RepositoryId = repoId.ToString(),
            Branch = "feature/list-test"
        });

        var response = await _client.ListAsync(new ListSandboxesRequest());

        Assert.True(response.Sandboxes.Count >= 1);
    }

    [Fact]
    public async Task Destroy_ReturnsTrue()
    {
        var repoId = await SeedRepoAsync();
        var sandbox = await _client.CreateAsync(new CreateSandboxGrpcRequest
        {
            RepositoryId = repoId.ToString(),
            Branch = "feature/destroy-test"
        });

        var result = await _client.DestroyAsync(new DestroySandboxRequest { Id = sandbox.Id });

        Assert.True(result.Destroyed);
    }

    [Fact]
    public async Task Get_NotFound_ThrowsRpcException()
    {
        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() =>
            _client.GetAsync(new GetSandboxRequest { Id = Guid.NewGuid().ToString() }).ResponseAsync);

        Assert.Equal(Grpc.Core.StatusCode.NotFound, ex.StatusCode);
    }

    private async Task<Guid> SeedRepoAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "dev-user",
            Name = $"sandbox-grpc-{Guid.NewGuid():N}",
            CloneUrl = $"https://github.com/test/{Guid.NewGuid()}.git"
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();
        return repo.Id;
    }
}
