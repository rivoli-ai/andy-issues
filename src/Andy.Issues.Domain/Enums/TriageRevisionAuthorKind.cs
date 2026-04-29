// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.Enums;

// Z5 — who produced a revision of the triage output. Drives audit
// (Conductor's AB6 chat panel renders agent vs. human attributions
// differently) and the `revised` event emission rule (only Human
// revisions emit `andy.issues.events.issue.<id>.revised`; Agent
// revisions are subsumed by the existing `triaged` event).
public enum TriageRevisionAuthorKind
{
    Agent = 0,
    Human = 1
}
