// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.Entities;

// Lightweight local projection of users the service has seen. Populated
// lazily from JWT claims on incoming requests (Story 2.8) so we can resolve
// an email or display name back to a stable subject id without calling out
// to Andy Auth on every share/lookup.
public class UserDirectoryEntry
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
