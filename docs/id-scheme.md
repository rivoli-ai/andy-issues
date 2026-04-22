# Backlog ID scheme

**AH1** (rivoli-ai/conductor#708). Every backlog entity now carries a short, human-readable identifier alongside its canonical GUID.

## Shape

| Entity       | Format       | Example      |
|--------------|--------------|--------------|
| `Epic`       | `EPIC-{seq}` | `EPIC-42`    |
| `Feature`    | `FEAT-{seq}` | `FEAT-7`     |
| `UserStory`  | `STORY-{seq}`| `STORY-13`   |

- `{seq}` is a `BIGINT` starting at 1, monotonically increasing per entity type.
- Each type has its own counter; the three namespaces are independent.
- The GUID (`Id`) remains the canonical primary key. `DisplayId` is an additive projection — never a replacement.
- Immutable once assigned. Enforced at the domain layer by a read-only computed getter (`DisplayId`) projected from an `internal` `Seq` setter.

## Scope

IDs are **globally scoped** per entity type. There is no tenant partition today — the codebase has no tenant boundary on backlog rows (only `Repository.AzureTenantId` for Azure DevOps OAuth). A tenant partition is a follow-up story that lands once AL-auth brings first-class tenancy.

## Resolver rules

Every controller endpoint that accepts `{id}` — including both existing GUID routes and the new `GET /api/{epics,features,stories}/{identifier}` routes — parses the route segment as:

1. Try `Guid.TryParse` — if it matches, treat as canonical id.
2. Otherwise split on the first `-`; match the prefix against `EPIC` / `FEAT` / `STORY` (case-insensitive); parse the suffix as a positive `long`; look up by `Seq` scoped to the matching entity type.
3. Anything else is `404`.

See [`BacklogIdentifier.Parse`](../src/Andy.Issues.Application/BacklogIdentifier.cs) for the canonical implementation.

## Allocation

IDs are allocated inside the insert's transaction via `IBacklogSequenceAllocator.AllocateAsync(BacklogEntityType)`. The Infrastructure implementation runs a single atomic `UPDATE backlog_sequences SET next_seq = next_seq + 1 WHERE entity_type = @t RETURNING next_seq - 1`, which works uniformly on Postgres and SQLite 3.35+.

Callers must be inside a transaction — a rollback-only-the-insert leaves a gap in the sequence. All current callers (`BacklogService`, `BacklogGitHubImportService`, `DraftBacklogGenerator`) save-and-commit right after allocation so this invariant holds.

## Migration backfill

The `AddBacklogDisplayIds` migration:
1. Adds `Seq BIGINT NOT NULL` to `Epics` / `Features` / `UserStories` (default 0)
2. Creates `backlog_sequences(entity_type INT PK, next_seq BIGINT)`
3. Backfills existing rows via `row_number() OVER (ORDER BY CreatedAt ASC, Id ASC)` — tiebreaking on `Id` guarantees determinism when timestamps collide (e.g., fixture imports)
4. Seeds the three counter rows to `COALESCE(MAX(Seq), 0) + 1` per type
5. Creates the unique indexes on `Seq` (after backfill, so no collision on the default 0)

The embedded SQLite path uses `EnsureCreatedAsync` rather than migrations, and picks the schema up from `AppDbContext` directly — no backfill needed because embedded mode starts fresh.

## Event payloads

`StoryEventPayload` (ADR 0001) now carries a nullable `display_id` field alongside the existing GUID identifiers. Older consumers that ignored unknown fields keep working; pre-AH1 outbox rows replayed from disk decode cleanly because the field is optional.

## Deep links

Reserved scheme: `conductor://{kind}/{display-id}` — e.g., `conductor://stories/STORY-13`. Conductor's deep-link handler will route these in a separate story.

## Out of scope for AH1

- Per-tenant partitioning (blocked on AL-auth)
- Cross-service linkage pinning issue ids on goals (AH6 / rivoli-ai/conductor#713)
- `conductor://` deep-link handler (conductor-side, separate story)
