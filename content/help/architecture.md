---
title: Architecture
order: 4
tags: [architecture, design, layers]
---

# Architecture

## Overview

This service follows Clean Architecture with strict dependency rules.

```
┌─────────────────────────────────────────────┐
│                Angular SPA                   │
│           (client/ directory)                │
├─────────────────────────────────────────────┤
│              API Layer                       │
│    REST Controllers │ MCP Tools │ gRPC       │
├─────────────────────────────────────────────┤
│           Application Layer                  │
│       Interfaces │ DTOs │ Contracts          │
├─────────────────────────────────────────────┤
│         Infrastructure Layer                 │
│   EF Core │ Services │ External Integrations │
├─────────────────────────────────────────────┤
│            Domain Layer                      │
│         Entities │ Enums │ Value Objects      │
└─────────────────────────────────────────────┘
```

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Backend | .NET 8 |
| Frontend | Angular 18 |
| Database | PostgreSQL (default) / SQLite (embedded) |
| Auth | Andy Auth (OAuth2/OIDC) |
| Authorization | Andy RBAC |
| Settings | Andy Settings |
| Telemetry | OpenTelemetry |
| Containerization | Docker |

## External Dependencies

| Service | Port | Purpose |
|---------|------|---------|
| Andy Auth | 5001 | Identity provider |
| Andy RBAC | 5003 | Access control |
| Andy Settings | 5300 | Configuration |

## Domain Model

```
Repository
 ├── Epic
 │    └── Feature
 │         └── UserStory (Draft → Ready → InProgress → Done)
 ├── RepositoryShare
 └── Azure Identity (optional)

Sandbox (backed by andy-containers)
McpServerConfig (personal + shared)
ArtifactFeedConfig (NuGet / npm)
LinkedProvider (GitHub / Azure DevOps)
LlmSetting (per-user AI config)
```

## External Service Integrations

| Service | Purpose |
|---------|---------|
| **andy-containers** | Sandbox lifecycle (create, destroy, connect) |
| **andy-code-index** | Code analysis, repo registration, backlog AI context |
| **andy-settings** | Centralized configuration + secret storage |

## Database Strategy

- **PostgreSQL**: Default for standalone deployment
- **SQLite**: Used when embedded in Conductor or for lightweight deployments
- Switch via `Database:Provider` configuration (`PostgreSql` or `Sqlite`)
