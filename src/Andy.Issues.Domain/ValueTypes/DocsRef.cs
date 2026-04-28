// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.ValueTypes;

// Pointer into andy-docs (AJ2 / AE1 reconciliation). `DocumentId` is
// the content reference; `LinkId` is the typed link row that scopes
// this reference to a specific Issue/Goal/etc. Z8 attaches DocsRefs to
// Issues; Z3 echoes them inside the triage output so consumers (Goal
// creation in andy-tasks) inherit the same input set without a second
// lookup.
//
// Lives in Domain rather than Shared until andy-docs publishes the
// canonical type as a nuget — flagged in the Z3 PR for eventual
// migration.
public readonly record struct DocsRef(Guid DocumentId, Guid LinkId);
