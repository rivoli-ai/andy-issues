// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.Entities;

public class RepositoryShare
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public Repository Repository { get; set; } = null!;
    public string SharedWithUserId { get; set; } = string.Empty;
    public string GrantedByUserId { get; set; } = string.Empty;
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
}
