---
title: Andy Issues Overview
slug: andy-issues-overview
order: 1
tags: [issues, stories, backlog]
---

# Andy Issues Overview

Andy Issues owns issue tracking, per-repo story refinement, and backlog management for the Andy ecosystem. It is the canonical home for stories Conductor surfaces in the Issues tab and feeds into agent runs.

## What it does

- Stores stories scoped per repository — title, body, status, priority, labels, and the refinement history.
- Refines stories with AI-assisted suggestions (acceptance criteria, test plan, edge cases) callable from the Conductor UI.
- Tracks blocking relationships so Conductor can compute execution waves from the dependency graph.
- Publishes change events on NATS so the Conductor Issues tab updates live without polling.
- Acts as the long-term store for stories that originated outside Conductor (GitHub, Azure DevOps sync).

## Key concepts

- **Story** — the unit of work an agent run can consume. Has structured fields plus a free-form body.
- **Refinement** — an AI pass that proposes acceptance criteria + test plan. The user keeps, edits, or discards.
- **Repository scope** — every story belongs to exactly one repo. Cross-repo work is modeled as linked stories.

## Where it fits

Conductor's Issues tab is a thin client over Andy Issues. Agent runs consume stories as their primary input. Depends on Auth, RBAC, Settings, and Code Index (for refinement that needs to grep the source).

## Configuration

Refinement model defaults, polling intervals, and label vocab live under `andy.issues.*` in `andy-settings`. Per-repo overrides live in `.andy/issues.yml` inside the repo.

## Troubleshooting

- **Story list empty after sync** — provider token expired or scope is wrong. Re-link the provider in **Settings → Connections**.
- **Refinement times out** — model provider is slow or down. Try a different model in **Settings → Models** and re-run.
- **Live updates not arriving** — NATS connection broke; restart the service and the Conductor tab will reattach.
