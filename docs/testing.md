# Testing

## Backend Tests

### Unit Tests

Located in `tests/Andy.Issues.Tests.Unit/`.

```bash
dotnet test tests/Andy.Issues.Tests.Unit
```

Uses:
- **xUnit** - Test framework
- **EF Core InMemory** - In-memory database for isolated tests
- **coverlet** - Code coverage collection

### Integration Tests

Located in `tests/Andy.Issues.Tests.Integration/`.

```bash
dotnet test tests/Andy.Issues.Tests.Integration
```

Uses:
- **WebApplicationFactory** - In-process API testing
- **xUnit** - Test framework

### Running All Tests

```bash
dotnet test
```

With coverage:
```bash
dotnet test --collect:"XPlat Code Coverage"
```

## Frontend Tests

### Unit Tests (Karma/Jasmine)

```bash
cd client
npm test
```

Headless mode:
```bash
npm test -- --watch=false --browsers=ChromeHeadless
```

### E2E Tests

Located in `client-tests/e2e/`.

```bash
# TODO: Configure Playwright or Cypress
```

## Fakes and Fixtures

Integration tests use `TestWebApplicationFactory` (in
`tests/Andy.Issues.Tests.Integration/`) which replaces all external
dependencies with in-memory fakes:

| Fake | Replaces | Purpose |
|------|----------|---------|
| `FakeGitHubClient` | `IGitHubClient` | GitHub API calls |
| `FakeAzureDevOpsClient` | `IAzureDevOpsClient` | Azure DevOps API calls |
| `FakeContainersClient` | `IContainersClient` | andy-containers sandbox lifecycle |
| `FakeCodeIndexClient` | `ICodeIndexClient` | andy-code-index repo registration & analysis |
| `FakeAndySettingsClient` | `IAndySettingsClient` | andy-settings key-value store |
| `FakeSecretStore` | `ISecretStore` | Encrypted secret storage |
| `FakePermissionChecker` | `IPermissionChecker` | Andy RBAC permission checks |
| `FakeMcpToolDiscoveryClient` | `IMcpToolDiscoveryClient` | MCP tool discovery |

Tests seed data into fakes before exercising the API. Authentication is
handled by `TestAuthHandler`, which always authenticates as `dev-user`.

## CI Gates

The CI pipeline (`ci.yml`) enforces:

1. **Gitleaks secret scan** — blocks the build if secrets are found.
2. **.NET build + test** — `dotnet test` with code coverage.
3. **Angular lint + test + build** — Karma headless + production build.
4. **Code boundary guards** — greps for removed code patterns
   (`CodeAnalysis`, `FileAnalysis`, `features/code`) and fails if found.
5. **Format check** — `dotnet format --verify-no-changes`.

## Test Strategy

| Layer | Type | Framework | Database |
|-------|------|-----------|----------|
| Domain | Unit | xUnit | None |
| Services | Unit | xUnit | InMemory |
| Controllers | Integration | xUnit + WebApplicationFactory | InMemory |
| Angular | Unit | Karma/Jasmine | Mock |
| Angular | E2E | Playwright/Cypress | Real |
