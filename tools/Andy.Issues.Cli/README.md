# andy-issues-cli

Per-service CLI for andy-issues. Federated by `andy-conductor-cli` (in `rivoli-ai/conductor`); do not confuse with `rivoli-ai/andy-cli`.

## Global flags

```
--api-url <url>     Andy Issues API base URL (default: https://localhost:5410)
--token   <jwt>     Bearer token for authentication
--help              Per-command help
```

## Commands

### `repos`
Manage repositories. See `andy-issues-cli repos --help`.

### `backlog`
Manage epics, features, stories. See `andy-issues-cli backlog --help`.

### `issues` (Z10)
Triage lifecycle for an `Issue`. See [features.md](../../docs/features.md#triage-workflow) for the state machine.

```
andy-issues-cli issues list [--triage-state=<state>] [--page=N] [--page-size=N] [--json]
andy-issues-cli issues get  <id> [--json]
andy-issues-cli issues triage <id> [--json]
```

- `list` — paginated owner-scoped list. `--triage-state` is case-insensitive (`NeedsTriage`, `Triaging`, `Triaged`, `Accepted`, `Rejected`); unknown values yield an empty page.
- `get` — print one issue, including triage attribution if present.
- `triage` — re-invoke triage. Allowed from `NeedsTriage` or `Triaged`. Z2 has shipped — the state transition is followed by an asynchronous agent run via `IContainersClient`; the `ContainerRunEventConsumer` correlates the run-finished event back to the issue and writes the resulting triage output.

### `sandbox`
Manage sandboxes. See `andy-issues-cli sandbox --help`.

### `mcp`
Manage MCP server configurations. See `andy-issues-cli mcp --help`.

### `artifact-feeds`
Manage artifact feeds. See `andy-issues-cli artifact-feeds --help`.
