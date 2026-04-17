// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.Enums;

/// <summary>
/// Which Azure identity flavour a <see cref="Entities.Repository"/> is
/// configured with. A repository has at most one kind configured at a
/// time; setting the PAT tuple clears the service-principal columns
/// and vice versa.
/// </summary>
public enum AzureIdentityKind
{
    None = 0,
    ServicePrincipal = 1,
    Pat = 2
}
