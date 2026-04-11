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
