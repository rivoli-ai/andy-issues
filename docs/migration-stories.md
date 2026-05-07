# Andy Issues — Migration Stories

Migration plan to evolve `andy-issues` from the bare service template into a replacement
for `andy-devpilot`, with these constraints (decided in chat):

- Code viewing / file analysis is **dropped** (lives in `andy-code-index`).
- VM/sandbox lifecycle is **delegated to `andy-containers`** via its `.NET` client.
- `infra/sandbox/*` scripts (Azure login, Zed, certs, artifact feeds) **move into
  `andy-containers`** as a "devpilot-desktop" container template.
- Backlog AI generation lives **here**, calling `andy-code-index` for repo context.
- **Andy Auth is the only IdP.** Local email/password is dropped. Azure AD federation
  is configured inside Andy Auth (not in this service).
- Settings centralize in `andy-settings` (per-user data stays local).
- Service name stays `andy-issues`.
- MCP, CLI, gRPC, Conductor embedding are first-class.

Story format: **Feature · Acceptance Criteria · Design · Implementation · Tests · Docs**.
Each story is sized to fit one PR.

---

## Epic 1 — Domain & data foundation

### Story 1.1 — Replace `Item` with the new domain entities
**Feature.** Drop the placeholder `Item`/`ItemStatus`. Introduce the entities everything
else hangs off.
**AC.**
- `Item.cs`/`ItemStatus.cs` deleted; no references remain.
- New entities in `Andy.Issues.Domain/Entities/`: `Repository`, `RepositoryShare`, `Epic`,
  `Feature`, `UserStory`, `LinkedProvider`, `McpServerConfig`, `ArtifactFeedConfig`,
  `Sandbox`, `LlmSetting`.
- Each has `Id (Guid)`, `CreatedAt`, `UpdatedAt`, and (where applicable) `OwnerUserId`.
- `UserStory.Status` enum: `Draft|Ready|InProgress|InReview|Done`.
- `Sandbox` is a thin local projection of an `andy-containers` container:
  `ContainerId`, `RepositoryId`, `OwnerUserId`, `Branch`, `Status`, `IdeEndpoint?`,
  `VncEndpoint?`.
- Build clean under `TreatWarningsAsErrors`.

**Design.** `RepositoryShare` is a dedicated entity (carries `GrantedAt`,
`GrantedByUserId`). Sensitive fields (Azure SP secret, PATs) are plain strings here;
encryption-at-rest is a follow-up in Epic 7.

**Implementation.**
1. Delete `Item.cs`, `ItemStatus.cs`, related DTOs/services/tests.
2. Add new entity files (records or classes; nullable enabled).
3. Use compiler errors as the worklist; remove all `Item*` references across layers.
4. `dotnet format`; verify zero warnings.

**Tests.**
- Unit: invariant tests on `UserStory` transitions; `Repository.AddShare` idempotency.
- Integration: none yet (DbContext story is 1.2).

**Docs.**
- Update `docs/architecture.md` entity diagram and `CLAUDE.md` "Naming" examples.

### Story 1.2 — `AppDbContext` + initial migrations (Postgres + SQLite)
**Feature.** Wire the new entities into EF Core with relationships, indexes, and seeded
defaults; produce initial migrations for both providers.
**AC.**
- `AppDbContext` exposes a `DbSet<>` for every entity from 1.1.
- `OnModelCreating` configures: cascade delete `Repository → Epic → Feature → UserStory`;
  unique index on `(RepositoryId, Email)` for `RepositoryShare`; unique index on
  `McpServerConfig.Name` per owner.
- Two migration sets exist (Postgres + SQLite) and apply cleanly to a fresh DB.
- `DesignTimeDbContextFactory` still works for `dotnet ef` commands.
- Auto-migration on Development unchanged.

**Implementation.**
1. Configure relationships in `AppDbContext.OnModelCreating`.
2. `dotnet ef migrations add InitialDomain` for both providers (Postgres default,
   SQLite via `Database__Provider=Sqlite`).
3. Verify `EnsureCreated` path for SQLite under Conductor mode.

**Tests.**
- Integration: `AppDbContextTests` — round-trip persistence for each entity (InMemory
  is insufficient for relationship checks; use SQLite in-memory).
- Integration: a Postgres-backed test (Testcontainers) gated behind a CI label.

**Docs.**
- Update `docs/deployment.md` migration commands (already documented for the template).

### Story 1.3 — Application layer DTOs & request contracts
**Feature.** DTOs and `Create*Request`/`Update*Request` records for all new entities,
mirroring devpilot shapes so frontends can be ported with minimal churn.
**AC.**
- DTOs in `Andy.Issues.Application/Dtos/` for each entity (records).
- Request models in `Andy.Issues.Application/Requests/`.
- Sensitive fields (Azure SP secret, PAT, MCP env vars) are **never** included in
  outbound DTOs — masked or omitted.
- Mapping helpers (entity → DTO) live in `Application/Mapping/`. No AutoMapper.

**Tests.**
- Unit: mapping tests; secret-masking tests (asserts a `RepositoryDto` contains no
  secret field even when entity has one).

**Docs.**
- Update `docs/features.md` with the DTO shapes (or link to the source).

---

## Epic 2 — Repository management

### Story 2.1 — `RepositoriesController` list/get/delete with sharing scope
**Feature.** REST endpoints for listing the user's own repos, repos shared with them,
or both; getting one; deleting (cascade backlog).
**AC.**
- `GET /api/repositories?scope=mine|shared|all&page=&pageSize=` — paginated, default
  `mine`.
- `GET /api/repositories/{id}` — 404 if not owner and not shared.
- `DELETE /api/repositories/{id}` — owner-only; cascades to backlog and sandboxes;
  attempts to destroy any live sandboxes via `SandboxService` first.
- All endpoints `[Authorize]`; user id read from JWT `sub`.

**Design.** Authorization helper `IRepositoryAccessGuard` (checks owner or share);
inject into the service layer so MCP/gRPC/CLI all enforce it identically.

**Implementation.**
1. `IRepositoryService` + `RepositoryService` in Infrastructure.
2. `RepositoriesController`.
3. `RepositoryAccessGuard` + tests.

**Tests.**
- Unit: `RepositoryService` filtering by scope; access guard truth table.
- Integration: `WebApplicationFactory` end-to-end with seeded users + JWT.
- E2E: deferred to Story 12.1 (Angular page).

**Docs.**
- Update `docs/features.md` with REST surface.

### Story 2.2 — Repository sharing
**Feature.** Share a repo with another user by email; revoke; list shares.
**AC.**
- `POST /api/repositories/{id}/share` body `{ email }` — owner-only; resolves email to
  a user via `UsersController.Suggest` backing service; creates `RepositoryShare`.
- `DELETE /api/repositories/{id}/share/{userId}` — owner-only.
- `GET /api/repositories/{id}/shares` — owner-only.
- Sharing the repo with yourself returns 400.
- Sharing with an unknown email returns 404.

**Tests.**
- Unit: `RepositoryService.Share` happy + edge cases.
- Integration: full HTTP cycle; verify `scope=shared` for the recipient user includes
  the repo afterwards.

**Docs.**
- `docs/features.md` "Sharing" subsection.

### Story 2.3 — GitHub repository sync
**Feature.** Pull the user's GitHub repos via their linked PAT and sync into our DB.
**AC.**
- `POST /api/repositories/sync-github` body `{ repoIds: string[] }` — for each id,
  upsert a `Repository` row owned by the caller.
- Uses the PAT from the caller's `LinkedProvider` row (`provider=github`).
- 401 if no GitHub PAT linked.
- Returns `{ added, updated, skipped }` counts.

**Design.** `IGitHubClient` abstraction so tests can fake it; real impl uses
`Octokit.net` (already in template? — confirm; otherwise add).

**Tests.**
- Unit: service with fake `IGitHubClient`.
- Integration: HTTP test with fake client registered in test DI.

**Docs.**
- `docs/features.md` "GitHub sync" with required PAT scopes.

### Story 2.4 — Azure DevOps repository sync
**Feature.** Same as 2.3 for Azure DevOps repos.
**AC.**
- `POST /api/repositories/sync-azure` body `{ organization, project?, repoIds: string[] }`.
- Uses caller's Azure DevOps PAT from `LinkedProvider`.
- Returns `{ added, updated, skipped }`.

**Design.** `IAzureDevOpsClient` abstraction; real impl via
`Microsoft.TeamFoundationServer.Client` or REST.

**Tests.** As 2.3.

**Docs.** `docs/features.md` "Azure DevOps sync" + required PAT scopes.

### Story 2.5 — Repository LLM override
**Feature.** Pin a specific `LlmSetting` to a repo (used by sandbox + draft backlog).
**AC.**
- `PATCH /api/repositories/{id}/llm-setting` body `{ llmSettingId | null }`.
- Owner-only.
- 404 if `llmSettingId` doesn't exist or isn't owned by caller.
- `RepositoryDto` exposes `llmSettingId` (id only, never the API key).

**Tests.**
- Unit + Integration round-trips.

### Story 2.6 — Azure Service Principal credentials per repo
**Feature.** Store an Azure SP triple on a repo so sandboxes can `az login` against it.
**AC.**
- `PATCH /api/repositories/{id}/azure-identity` body
  `{ clientId, clientSecret, tenantId, subscriptionId? }`.
- Owner-only.
- `POST /api/repositories/{id}/verify-azure-identity` — performs an `az login` test
  inside an ephemeral container exec via `ContainersClient.ExecAsync` against a tiny
  helper container; returns `{ success, message }`.
- `RepositoryDto` exposes only a boolean `hasAzureIdentity`, never the secret.

**Tests.**
- Unit: service stores values, masks in DTO.
- Integration: `verify-azure-identity` against a fake `IContainersClient` (real-call
  variant runs only in nightly CI with secrets).

**Docs.**
- `docs/security.md` — section "Azure SP storage" explaining at-rest protection (links
  to Story 7.3 secrets-via-andy-settings).

### Story 2.7 — Create PR from a sandbox branch
**Feature.** From a finished sandbox, push the branch and open a PR on the upstream.
**AC.**
- `POST /api/repositories/{id}/pull-request` body
  `{ sandboxId, title, description, sourceBranch, targetBranch }`.
- Owner-only.
- Performs the push by calling `ContainersClient.ExecAsync(sandboxId, "git push ...")`,
  then opens the PR via the appropriate provider client (GitHub or AzDO).
- Returns `{ pullRequestUrl }`; updates the linked `UserStory.PullRequestUrl` if a
  story id is provided.

**Tests.**
- Unit: provider dispatch (GitHub vs AzDO).
- Integration: HTTP cycle with fake provider clients.

### Story 2.8 — `UsersController.Suggest`
**Feature.** Search users by email/name for sharing dialogs.
**AC.**
- `GET /api/users/suggest?q=&limit=10` — case-insensitive prefix match on email/name.
- Excludes the calling user.
- Reads from a local `UserDirectory` projection populated lazily from JWT claims (no
  Andy Auth admin API call required).

**Tests.**
- Unit + Integration.

---

## Epic 3 — Backlog management

### Story 3.1 — `BacklogController` CRUD for epics, features, stories
**Feature.** Hierarchical CRUD scoped to a repo.
**AC.**
- `GET /api/repositories/{id}/backlog` — returns the full tree.
- `POST /api/repositories/{id}/epics` — owner or share with write.
- `POST /api/epics/{id}/features`
- `POST /api/features/{id}/stories`
- `PATCH`/`DELETE` equivalents for each.
- Story payload supports `acceptanceCriteria` (string or list), `storyPoints?`.

**Design.** Single `IBacklogService` rather than three services — the entities are
small and always co-loaded.

**Tests.**
- Unit + Integration full CRUD matrix.

**Docs.** `docs/features.md` "Backlog".

### Story 3.2 — Story status transitions + PR URL
**Feature.** Update a story's status and attach a PR URL.
**AC.**
- `PATCH /api/stories/{id}/status` body `{ status, pullRequestUrl? }`.
- Server enforces transitions (no `Done → Draft`).
- Emits a `BoardHub` event (Story 3.4).

**Tests.**
- Unit: transition rules.
- Integration: end-to-end.

### Story 3.3 — Azure DevOps bidirectional sync
**Feature.** Push backlog items to AzDO as Work Items, pull status changes back.
**AC.**
- `POST /api/repositories/{id}/sync-azure-devops` — creates/updates Work Items for all
  stories under the repo; persists `azureDevOpsId` per story.
- A scheduled `IBackgroundService` (`AzureDevOpsBacklogPullJob`) polls every N minutes
  for status updates and applies them to local stories. Interval comes from
  `andy.issues.azureDevops.pullIntervalSeconds` (Epic 7).
- Conflict resolution: AzDO is authoritative on closed/done states; local is
  authoritative on title/description.

**Tests.**
- Unit: mapping (story ↔ work item); conflict rules.
- Integration: HTTP push path against fake AzDO client.
- E2E: deferred to manual nightly run.

**Docs.** `docs/features.md` "Azure DevOps sync" subsection.

### Story 3.4 — `BoardHub` SignalR for live backlog updates
**Feature.** Multi-client live updates of the backlog board.
**AC.**
- Hub at `/hubs/board`; auth required (JWT via SignalR).
- Methods: `JoinRepository(repoId)`, `LeaveRepository(repoId)`.
- Server emits `StoryUpdated`, `StoryAdded`, `StoryDeleted`, `EpicUpdated`, etc.
- All `BacklogService` mutations publish via the hub.

**Tests.**
- Unit: hub method auth.
- Integration: connect a test SignalR client, mutate via REST, assert event received.

**Docs.** `docs/architecture.md` "Real-time" section.

---

## Epic 4 — Sandbox lifecycle via `andy-containers`

### Story 4.1 — `SandboxService` thin wrapper over `ContainersClient`
**Feature.** A local service that creates/lists/gets/destroys sandboxes by talking to
`andy-containers`, persisting only a thin `Sandbox` projection.
**AC.**
- `ISandboxService.CreateAsync(repoId, branch, ct)` — calls
  `ContainersClient.CreateContainerAsync` with template code from settings; persists
  `Sandbox { ContainerId, RepositoryId, OwnerUserId, Branch, Status="Pending" }`.
- `ListAsync(userId)` — joins local rows with live status from `containers.GetContainer`.
- `GetAsync(id)` — owner check; refreshes status.
- `DestroyAsync(id)` — owner check; calls `DestroyContainerAsync`; deletes local row.
- Connection info (IDE/VNC) read on demand via `GetConnectionInfoAsync`.

**Design.** `Andy.Containers.Client.ContainersClient` registered in DI with bearer
token forwarded from the inbound JWT.

**Implementation.**
1. Add `Andy.Containers.Client` package reference (or `local-packages/`).
2. DI registration in `Program.cs` reading `AndyContainers:BaseUrl` (defaults from
   andy-settings, see Epic 7).
3. Implement service.

**Tests.**
- Unit: with `IContainersClient` fake.
- Integration: `WebApplicationFactory` + fake client; verifies persistence + cascade
  on delete.

**Docs.** `docs/architecture.md` — diagram showing andy-issues → andy-containers.

### Story 4.2 — `SandboxesController` REST surface
**Feature.** REST endpoints matching devpilot's `SandboxController`.
**AC.**
- `POST /api/sandboxes` body `{ repositoryId, branch, resolution? }`.
- `GET /api/sandboxes` — list user's sandboxes.
- `GET /api/sandboxes/{id}` — status + connection info.
- `DELETE /api/sandboxes/{id}`.
- 403 if not owner.

**Tests.** Integration HTTP cycle.

### Story 4.3 — Move `infra/sandbox/*` scripts into `andy-containers`
**Feature.** Cross-repo: relocate the entire `infra/sandbox/` script tree from
`andy-devpilot` into `andy-containers/templates/devpilot-desktop/`.
**AC.**
- All scripts (`setup.sh`, `windows/setup.ps1`, `linux/setup.sh`, `mac/setup.sh`,
  `manager/docker-entrypoint.sh`, `build-desktop-docker-inner.sh`) live in
  `andy-containers/templates/devpilot-desktop/scripts/`.
- A new `andy-containers` template `devpilot-desktop` is registered, referencing
  these scripts as the container build context.
- The `ContainerTemplate` row exposes `IdeType=Zed`, mounts `/certs` for corporate CA
  injection, and reads `AZURE_*` + `ARTIFACT_FEEDS_JSON` env vars at entrypoint.
- The original devpilot scripts continue to function unchanged inside the template
  (i.e. don't rewrite, just relocate and parameterize).

**Implementation.** Done in `andy-containers` repo, tracked here as a dependency.

**Tests.**
- Manual: build the template image and launch a container against a public repo.
- Owned by `andy-containers` CI going forward.

**Docs.** `andy-containers/docs/templates/devpilot-desktop.md` (created in that repo);
link from `docs/features.md` here.

### Story 4.4 — Azure profile injection preserved end-to-end
**Feature.** When a sandbox is created on a repo with an Azure SP set, the SP env
vars flow to the container and the entrypoint logs in.
**AC.**
- `SandboxService.CreateAsync` reads `Repository.AzureClientId/Secret/TenantId/SubscriptionId`
  and passes them to `CreateContainerRequest.EnvironmentVariables` as `AZURE_*`.
- Inside the container, the relocated `setup.sh` (Story 4.3) `az login` path runs
  unchanged.
- Verified by `verify-azure-identity` (Story 2.6) against a real container in nightly
  CI.

**Tests.**
- Unit: env var construction.
- Integration: assert `CreateContainerRequest.EnvironmentVariables` contents via fake.
- E2E (nightly): real container, real az cli.

### Story 4.5 — Artifact feed injection preserved
**Feature.** Enabled artifact feeds (Story 5.3) get serialized as `ARTIFACT_FEEDS_JSON`
plus `AZURE_DEVOPS_PAT` and the entrypoint configures `nuget.config`/`pip.conf`/`npm`.
**AC.**
- `SandboxService.CreateAsync` builds the JSON from
  `IArtifactFeedService.GetEnabledAsync`.
- Tested with multi-feed sandboxes in nightly E2E.

**Tests.** Unit + Integration with fakes; nightly E2E.

### Story 4.6 — Certificate injection preserved
**Feature.** Corporate CAs from this repo's `certs/` folder are mounted into every
sandbox, identical effect to today.
**AC.**
- The `devpilot-desktop` template mounts `/certs` (sourced at container build time
  from `andy-containers`'s own `certs/`).
- Document the developer flow: drop your `.crt` files in `andy-containers/certs/`
  before building the template image.

**Tests.** Manual `update-ca-certificates --verbose` inside a launched sandbox shows
the corporate certs.

**Docs.** `docs/security.md` — "Sandbox certificate trust" section.

### Story 4.7 — Zed IDE session via `ContainersClient`
**Feature.** Replace devpilot's `ZedSessionService` (which talked to a custom VPS
gateway) with `ContainersClient.GetConnectionInfoAsync`.
**AC.**
- `GET /api/sandboxes/{id}/connection` returns `{ ideEndpoint, vncEndpoint, sshEndpoint }`
  sourced from `ContainersClient`.
- Frontend (Story 12.3) embeds the IDE/VNC iframe with these URLs.
- No code from `ZedSessionService.cs` survives.

**Tests.** Integration HTTP cycle.

### Story 4.8 — MCP server injection into sandboxes
**Feature.** Enabled MCP server configs (personal + shared) get injected into the
sandbox so in-container Zed/Claude can use them.
**AC.**
- `SandboxService.CreateAsync` calls `IMcpConfigService.GetEnabledForUserAsync`,
  serializes the configs (with their secrets — this is server→server) into a
  `MCP_SERVERS_JSON` env var, and the template entrypoint writes them to the
  appropriate location for Zed/Claude.
- Secrets never appear in any DTO returned to the browser.

**Tests.**
- Unit: serialization includes secrets; DTO masking still applied to outbound paths.
- Integration: assert env var contents via fake `IContainersClient`.

### Story 4.9 — Sandbox → PR convenience endpoint
**Feature.** Frontend convenience: one-click "create PR from this sandbox".
**AC.**
- `POST /api/sandboxes/{id}/pull-request` body `{ title, description, storyId? }`
  delegates to Story 2.7's `RepositoriesController` PR-creation.
- Single round trip from the UI.

**Tests.** Integration.

---

## Epic 5 — MCP server config & artifact feeds (CRUD only)

### Story 5.1 — `McpController` CRUD (personal + shared)
**Feature.** Manage MCP server configurations (stdio + remote HTTP).
**AC.**
- `GET /api/mcp` — personal + shared visible to caller; secrets masked.
- `GET /api/mcp/enabled` — internal only (used by `SandboxService`); secrets present;
  not exposed externally — guarded by an `[InternalOnly]` policy or never wired to a
  controller (call the service directly instead). Pick one and document.
- `POST /api/mcp` — create personal config (`type=stdio|remote`).
- `PATCH /api/mcp/{id}` — owner-only.
- `DELETE /api/mcp/{id}` — owner-only.
- `POST /api/mcp/{id}/toggle` — owner-only; flips `Enabled`.
- Admin variants (`/api/mcp/shared/...`) require `mcp:admin` permission via Andy RBAC.

**Tests.** Unit + Integration with masking assertions.

**Docs.** `docs/features.md` "MCP server configuration".

### Story 5.2 — MCP tool discovery via JSON-RPC
**Feature.** For a remote MCP server, fetch its tool list via the standard MCP
`initialize` + `tools/list` JSON-RPC handshake.
**AC.**
- `POST /api/mcp/{id}/tools` — owner-only; opens an HTTP request to the configured
  URL with configured headers; returns `[ { name, description, schema } ]`.
- Failures (timeout, non-200, malformed) return a structured error.

**Tests.**
- Unit: client with a stub HTTP handler returning canned JSON-RPC payloads.
- Integration: HTTP cycle with WireMock.

### Story 5.3 — `ArtifactController`
**Feature.** Artifact feed CRUD (admin) plus per-user enabled-list read.
**AC.**
- `GET /api/artifact/feeds` (admin) — browse Azure DevOps feeds for an org via the
  caller's AzDO PAT.
- `GET /api/artifact` (admin) — list configs.
- `POST /api/artifact` (admin) — create a config (`type=nuget|npm|pip`).
- `PATCH /api/artifact/{id}` (admin)
- `DELETE /api/artifact/{id}` (admin)
- `GET /api/artifact/enabled` — read-only; visible to all auth'd users; used by
  sandbox creation.
- Admin checks via `[RequirePermission("artifact:admin")]`.

**Tests.** Unit + Integration with permission matrix.

---

## Epic 6 — `andy-code-index` integration (replaces local code analysis)

### Story 6.1 — `CodeIndexClient`
**Feature.** Thin .NET client for `andy-code-index` REST + MCP surface.
**AC.**
- New project `Andy.Issues.Infrastructure.CodeIndex` (or a folder; either is fine).
- `ICodeIndexClient` with methods: `AddRepositoryAsync`, `SyncRepositoryAsync`,
  `SemanticSearchAsync`, `GetArchitectureDocsAsync`, `GetCookbookAsync`,
  `GetWikiAsync`, `GetApiDocsAsync`.
- Base URL from `andy.issues.codeIndex.baseUrl` (settings, Epic 7).
- Forwards JWT.

**Tests.**
- Unit: with stub HTTP handler.
- Integration: against a fake server (WireMock or test fixture).

### Story 6.2 — Auto-register repos with `andy-code-index` on creation
**Feature.** Whenever a `Repository` is created or sync'd in this service, ensure it's
indexed in `andy-code-index`.
**AC.**
- `RepositoryService.CreateAsync` + sync paths call `CodeIndexClient.AddRepositoryAsync`
  fire-and-forget on a background channel; failures logged, do not block.
- A `POST /api/repositories/{id}/reindex` endpoint forces a sync.
- New `Repository.CodeIndexStatus` field: `NotIndexed|Indexing|Indexed|Failed`.

**Tests.**
- Unit + Integration with fake `ICodeIndexClient`.

### Story 6.3 — Draft backlog generator
**Feature.** Given a repo, produce a draft set of epics/features/stories by combining
`andy-code-index` enrichments (architecture, cookbook, API docs) with an LLM call,
then writing them as `Status=Draft` rows.
**AC.**
- `POST /api/repositories/{id}/backlog/draft` body `{ llmSettingId? }` — owner-only.
- Workflow:
  1. Fetch enrichments via `ICodeIndexClient`.
  2. Build a structured prompt summarizing the repo.
  3. Call the LLM (using `Repository.LlmSettingId` or override) via a small
     `ILlmClient` abstraction.
  4. Parse JSON output; validate schema; persist as draft epics/features/stories.
- Streams progress via `BoardHub` events.
- Returns `{ epicsAdded, featuresAdded, storiesAdded }`.
- Idempotent: re-running on a repo with existing drafts merges by title.

**Design.** `ILlmClient` is a tiny interface (one method `CompleteJsonAsync<T>`); we
do not depend on a heavy SDK. `Andy.Llm` package may already exist in `local-packages/` —
check; otherwise inline a small implementation that supports OpenAI + Anthropic +
Ollama (matching `LlmSetting.Provider`).

**Tests.**
- Unit: prompt construction; JSON parsing; merge-on-title.
- Integration: full flow with fake `ICodeIndexClient` and fake `ILlmClient`.
- E2E (nightly): real code-index, real LLM, against a small fixture repo.

**Docs.** `docs/features.md` "Draft backlog generation" — explain the data flow and
the prompt template location.

### Story 6.4 — Cleanup: remove all in-repo code analysis remnants
**Feature.** Make sure no `CodeAnalysis` / `FileAnalysis` / `VPSAnalysisService`
shapes ever land in this service.
**AC.**
- Grep for `CodeAnalysis`, `FileAnalysis`, `VPSAnalysisService` returns zero hits in
  `src/`, `tests/`, `client/`, `tools/`.
- A repo-level lint rule (or CI grep job) fails the build if these strings reappear.

**Tests.** CI guard.

**Docs.** `docs/architecture.md` — explicitly note the boundary: "code understanding
lives in andy-code-index".

---

## Epic 7 — `andy-settings` integration

### Story 7.1 — `AndySettingsClient` with per-request cache
**Feature.** Wrap `andy-settings` REST API for typed reads with caching.
**AC.**
- `IAndySettingsClient.GetAsync<T>(key, ct)` and `GetBatchAsync(keys, ct)`.
- Per-request cache (`Scoped` lifetime) backed by a dictionary.
- Forwards user JWT for user-scoped resolution.
- Falls back to `appsettings.json` if `AndySettings:ApiBaseUrl` is empty (dev mode).

**Implementation.**
1. Use existing `AndySettings` `HttpClient` registered in `Program.cs`.
2. Implement client + DI registration.

**Tests.**
- Unit: cache behavior; fallback behavior.
- Integration: against a test double for andy-settings.

### Story 7.2 — Settings key catalog + bootstrap
**Feature.** Define the keys this service reads from andy-settings, document them,
and ship a seed file an admin can apply.
**AC.**
- New file `config/andy-settings-seed.json` listing definitions for:
  - `andy.issues.azureDevops.defaultOrg` (string, user-scope)
  - `andy.issues.azureDevops.pullIntervalSeconds` (int, app-scope, default 300)
  - `andy.issues.codeIndex.baseUrl` (uri, app-scope)
  - `andy.issues.containers.baseUrl` (uri, app-scope)
  - `andy.issues.containers.templateCode` (string, app-scope, default `devpilot-desktop`)
  - `andy.issues.containers.providerCode` (string, app-scope)
  - `andy.issues.draftBacklog.defaultLlmSettingId` (string, user-scope)
- README in `config/` describing how to apply the seed via `andy-settings` CLI.

**Tests.** N/A (config file).

**Docs.** `docs/deployment.md` "Settings keys".

### Story 7.3 — Secrets via andy-settings
**Feature.** Move all secret values out of `appsettings.json` into andy-settings
encrypted secrets.
**AC.**
- Repository Azure SP secrets are stored in andy-settings (key per repo, scoped by
  user) — the `Repository` row stores only the secret ref/key.
- PATs (`LinkedProvider.AccessToken`) likewise.
- DI bootstrap uses `AndySettingsClient` to resolve at use time; never logged.
- A migration moves any existing rows on upgrade (no-op for fresh installs).

**Tests.**
- Unit: storage adapter that abstracts "raw value vs ref".
- Integration: end-to-end with fake andy-settings.

**Docs.** `docs/security.md` — "Secret storage" section.

---

## Epic 8 — Auth & linked providers

### Story 8.1 — Andy Auth as the only IdP
**Feature.** Drop devpilot's local email/password and direct OAuth client code; rely
entirely on Andy Auth for primary login.
**AC.**
- `appsettings.json` `AndyAuth:Authority` is the only auth config.
- No `AuthController` registration / login / OAuth callback endpoints in this service.
- No password fields anywhere in the domain.
- Frontend uses `angular-auth-oidc-client` against Andy Auth (Story 12.5).

**Implementation.** Mostly deletion. Leave the `[Authorize]` plumbing intact.

**Tests.**
- Integration: anonymous request to a protected endpoint returns 401.
- Integration: request with valid Andy Auth JWT succeeds.

**Docs.** `docs/security.md` "Authentication" — note Andy Auth federates Azure AD
upstream; this service has no Azure-specific auth config.

### Story 8.2 — Linked providers for GitHub & Azure DevOps
**Feature.** Let users link a GitHub or AzDO account to store an OAuth token / PAT
that this service uses for repo sync and PR creation.
**AC.**
- `POST /api/linked-providers` body `{ provider, accessToken, refreshToken?, expiresAt? }` —
  upserts a row for the caller.
- `GET /api/linked-providers` — list (token values masked).
- `DELETE /api/linked-providers/{provider}`.
- Tokens stored via Story 7.3 secret refs.

**Tests.** Unit + Integration.

### Story 8.3 — PAT entry helper endpoint
**Feature.** Convenience for users who provide a raw PAT instead of an OAuth flow.
**AC.**
- `POST /api/linked-providers/pat` body `{ provider, pat }` — validates the PAT by
  making one API call (e.g., GitHub `GET /user`), stores on success.

**Tests.** Unit + Integration with fake provider clients.

---

## Epic 9 — gRPC surface

### Story 9.1 — `RepositoriesGrpcService`
**Feature.** gRPC mirror of `RepositoriesController`.
**AC.**
- `proto/repositories.proto` defines messages and `RepositoriesService` with RPCs:
  `List`, `Get`, `Delete`, `Share`, `SyncGitHub`, `SyncAzureDevOps`, `CreatePr`.
- Implementation reuses `IRepositoryService`.
- `[Authorize]` enforced.

**Tests.** Integration via in-process gRPC test client.

### Story 9.2 — `BacklogGrpcService`
**Feature.** gRPC mirror of `BacklogController`.
**AC.** As above; RPCs match the REST CRUD; `UpdateStoryStatus` included.

**Tests.** Integration.

### Story 9.3 — `SandboxesGrpcService`
**Feature.** gRPC mirror of `SandboxesController`.
**AC.** RPCs: `Create`, `List`, `Get`, `Destroy`, `GetConnectionInfo`, `CreatePrFromSandbox`.

**Tests.** Integration.

---

## Epic 10 — MCP tools (this service exposes)

### Story 10.1 — Replace `ServiceTools` placeholder with the real toolset
**Feature.** MCP tools matching the new domain.
**AC.**
- `Andy.Issues.Api/Mcp/ServiceTools.cs` exposes `[McpServerTool]` methods:
  - Repositories: `ListRepositories`, `GetRepository`, `SyncGitHubRepositories`,
    `SyncAzureDevOpsRepositories`, `DeleteRepository`.
  - Backlog: `ListBacklog`, `CreateEpic`, `CreateFeature`, `CreateStory`,
    `UpdateStoryStatus`, `GenerateDraftBacklog`.
  - Sandboxes: `CreateSandbox`, `ListSandboxes`, `GetSandboxConnection`,
    `DestroySandbox`.
  - MCP configs: `ListMcpConfigs`.
  - Artifact feeds: `ListEnabledArtifactFeeds`.
- All tools call the same service-layer interfaces as REST.
- Tool descriptions are user-facing — written in plain English.

**Tests.**
- Unit: tool method delegation.
- Integration: end-to-end via the MCP HTTP endpoint with a JSON-RPC test client.

**Docs.** `docs/features.md` "MCP tools" — auto-generated tool catalog (or hand-written
list).

### Story 10.2 — `HelpTools` updates
**Feature.** Update the existing `HelpTools` to describe the new feature surface.
**AC.** Tool returns up-to-date markdown describing this service's domain.

---

## Epic 11 — CLI (`Andy.Issues.Cli`)

### Story 11.1 — `repos` commands
**Feature.** `andy-issues repos {list|get|sync-github|sync-azdo|share|delete|set-llm|set-azure-identity|verify-azure-identity}`.
**AC.**
- Calls the REST API; auth via stored token (reuse the template's auth helper).
- `list` supports `--scope=mine|shared|all`, `--json`.
- All commands return non-zero on error.

**Tests.**
- Unit: command parsing.
- Integration: against `WebApplicationFactory` over a TCP socket.

### Story 11.2 — `backlog` commands
`andy-issues backlog {list <repo>|add-epic|add-feature|add-story|set-status|sync-azdo|draft <repo>}`.
**AC.** As 11.1; `draft` calls Story 6.3.
**Tests.** As 11.1.

### Story 11.3 — `sandbox` commands
`andy-issues sandbox {create <repo> --branch|list|get|connect|destroy}`.
`connect` prints IDE + VNC URLs; with `--open`, launches the browser.
**Tests.** As 11.1.

### Story 11.4 — `mcp` commands
`andy-issues mcp {list|add stdio|add remote|toggle|discover|delete}`.
**Tests.** As 11.1.

### Story 11.5 — `artifact-feeds` commands
`andy-issues artifact-feeds {list-enabled|admin list|admin add|admin delete}` (admin
gated server-side).
**Tests.** As 11.1.

**Docs (covers 11.1–11.5).** `docs/features.md` CLI section + `tools/Andy.Issues.Cli/README.md`.

---

## Epic 12 — Angular client

### Story 12.1 — Repositories page
**Feature.** Port devpilot's repositories page to this client.
**AC.**
- Standalone component `features/repositories/repositories.component.ts`.
- Filter (`mine|shared|all`), search, pagination.
- Sync GitHub / sync AzDO modal (multi-select).
- Share dialog (uses `users/suggest`).
- Delete with confirmation.
- Set custom LLM modal.
- Set/verify Azure SP modal.
- All API calls go through `ApiService` (typed methods added in this story).

**Tests.**
- Karma: component tests with `HttpTestingController`.
- E2E: Playwright spec at `client/e2e/repositories.spec.ts`.

**Docs.** Update screenshots in `docs/features.md`.

### Story 12.2 — Backlog page
**Feature.** Hierarchical board with epics → features → stories; status + PR URL;
"generate draft" button; AzDO sync; live updates via `BoardHub`.
**AC.**
- Component `features/backlog/backlog.component.ts`.
- Drag-to-status (kanban-style) with optimistic update + server reconciliation.
- "Generate draft backlog" calls Story 6.3 and streams progress via SignalR.
- Inline edit for title/description/AC/story points.

**Tests.**
- Karma + Playwright E2E (golden path: create story → drag to In Progress → assert
  server state).

### Story 12.3 — Sandboxes page (replaces devpilot's `code` page)
**Feature.** List user sandboxes; create from a repo+branch; embed VNC iframe + IDE
link; destroy.
**AC.**
- Component `features/sandboxes/sandboxes.component.ts`.
- VNC iframe sized to the sandbox resolution.
- Buttons: "Open IDE in new tab", "Destroy", "Create PR from this sandbox".
- Health polling every 10s while a sandbox card is visible.

**Tests.** Karma + Playwright E2E (mock backend).

### Story 12.4 — Settings page (slimmed)
**Feature.** Source control linking + PATs, MCP servers CRUD, Artifact feeds (admin),
per-user LLM defaults. Anything global is a link out to andy-settings UI.
**AC.**
- Component `features/settings/settings.component.ts` with tab strip:
  Source Control / AI / MCP / Artifact Feeds (admin only).
- Each tab uses its own service in `core/services/`.

**Tests.** Karma + Playwright.

### Story 12.5 — Auth wiring
**Feature.** Use `angular-auth-oidc-client` against Andy Auth only.
**AC.**
- `app.config.ts` configures OIDC client from `/api/auth/config` (which the API
  exposes from `AndyAuth:Authority`).
- `authGuard` (functional) protects all feature routes.
- HTTP interceptor attaches Bearer token to `/api` requests.
- No fallback local-login UI exists.

**Tests.** Karma for the guard + interceptor; Playwright login flow against a fake
issuer.

### Story 12.6 — Drop the `CodeComponent` entirely
**Feature.** Cleanup: ensure the legacy code-viewing pages do not get ported.
**AC.**
- No file under `client/src/app/features/code/` exists.
- No `code` route in `app.routes.ts`.
- A CI grep job fails if `features/code` reappears.

**Tests.** CI guard.

---

## Epic 13 — Conductor integration

### Story 13.1 — `IssuesServiceConfig.swift` in conductor
**Feature.** Register `andy-issues` as a managed service in Conductor.
**AC.**
- New file
  `conductor/Conductor/Core/ServiceHost/Services/IssuesServiceConfig.swift` defining a
  struct conforming to `ServiceConfiguration`:
  - `serviceName = "issues"`
  - `displayName = "Andy Issues"`
  - `binaryRelativePath` pointing at the published binary
  - `healthEndpoint = "/health"`
  - `proxyPrefix = "/issues"`
  - `defaultPort = 9108` (or next available)
  - `dependencies = ["auth", "rbac", "settings", "containers", "code-index"]`
  - `environmentOverrides` injecting `AndyAuth:Authority`, `AndySettings:ApiBaseUrl`,
    `AndyContainers:BaseUrl`, `AndyCodeIndex:BaseUrl`, OTLP endpoint, SQLite path.
- Added to `ManagedService.serviceConfigs`.

**Implementation.** Lives in the conductor repo, tracked here.

**Tests.** Manual: launch Conductor, verify `andy-issues` shows up healthy.

**Docs.** `docs/deployment.md` "Conductor embedded mode".

### Story 13.2 — SQLite-embedded mode verified
**Feature.** Make sure this service runs cleanly under Conductor with SQLite.
**AC.**
- `Database__Provider=Sqlite ASPNETCORE_ENVIRONMENT=Production dotnet run` starts
  cleanly with `EnsureCreated`.
- All tests (`dotnet test`) pass with the SQLite provider.
- Conductor smoke test (Story 13.1) works.

**Tests.**
- CI matrix: run the full test suite once with Postgres and once with SQLite.

### Story 13.3 — Documented environment overrides
**Feature.** Document everything Conductor injects.
**AC.**
- `docs/deployment.md` table of env vars: name, source, default, used by which story.

---

## Epic 14 — Test infrastructure & quality gates

### Story 14.1 — Fakes for external dependencies
**Feature.** Test doubles for the three external services this code talks to.
**AC.**
- `tests/Andy.Issues.Tests.Fakes/`:
  - `FakeContainersClient : IContainersClient` (in-memory container store).
  - `FakeAndySettingsClient : IAndySettingsClient` (in-memory key-value).
  - `FakeCodeIndexClient : ICodeIndexClient` (canned enrichments).
- Fakes are wired into the test `WebApplicationFactory` via a `TestFixture` base.
- A small fluent builder lets a test seed state in two lines.

**Tests.** N/A (testing infrastructure).

**Docs.** `docs/testing.md` "Fakes and fixtures" section.

### Story 14.2 — CI guards
**Feature.** Repo-level guards to prevent regressions of the boundary decisions.
**AC.**
- CI job greps for `CodeAnalysis|FileAnalysis|VPSAnalysisService|features/code` and
  fails the build if found.
- CI job runs `dotnet format --verify-no-changes`.
- CI job runs the test suite under both Postgres and SQLite (Story 13.2).

**Docs.** `docs/testing.md` "CI gates".

---

## Epic 15 — Event messaging (NATS)

Adopts the ecosystem messaging contract from [andy-tasks ADR 0001](https://github.com/rivoli-ai/andy-tasks/blob/main/docs/adr/0001-messaging.md) in andy-issues. Events on NATS (commands stay on HTTP), transactional outbox in our own database, idempotent consumers, strict subject ownership. The adoption reference is [`docs/adr/0001-messaging.md`](adr/0001-messaging.md).

Rollout is incremental and landable one PR per story. Stories 15.2–15.5 and 15.7–15.8 are self-contained within andy-issues. Story 15.6 is **cross-repo** and remains gated until andy-containers begins publishing run events (andy-tasks ADR Phase 3).

### Story 15.1 — ADR 0001 reference doc
**Feature.** Thin adoption ADR at `docs/adr/0001-messaging.md` that enumerates andy-issues's subjects and consumers.
**AC.**
- File exists and links to the canonical andy-tasks ADR.
- Lists publisher subjects: `andy.issues.events.story.*`, `andy.issues.events.repository.*`, `andy.issues.events.sandbox.*`, `andy.issues.events.system.health`.
- Lists subscriber subjects: `andy.containers.events.run.*`.
- Restates the outbox rule and the "no self-subscription" rule.

**Tests.** MkDocs build still passes.

**Docs.** This IS the docs change. Linked from `docs/architecture.md`.

### Story 15.2 — `IMessageBus`, `OutboxEntry`, `OutboxDispatcher`
**Feature.** Transactional outbox + dispatcher scaffolding, identical in shape to andy-tasks's Phase 1.
**AC.**
- `IMessageBus` interface in `Andy.Issues.Application/Messaging/`.
- `InMemoryMessageBus` in `Andy.Issues.Infrastructure/Messaging/` for local dev and tests.
- `OutboxEntry` entity (Id, Subject, PayloadType, PayloadJson, HeadersJson, CreatedAt, PublishedAt?, Attempts, LastError?).
- EF config + migrations for Postgres and SQLite.
- `OutboxDispatcher` hosted service with exponential backoff.
- `services.AddIssuesMessaging()` DI extension.

**Tests.**
- Unit: dispatcher orders, marks `PublishedAt`, retries on transient failure.
- Integration: outbox round-trips on SQLite and Postgres.

**Docs.** `docs/architecture.md` new "Messaging" section.

### Story 15.3 — Publish `andy.issues.events.story.*`
**Feature.** Emit `UserStory` lifecycle events through the outbox.
**AC.**
- `created` on insert; `readied` on transition to `Ready`; `done` on transition to `Done`; `updated` on any other change.
- Payload `{ storyId, featureId, epicId, repositoryId, title, status, schema_version: 1 }`.
- Required headers per ADR 0001.

**Tests.**
- Unit: each transition produces exactly one event of the right kind.
- Integration: InMemory subscriber observes the event end-to-end.

**Docs.** Subject list in `docs/architecture.md`.

### Story 15.4 — Publish `andy.issues.events.repository.*`
**Feature.** Emit repository lifecycle (`registered`, `synced`).
**AC.**
- `registered` on `Repository` creation via REST/MCP/CLI/gRPC.
- `synced` on successful GitHub or AzDo sync with `{ repositoryId, provider, added, updated, skipped, errorCount, schema_version: 1 }`.
- Failed syncs do **not** publish `synced` (error metrics stay in the DTO).

**Tests.**
- Integration: sync endpoint emits exactly one event on the happy path.
- Unit: failed sync emits none.

**Docs.** Subject list update.

### Story 15.5 — Publish `andy.issues.events.sandbox.*`
**Feature.** Emit sandbox lifecycle events.
**AC.**
- `attached` when a `Sandbox` row is created against an andy-containers container.
- `detached` on sandbox release.
- `failed` when `SandboxService` records a provisioning error.
- Payload `{ sandboxId, containerId, repositoryId, branch, status, schema_version: 1 }`.

**Tests.**
- Unit: transitions produce matching events.
- Integration: attach/detach flow observed via InMemory subscriber.

**Docs.** Subject list update.

### Story 15.6 — Subscribe to `andy.containers.events.run.*` (cross-repo)
**Feature.** Correlate run outcomes back to `UserStory` state.
**AC.**
- `ContainerRunEventConsumer` listens for `run.*.finished|failed|cancelled`.
- Payload `{ runId, storyId, status, exitCode?, durationSeconds? }` (contract owned by andy-containers).
- `finished` → story `InReview`; `failed`/`cancelled` → keep `InProgress`, append activity note.
- Idempotent per `msg-id`.
- Always-on once registered (AK4 — andy-tasks epic). Per-consumer disable is via `nats consumer pause`, not configuration.

**Tests.**
- Unit: state transitions per event kind.
- Integration (env-gated): publish a synthetic `run.finished` and observe the state flip.

**Docs.** `docs/architecture.md` subscriber section.

**Cross-repo.** Blocked on andy-containers outbox + publisher (andy-tasks ADR Phase 3).

### Story 15.7 — NATS provider wire-up
**Feature.** Select NATS vs InMemory via `Messaging:Provider`.
**AC.**
- `NatsMessageBus` implements `IMessageBus` with JetStream + header mapping + durable consumers + DLQ routing.
- `NatsOptions` (`Url`, `StreamName`, `StreamSubjects`, `MaxAge`, `DlqPrefix`).
- `NatsStreamProvisioner` hosted service auto-creates the stream on boot.
- `docker-compose.yml` adds `nats:2-alpine` with JetStream.
- `Program.cs` switches implementations on the config key.

**Tests.**
- Unit: option binding, DI selection.
- Integration: covered by 15.8.

**Docs.** `docs/deployment.md` Messaging section.

### Story 15.8 — NATS integration tests + CI job
**Feature.** Env-gated end-to-end tests against a real NATS broker.
**AC.**
- Env flag `ANDY_ISSUES_TEST_NATS=true` required (tests skipped otherwise).
- Cases: round-trip, generation-limit enforcement, outbox → NATS E2E, DLQ routing.
- CI `integration-nats` job starts NATS in a sidecar and sets the flag.

**Tests.** This IS the test story.

**Docs.** `docs/testing.md` Messaging tests.

---

## Cross-cutting telemetry

Every service method added in Epics 2–6 emits an OpenTelemetry span with attributes
`andy.issues.repository.id`, `andy.issues.user.id`, `andy.issues.sandbox.id` where
applicable. Not split into a separate story — included in each story's "Implementation"
step as a one-line `using var span = ActivitySource.StartActivity(...)`. Listed here
as a reminder, not a deliverable.
