using System.Collections.Concurrent;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.ValueTypes;
namespace Andy.Issues.Tests.Integration.Fakes;

public sealed class FakeDocsClient : IDocsClient
{
    public ConcurrentDictionary<Guid, string> Content { get; } = new();
    public ConcurrentDictionary<Guid, (DocsRef Reference, Guid IssueId, Guid? RunId)> Links { get; } = new();
    public bool FailWrites { get; set; }
    public Task<bool> VerifyLinkAsync(Guid linkId, string expectedTargetType, Guid expectedTargetId, CancellationToken ct = default) =>
        Task.FromResult(linkId != Guid.Empty && expectedTargetId != Guid.Empty);
    public Task<DocsMetadata?> GetMetadataAsync(Guid documentId, CancellationToken ct = default) => Task.FromResult<DocsMetadata?>(null);
    public Task<string?> GetContentAsync(Guid documentId, CancellationToken ct = default) => Task.FromResult(Content.GetValueOrDefault(documentId));
    public Task<DocsRef?> PutTriageOutputAsync(Guid issueId, Guid? runId, string markdown, CancellationToken ct = default)
    {
        if (FailWrites) return Task.FromResult<DocsRef?>(null);
        var reference = new DocsRef(Guid.NewGuid(), Guid.NewGuid());
        Content[reference.DocumentId] = markdown; Links[reference.LinkId] = (reference, issueId, runId);
        return Task.FromResult<DocsRef?>(reference);
    }
    public Task<DocsRef?> LinkTriageOutputAsync(DocsRef reference, Guid issueId, Guid? runId, CancellationToken ct = default) =>
        Task.FromResult<DocsRef?>(!FailWrites && Links.TryGetValue(reference.LinkId, out var link) && link.Reference == reference && link.IssueId == issueId ? reference : null);
}
