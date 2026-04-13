---
title: Authentication & Authorization
order: 3
tags: [auth, oidc, rbac, security]
---

# Authentication & Authorization

## Andy Auth (OAuth2 / OIDC)

This service uses Andy Auth as its identity provider.

| Setting | Value |
|---------|-------|
| Protocol | OAuth 2.0 Authorization Code with PKCE |
| Token format | JWT Bearer |
| Auth server | `https://localhost:5001` |
| Audience | `urn:andy-issues-api` |

## Test Credentials

For development and testing only:

| Field | Value |
|-------|-------|
| Email | `test@andy.local` |
| Password | `Test123!` |
| Role | User (super-admin in RBAC) |

> These credentials are seeded automatically in non-production environments.

## Andy RBAC

Role-based access control is managed by Andy RBAC.

### Roles

| Role | Description |
|------|-------------|
| **admin** | Full access to all resources |
| **user** | Standard access (create, read, update) |
| **viewer** | Read-only access |

### Actions

`read`, `write`, `delete`, `share`, `admin`, `execute`, `export`, `import`

## Linked Providers

Andy Issues supports linking external provider accounts for repository sync:

| Provider | Purpose |
|----------|---------|
| **GitHub** | Sync repositories, create PRs |
| **Azure DevOps** | Sync repositories, bidirectional work item sync |

Link providers via the `/api/linked-providers` endpoint or the Angular settings page. Personal Access Tokens (PATs) can also be entered via the `/api/linked-providers/pat` helper endpoint.

## Swagger Authentication

1. Open `/swagger` in your browser
2. Click the **Authorize** button
3. Enter your Bearer token: `Bearer <your-jwt-token>`
4. Click **Authorize** to apply
