// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;

namespace Andy.Issues.Domain.Entities;

public class Sandbox
{
    public Guid Id { get; set; }
    public Guid ContainerId { get; set; }
    public Guid RepositoryId { get; set; }
    public Repository Repository { get; set; } = null!;
    public string OwnerUserId { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public SandboxStatus Status { get; set; } = SandboxStatus.Pending;
    public string? IdeEndpoint { get; set; }
    public string? VncEndpoint { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
