# ID allocator — shared test scenarios

Cross-repo reference for the allocator test matrix that AH7 / AH7.1
requires. Both `andy-tasks` (`PlanningSequenceAllocator`, GOAL-N /
TASK-N) and `andy-issues` (`BacklogSequenceAllocator`, EPIC-N /
FEATURE-N / STORY-N / ISSUE-N) follow the same allocator contract;
keeping the scenarios identical means drift between the two
implementations is visible in code review.

## The contract

For every entity type the allocator owns:

1. **Serial monotonicity** — N sequential `AllocateAsync(type)`
   calls produce exactly `1..N`, no duplicates, no gaps.
2. **Type independence** — interleaving multiple types never
   leaks a counter from one type into another. Each type's
   counter starts at 1 and increments only on its own
   allocations.
3. **Persistence across connections** — closing the `DbContext`
   and opening a new one must continue from `max(seq) + 1`. The
   counter row is the source of truth; in-memory state never is.
4. **Concurrency safety** — N concurrent workers each allocating
   M sequences produce `1..(N×M)` after sort, no duplicates, no
   gaps. The allocator uses `UPDATE...RETURNING` (Postgres) or
   the SQLite equivalent and never deadlocks under contention.
5. **Mixed-type concurrency** — N workers each racing to
   allocate one of every type must preserve per-type uniqueness
   without deadlocking. Counter rows must not share locks.
6. **Backfill correctness** *(Postgres only, integration-test
   layer)* — running the AH1 / AH2 backfill migration against an
   existing dataset assigns seqs in deterministic
   `ORDER BY CreatedAt ASC, Id ASC` order. Re-running the
   backfill is a no-op. The counter row ends at `max(seq) + 1`.

## Where the tests live

| Repo | File | Status |
|------|------|--------|
| andy-issues | `tests/Andy.Issues.Tests.Unit/Services/BacklogSequenceAllocatorConcurrencyTests.cs` | covers 1, 2, 4, 5 |
| andy-tasks | `tests/Andy.Tasks.Tests.Unit/Services/PlanningSequenceAllocatorConcurrencyTests.cs` | covers 1, 2, 4, 5 |
| andy-issues | `tests/Andy.Issues.Tests.Unit/Services/BacklogSequenceAllocatorTests.cs` | covers 3 |
| andy-tasks | `tests/Andy.Tasks.Tests.Unit/Services/PlanningSequenceAllocatorTests.cs` (and similar) | covers 3 |
| both | *(missing — see issue rivoli-ai/conductor#1679 AH7.1)* | (6) backfill correctness + idempotency — needs Postgres test container |

## SQLite vs Postgres

Unit-test concurrency runs against SQLite in file-mode (shared
cache). SQLite serializes writes via its file lock, which is the
more restrictive backend — anything that holds under SQLite
concurrency holds under Postgres' MVCC. The opposite isn't true
(Postgres can expose race windows SQLite hides), which is why
backfill-migration assertions belong in a Postgres integration
test rather than SQLite unit tests.

## Tenancy

Per-tenant scoping is a separate story (AH2.1 / rivoli-ai/conductor#1675).
Until that lands, every test runs with the implicit default
tenant. Cross-tenant isolation tests are intentionally omitted
because the contract they'd assert doesn't exist yet — adding
them before the schema change would just lock in the broken
shared-counter behavior.

## When to update this file

- A new entity type joins the allocator contract.
- A new scenario reveals a gap (audit, regression, code review).
- The two repos diverge in coverage — write a follow-up to bring
  the lagging side back in line, and update the table above.
