// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Messaging.Events;

// Payloads for andy.issues.events.repository.{repoId}.{kind} events.
// Two shapes because the kinds carry different information:
//   - Registered: snapshot of the freshly-created repository row.
//   - Synced:     per-repo result of a batch sync operation, with the
//                 aggregate counts of the whole batch for context.

public sealed record RepositoryRegisteredPayload(
    Guid RepositoryId,
    string Provider,
    string Name,
    string CloneUrl)
{
    public const int SchemaVersion = 1;
    public int Schema_Version => SchemaVersion;
}

public sealed record RepositorySyncedPayload(
    Guid RepositoryId,
    string Provider,
    int Added,
    int Updated,
    int Skipped,
    int ErrorCount)
{
    public const int SchemaVersion = 1;
    public int Schema_Version => SchemaVersion;
}

public enum RepositoryEventKind
{
    Registered,
    Synced
}

public static class RepositoryEventKindExtensions
{
    public static string ToSubjectKind(this RepositoryEventKind kind) => kind switch
    {
        RepositoryEventKind.Registered => "registered",
        RepositoryEventKind.Synced => "synced",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
