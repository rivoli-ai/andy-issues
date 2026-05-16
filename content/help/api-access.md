---
title: API Access
order: 2
tags: [api, swagger, mcp, grpc, cli]
---

# API Access

## REST / Swagger

Interactive API documentation is available at `/swagger` when running in Development mode.

All endpoints require a Bearer token from Andy Auth unless running without auth configured.

### Authentication

```
Authorization: Bearer <your-jwt-token>
```

### Base URL

- Development: `https://localhost:5410`
- Docker: `https://localhost:5410`

### REST Controllers

| Controller | Prefix | Purpose |
|-----------|--------|---------|
| Repositories | `/api/repositories` | CRUD, sharing, GitHub/Azure DevOps sync |
| Backlog | `/api/repositories/{id}/backlog` | Epics, features, stories, AI generation |
| Sandboxes | `/api/sandboxes` | Create, list, connect, destroy dev environments |
| MCP Configs | `/api/mcp-configs` | Personal + shared MCP server configurations |
| Artifact Feeds | `/api/artifact-feeds` | NuGet/npm registry management |
| Linked Providers | `/api/linked-providers` | GitHub & Azure DevOps OAuth tokens |
| Users | `/api/users` | User directory, suggest users for sharing |

## MCP (Model Context Protocol)

Connect AI assistants to this service via the `/mcp` endpoint.

### Supported Clients

- Claude Desktop
- ChatGPT
- VS Code extensions (Cline, Roo, Continue)

### Available Tools (17)

**Repositories**: ListRepositories, GetRepository, SyncGitHubRepositories, SyncAzureDevOpsRepositories, DeleteRepository

**Backlog**: ListBacklog, CreateEpic, CreateFeature, CreateStory, UpdateStoryStatus, GenerateDraftBacklog

**Sandboxes**: CreateSandbox, ListSandboxes, GetSandboxConnection, DestroySandbox

**Configuration**: ListMcpConfigs, ListEnabledArtifactFeeds

### Help Tools

ListHelpTopics, GetHelpTopic, SearchHelp — browse this help content via MCP.

## gRPC

For service-to-service communication. Proto definitions are in `src/Andy.Issues.Api/Protos/`.

| Service | Proto | Operations |
|---------|-------|------------|
| Repositories | `repositories.proto` | List, Get |
| Backlog | `backlog.proto` | GetBacklog, CreateEpic, CreateFeature, CreateStory |
| Sandboxes | `sandboxes.proto` | Create, List, Destroy |

## CLI

```bash
# List repositories
dotnet run --project tools/*.Cli -- repos list --api-url https://localhost:5410

# View backlog
dotnet run --project tools/*.Cli -- backlog list --repo-id <guid>

# List sandboxes
dotnet run --project tools/*.Cli -- sandboxes list

# With authentication
dotnet run --project tools/*.Cli -- repos list --token <bearer-token>
```
