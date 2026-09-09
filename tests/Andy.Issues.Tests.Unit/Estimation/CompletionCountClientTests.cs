using System.Net;
using System.Net.Http.Json;
using Andy.Issues.Infrastructure.Estimation;
using Xunit;

namespace Andy.Issues.Tests.Unit.Estimation;

public sealed class CompletionCountClientTests
{
    [Fact]
    public async Task CountRead_PreservesProxyPrefix_AndEscapesTenantAndTemplate()
    {
        using var handler = new Reply();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://proxy.example/tasks/") };
        var service = new CompletionCountClient(new Factory(client));
        Assert.Equal(10, await service.GetAsync("owner+test", "andy-issues:bug", default));
        Assert.Equal("https://proxy.example/tasks/api/tenants/owner%2Btest/templates/andy-issues%3Abug/completion-count", handler.Url);
        handler.Body = "{}";
        Assert.Null(await service.GetAsync("owner", "feature", default));
        handler.Code = HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetAsync("owner", "feature", default));
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) { Assert.Equal("AndyTasksEstimates", name); return client; }
    }
    private sealed class Reply : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string Body { get; set; } = "{\"completion_count\":10}";
        public HttpStatusCode Code { get; set; } = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(Code) { Content = new StringContent(Body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
