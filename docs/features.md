# Features

## API

- **REST API** - Full CRUD operations with Swagger/OpenAPI documentation
- **gRPC** - High-performance RPC for service-to-service communication
- **MCP** - Model Context Protocol tools for AI assistant integration

## Frontend

- **Angular SPA** - Modern single-page application built with Angular 18
- **OIDC Authentication** - Integrated with Andy Auth for SSO
- **Responsive Design** - Works across desktop and mobile

## Security

- **OAuth2/OIDC** - Authentication via Andy Auth server
- **RBAC** - Role-based access control via Andy RBAC
- **HTTPS Everywhere** - TLS from development to production
- **JWT Bearer** - API authentication with JWT tokens

## Data

- **PostgreSQL** - Primary database for production and development
- **SQLite** - Embedded database for Conductor integration
- **EF Core** - Entity Framework Core for data access with migrations

## Operations

- **OpenTelemetry** - Distributed tracing, metrics, and logging
- **Docker** - Containerized deployment with multi-stage builds
- **Health Checks** - Built-in health endpoint at `/health`
- **CI/CD** - GitHub Actions for build, test, and deployment

## CLI

- **Command-line interface** - Manage resources from the terminal
- **Token-based auth** - Works with Andy Auth Bearer tokens

## Story workflow

User stories progress through a fixed set of statuses: `Draft → Ready → InProgress → InReview → Done`. Fresh stories start in `Draft`.

Clients advance a story via `PATCH /api/stories/{id}/status` with a JSON body:

```json
{ "status": "InReview", "pullRequestUrl": "https://github.com/org/repo/pull/42" }
```

- The server enforces one transition rule: `Done → Draft` is rejected (use a different target status to re-open). All other transitions are allowed so re-work loops remain possible.
- `pullRequestUrl` is optional; supplying it attaches or replaces the link on the story. Omitting it leaves any existing URL untouched.
- Responses:
  - `200 OK` with the updated story DTO on success.
  - `400 Bad Request` if `status` is not a recognized enum value.
  - `409 Conflict` if the transition is forbidden (currently only `Done → Draft`).
  - `404 Not Found` if the story does not exist or the caller cannot see the owning repository.
- Each successful update emits a `BoardHub` SignalR event so board views refresh live (see Story 3.4).

## Triage workflow

The `Issue` entity tracks an intake envelope through triage before it becomes a backlog item. Fresh issues start in `NeedsTriage`. See [architecture.md](architecture.md#triage-lifecycle) for the state diagram.

| Endpoint | Allowed from | Effect |
|---|---|---|
| `POST /api/triage` | — | Creates a new `Issue` in `NeedsTriage`. |
| `GET /api/triage/{id}` | any | Returns the issue (owner-scoped). |
| `POST /api/triage/{id}/start` | `NeedsTriage`, `Triaged` | Moves to `Triaging` (initial run or Z9/Z10 re-invoke). |
| `POST /api/triage/{id}/complete` | `Triaging` | Moves to `Triaged`; emits `andy.issues.events.issue.{id}.triaged`. |
| `POST /api/triage/{id}/accept` | `Triaged`, `Accepted` (no-op) | Moves to `Accepted` (terminal); emits `.accepted` once. |
| `POST /api/triage/{id}/reject` | `Triaged`, `Rejected` (no-op) | Moves to `Rejected` (terminal); emits `.rejected` once. |

- `200 OK` with the updated `IssueDto` on success.
- `409 Conflict` if the transition is forbidden by the state machine.
- `404 Not Found` if the issue does not exist or the caller does not own it.

The `TriageState` enum values: `NeedsTriage`, `Triaging`, `Triaged`, `Accepted`, `Rejected` — stored as strings on the row.

The same surface is also published as MCP tools (Z9) for agent callers via mcp-gateway. The MCP SDK auto-converts `PascalCase` method names to `snake_case`, so the tool ids are `issue_get`, `issue_list`, `issue_triage`:

| Tool | Inputs | Effect |
|---|---|---|
| `issue_get` | `issueId` | Owner-scoped `IssueDto`. |
| `issue_list` | `triageState?`, `page?`, `pageSize?` | `PagedResult<IssueDto>`; unknown `triageState` filters yield an empty page. |
| `issue_triage` | `issueId` | Re-invokes triage (calls `StartTriageAsync`). The actual agent dispatch lands in Z2; today this is the state transition only. |

The CLI mirrors the same surface (Z10):

```
andy-issues-cli issues list [--triage-state=<state>] [--page=N] [--page-size=N] [--json]
andy-issues-cli issues get  <id> [--json]
andy-issues-cli issues triage <id> [--json]
```

## Artifact feeds

Administrators manage the shared list of Azure DevOps artifact feeds (NuGet/NPM/Pip) that every sandbox will import. All endpoints live under `/api/artifact`:

| Method | Route | Who |
|---|---|---|
| `GET` | `/api/artifact/enabled` | Any authenticated user. Used by sandbox creation; returns enabled feeds only. |
| `GET` | `/api/artifact` | `artifact:admin` — lists all configs including disabled ones. |
| `GET` | `/api/artifact/feeds?organization={org}` | `artifact:admin` — live-browses Azure DevOps feeds for the org using the caller's linked AzDO PAT, so the admin UI can pick a feed to register. |
| `POST` | `/api/artifact` | `artifact:admin` — creates a feed config (`type = nuget\|npm\|pip`). Duplicate names return `409`. |
| `PATCH` | `/api/artifact/{id}` | `artifact:admin` — partial update: `name`, `project`, `enabled`. |
| `DELETE` | `/api/artifact/{id}` | `artifact:admin`. |

**Browse flow**

`GET /api/artifact/feeds` goes through `IArtifactFeedService.BrowseAzureDevOpsFeedsAsync`, which reads the caller's linked Azure DevOps provider to get the PAT and then hits the AzDO feeds endpoint. Outcomes map to HTTP as:

- `Ok` → `200 { feeds: [...] }`
- `NoLinkedProvider` → `400` (the caller has no Azure DevOps provider on their account)
- `ProviderError` → `502` (AzDO rejected the call, org is blank, or client threw)

**Permission seam**

The `artifact:admin` check uses the same `IPermissionChecker` introduced for `mcp:admin` (Story 5.1). In auth-bypass / dev / Conductor mode it short-circuits to `true`; in production mode Epic 8 will back it with Andy RBAC. Integration tests toggle a fake checker to exercise both the allow and deny paths end-to-end.

## MCP server configuration

Developers can manage MCP (Model Context Protocol) servers through `/api/mcp`. Two scopes exist:

- **Personal** — owned by the caller, visible only to them. Any authenticated user can create personal configs.
- **Shared** — visible to everyone; mutations require the `mcp:admin` permission. Admin callers also use the same routes (`POST /api/mcp` with `isShared = true`, `PATCH /api/mcp/{id}`, etc.) — there is no separate `/api/mcp/shared` path. The admin check happens at the service layer based on the `IPermissionChecker` contract.

| Method | Route | Who |
|---|---|---|
| `GET` | `/api/mcp` | Any caller — lists personal + shared. Secrets masked. |
| `GET` | `/api/mcp/{id}` | Any caller who can see the config. |
| `POST` | `/api/mcp` | Any caller (personal). `isShared=true` requires `mcp:admin`. |
| `PATCH` | `/api/mcp/{id}` | Owner of personal; `mcp:admin` for shared. |
| `POST` | `/api/mcp/{id}/toggle` | Owner of personal; `mcp:admin` for shared. Flips `enabled`. |
| `DELETE` | `/api/mcp/{id}` | Owner of personal; `mcp:admin` for shared. |

**Validation**

- `type = stdio` requires a `command`. `type = remote` requires a `url`. Other combinations return `400`.
- `(OwnerUserId, Name)` is unique per scope — duplicate personal names or duplicate shared names return `409`.

**Secret handling**

`environmentJson` and `headersJson` are write-only from the API's perspective. The outbound DTO exposes `hasEnvironment` and `hasHeaders` booleans and never serializes the raw JSON, so browsers and non-admin tools never see secrets. The unmasked fields only leave the service layer through `IMcpConfigService.GetEnabledForUserAsync`, which is exclusively consumed by `SandboxService` for the `MCP_SERVERS_JSON` injection path (Story 4.8).

**Admin permissions**

The `mcp:admin` check goes through `IPermissionChecker`, a tiny seam that Epic 8 will back with Andy RBAC. Today it reads a `permission` claim off the current principal; in auth-bypass mode (dev / Conductor) it returns `true` so local flows keep working. Tests swap in a toggleable fake so both the allow and deny paths are exercised end-to-end.

## MCP tool discovery

For a remote MCP server, `POST /api/mcp/{id}/tools` runs a JSON-RPC handshake against the configured URL and returns its live tool list. The call is a one-shot probe — the server state is not cached, so the UI can refresh whenever the user hits a "refresh" button.

- The handshake is two POSTs to the configured URL carrying any stored headers verbatim:
  1. `initialize` with andy-issues client metadata (`protocolVersion: 2024-11-05`, empty capabilities).
  2. `tools/list`, whose `result.tools` array is mapped into `[{ name, description, inputSchema }]`.
- The underlying HTTP call has a 15-second timeout. The implementation lives behind `IMcpToolDiscoveryClient` so tests (and future transports) can substitute a fake without standing up a real MCP process.
- Authorization matches the mutation endpoints — personal configs require the owner, shared configs require `mcp:admin` — because tool names and schemas can reveal sensitive capabilities.
- `type=stdio` configs are rejected with `400 Bad Request` (tool discovery is only meaningful against a remote transport).

**Outcome → HTTP mapping**

| Outcome | HTTP |
|---|---|
| `Ok` | `200` with `{ tools: [...] }` |
| `NotFound` (config missing or not visible) | `404` |
| `Forbidden` (non-owner personal; non-admin shared) | `403` |
| `NotRemote` (stdio config) | `400` |
| `DiscoveryFailed` (`Timeout`, `HttpError`, `MalformedResponse`) | `502` with `{ error, discoveryOutcome }` |

## MCP server injection into sandboxes

Developers register MCP (Model Context Protocol) servers through the Andy Issues admin surface — some personal, some shared across the org — and sandboxes need to see them so in-container tooling (Zed, Claude) can call into them.

- `IMcpConfigService.GetEnabledForUserAsync(userId)` returns every enabled `McpServerConfig` that the user can see: personal configs owned by them, plus every shared config. Disabled rows are filtered out at query time.
- `SandboxService.CreateAsync` serializes the result into a single `MCP_SERVERS_JSON` environment variable on the container create call. Each entry carries `{ id, name, description, type, command, argumentsJson, environmentJson, url, headersJson }` — including the full stdio env block and the full HTTP headers block. The sandbox entrypoint parses this and writes the appropriate config file so the in-container agents see the servers on first run.
- The sandbox channel is a **server→server** path. Secrets are intentionally shipped in the clear on the env var because the container is owned by the same user and lives behind andy-containers' auth boundary. The outbound `McpServerConfigDto` used by the REST API deliberately drops `EnvironmentJson` and `HeadersJson` and exposes `HasEnvironment`/`HasHeaders` booleans instead — the browser never sees the unmasked data even if the user is an admin.
- `McpServerConfigFull` (defined in the Application layer) is the server-side-only record that carries unmasked fields and must never be returned through an HTTP/gRPC/MCP response. The type has an explicit comment to that effect and only one consumer (`SandboxService`).

## Zed IDE access

Every sandbox container started through `POST /api/sandboxes` exposes an in-browser Zed IDE plus a VNC display — andy-containers brings them up as part of the template image and publishes their URLs on the container object. andy-issues never runs a dedicated IDE gateway; it just forwards the live connection details on demand.

- `GET /api/sandboxes/{id}/connection` returns `{ ideEndpoint, vncEndpoint, sshEndpoint }`. The call always goes through `IContainersClient.GetConnectionInfoAsync` so the response reflects the current state of the container rather than whatever was cached when the sandbox was created (IDE ports can change between runs, e.g. after a stop/start cycle).
- The endpoint is owner-only; anyone other than `Sandbox.OwnerUserId` sees `404`. There is no shared-user access to another developer's IDE session.
- Front-end integration (see Story 12.3) is expected to iframe `ideEndpoint` for the editor surface and, when VNC is needed for non-Zed apps, iframe `vncEndpoint` using the noVNC JS client. SSH is surfaced for developers who prefer their local terminal.
- There is no `ZedSessionService` shim carried over from devpilot. The entire IDE session plumbing lives in andy-containers and is reached through the thin `IContainersClient` seam; removing devpilot's custom VPS gateway was the point of the migration.

## Azure profile injection

When a sandbox is created on a repository that has an Azure service principal configured (via Story 2.5 / 2.6), the credentials flow into the container as `AZURE_*` environment variables so the sandbox entrypoint can `az login` without any additional plumbing:

| Source (Repository column) | Environment variable |
|---|---|
| `AzureClientId` | `AZURE_CLIENT_ID` |
| `AzureClientSecret` | `AZURE_CLIENT_SECRET` |
| `AzureTenantId` | `AZURE_TENANT_ID` |
| `AzureSubscriptionId` (optional) | `AZURE_SUBSCRIPTION_ID` |

`SandboxService` treats the Azure identity as all-or-nothing: unless `ClientId`, `ClientSecret`, and `TenantId` are all present on the repository row (`Repository.HasAzureIdentity`), the env vars are omitted entirely and the container starts without Azure credentials. `AZURE_SUBSCRIPTION_ID` is additive — it's only included when set on the row.

The env vars are passed to `IContainersClient.CreateContainerAsync` via the `environmentVariables` dictionary, which the andy-containers adapter forwards on the POST to `api/containers`. Nightly end-to-end runs use `verify-azure-identity` (Story 2.6) against a real container to confirm `az login` succeeds inside the sandbox.

## Artifact feed injection

Sandboxes inherit the organization's enabled artifact feeds so package managers inside the container (`nuget`, `pip`, `npm`) can restore from private Azure Artifacts without per-container configuration.

- Every enabled `ArtifactFeedConfig` is serialized into a single `ARTIFACT_FEEDS_JSON` environment variable — an array of `{ name, type, organization, project?, feedName }` objects — and passed to andy-containers on sandbox create. The sandbox entrypoint parses this and writes the appropriate `nuget.config`, `pip.conf`, and `.npmrc` files.
- Disabled feeds are filtered out at query time and never reach the container.
- If the caller has a linked Azure DevOps provider (`LinkedProvider` row for `AzureDevOps`), their PAT is attached as `AZURE_DEVOPS_PAT` so those config files can reference it. When no PAT is linked, `ARTIFACT_FEEDS_JSON` is still emitted but `AZURE_DEVOPS_PAT` is omitted — individual package managers will fail with clear "authentication required" messages, which is surfaced to the developer.
- The feed query is implemented behind `IArtifactFeedService.GetEnabledAsync`, which Story 5.3 will extend with CRUD admin endpoints.

## Pull request from a sandbox

Once work is complete inside a sandbox, a single call can push the feature branch to the upstream and open a pull request. The flow is:

1. `POST /api/repositories/{id}/pull-request` body `{ sandboxId, title, description?, sourceBranch, targetBranch, storyId? }`.
2. The server validates that the caller owns the repository *and* the sandbox — shared users can't open PRs on repositories they don't own.
3. The server execs `git -C /workspace push -u origin {sourceBranch}` **inside the sandbox container** via `andy-containers`. andy-issues never runs git locally; the push always happens from the sandbox where the branch actually lives. Branch names are validated first (letters, digits, `/`, `-`, `_`, `.`, `+` only) so they can't inject shell metacharacters into the remote exec.
4. If the push exits non-zero, the response is `502` with the captured stderr and no PR is opened.
5. On a successful push, the server dispatches to the correct provider client based on `Repository.Provider`:
   - **GitHub** — owner and repo are parsed from the clone URL (`https://github.com/{owner}/{repo}.git`); the caller's linked GitHub provider supplies the token.
   - **Azure DevOps** — org/project are parsed from the clone URL, `Repository.ExternalId` supplies the AzDO repo GUID, and the caller's linked Azure DevOps PAT authenticates the call.
6. The response is `{ pullRequestUrl }`. If `storyId` is provided and refers to an existing story, `UserStory.PullRequestUrl` is updated to the new URL so the backlog reflects the link.

Outcome → HTTP mapping: `Created → 200`, `NotFound → 404`, `Forbidden → 403`, `PushFailed → 502`, `ProviderFailed → 502`.

### One-click variant from a sandbox

For UI flows where the user already has a specific sandbox selected there is a thinner wrapper: `POST /api/sandboxes/{id}/pull-request` body `{ title, description?, storyId? }`. The server loads the sandbox, derives `sourceBranch` from `Sandbox.Branch` and `targetBranch` from `Repository.DefaultBranch`, then delegates to the same `IPullRequestService.CreateFromSandboxAsync` path so the push + provider dispatch logic lives in exactly one place. Ownership is checked on the sandbox and repository, and the outcome mapping is identical.

## Sandboxes

A sandbox is a working container managed by the sibling `andy-containers` service. andy-issues keeps only a thin local projection (container id, repo, branch, owner, cached status) and delegates every lifecycle operation.

- `POST /api/sandboxes` — body `{ repositoryId, branch, resolution? }`. Creates a container from the template code configured at `AndyContainers:DefaultTemplateCode`, persists a `Sandbox` row, and returns the new DTO. Returns `404` if the caller cannot view the repository.
- `GET /api/sandboxes` — lists the caller's sandboxes. Each row's status is refreshed from andy-containers before being returned so stale `Creating` rows flip to `Running` / `Stopped` / `Destroyed` automatically.
- `GET /api/sandboxes/{id}` — same refresh, but for a single sandbox. Returns `404` if the sandbox does not exist *or* the caller is not the owner (ownership is intentionally not shared).
- `GET /api/sandboxes/{id}/connection` — on-demand lookup of connection details (IDE URL, VNC URL, SSH endpoint) from andy-containers. Never cached.
- `DELETE /api/sandboxes/{id}` — destroys the remote container and removes the local row. Already-gone containers (404 from andy-containers) are treated as success so local state can reconcile after out-of-band cleanup.

## Azure DevOps sync

User stories attached to Azure-DevOps-backed repositories can be mirrored to Work Items in the linked AzDO project.

- `POST /api/repositories/{id}/sync-azure-devops` walks every story under the repo and either creates or updates a Work Item (type *User Story*) in the target project. New items have their id persisted in `UserStory.AzureDevOpsWorkItemId`; subsequent pushes update the existing item. Org/project are derived from the repository's clone URL (both `dev.azure.com/{org}/{project}/_git/...` and `{org}.visualstudio.com/{project}/_git/...` are supported). The caller's AzDO linked provider supplies the PAT.
- A hosted `AzureDevOpsBacklogPullJob` polls remote state on a timer configured by `Andy:Issues:AzureDevops:PullIntervalSeconds`. A value ≤ 0 disables the job entirely (the default in test and dev environments). Each tick reads every AzDO-linked repository and calls the sync service's pull path.
- Conflict resolution:
  - **Azure DevOps is authoritative for done/closed state.** When the remote Work Item is in `Closed`, `Done`, or `Removed`, the local story is forced to `Done`. Other remote states (`New`, `Active`, `Resolved`, ...) are ignored on pull so local progress is never rolled back.
  - **Andy Issues is authoritative for title and description.** Pulled snapshots never overwrite local text fields; push is the only direction in which title/description flow.
- Local status → AzDO state mapping used on push: `Draft → New`, `Ready/InProgress → Active`, `InReview → Resolved`, `Done → Closed`.
