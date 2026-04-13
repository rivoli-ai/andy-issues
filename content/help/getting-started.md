---
title: Getting Started
order: 1
tags: [onboarding, quickstart]
---

# Getting Started

Andy Issues is a project management and development environment service. It manages code repositories, backlog items (epics, features, user stories), and container-based sandboxes for development.

## Sign In

Use your Andy Auth credentials to sign in. For development:

- **Email**: `test@andy.local`
- **Password**: `Test123!`

## Navigate

- **Repositories** — Register and manage code repositories (GitHub, Azure DevOps)
- **Backlog** — Plan work with epics, features, and user stories
- **Sandboxes** — Spin up container-based dev environments for any repo branch
- **MCP Configs** — Manage MCP server configurations (personal + shared)
- **Artifact Feeds** — Configure NuGet/npm registries for sandbox injection
- **Settings** — Linked providers, LLM settings, Azure identity

## Quick Actions

1. Sign in with your Andy Auth credentials
2. Sync your repositories from GitHub or Azure DevOps
3. Create a backlog (or generate one with AI)
4. Launch a sandbox to start coding

## API Access

This service exposes multiple API protocols:

- **REST / Swagger** — Interactive API docs at `/swagger`
- **MCP** — AI assistant integration at `/mcp` (17 tools)
- **gRPC** — High-performance RPC for service-to-service calls
- **CLI** — Command-line tool for terminal workflows
