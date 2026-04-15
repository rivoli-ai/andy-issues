// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Messaging.Events;

// Payload for andy.issues.events.sandbox.{sandboxId}.{kind} events.
// Reason is populated only on the `failed` kind; serialised as
// snake_case via EventJson.Options and omitted when null.
public sealed record SandboxEventPayload(
    Guid SandboxId,
    string ContainerId,
    Guid RepositoryId,
    string Branch,
    string Status,
    string? Reason = null)
{
    public const int SchemaVersion = 1;
    public int Schema_Version => SchemaVersion;
}

// Three kinds:
//   Attached  — fresh create, andy-containers confirmed the container exists
//   Detached  — explicit destroy, or remote disappearance detected by refresh
//   Failed    — remote reports a failed/errored state; carries Reason
public enum SandboxEventKind
{
    Attached,
    Detached,
    Failed
}

public static class SandboxEventKindExtensions
{
    public static string ToSubjectKind(this SandboxEventKind kind) => kind switch
    {
        SandboxEventKind.Attached => "attached",
        SandboxEventKind.Detached => "detached",
        SandboxEventKind.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
