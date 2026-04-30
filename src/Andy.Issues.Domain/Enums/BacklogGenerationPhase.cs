// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.Enums;

// #103 — phases the draft-backlog generator passes through. The
// terminal phases (Completed, Failed, Cancelled) carry CompletedAt
// on the BacklogGeneration row; intermediate phases bump UpdatedAt
// so connected clients can render a live progress UI.
//
// `Cancelled` is reserved for the future cancel endpoint — today no
// caller transitions into it.
public enum BacklogGenerationPhase
{
    Pending = 0,
    FetchingCodeIndex = 1,
    CallingLlm = 2,
    ParsingDraft = 3,
    Persisting = 4,
    Completed = 5,
    Failed = 6,
    Cancelled = 7
}
