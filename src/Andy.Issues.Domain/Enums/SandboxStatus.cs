// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.Enums;

public enum SandboxStatus
{
    Pending = 0,
    Creating = 1,
    Running = 2,
    Stopped = 3,
    Failed = 4,
    Destroying = 5,
    Destroyed = 6
}
