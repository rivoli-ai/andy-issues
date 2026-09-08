# Agent rules

Open a repository's Backlog and choose **Agent rules** to manage named Markdown instruction profiles. Owners can add, rename, reorder, preview, remove and save profiles. Shared readers can inspect them. Changes remain local until **Save profiles** succeeds; a failed save retains the draft.

A nonempty profile list has exactly one default. Removing it promotes the remaining profile with the highest sort order. Names are unique within a repository, ignoring case, and limited to 120 characters; bodies are limited to 65,536 characters. Each repository supports up to 100 profiles.

Choose a profile when creating a story, or use the selector beside an existing story. **Repository default** follows later default changes. Deleting a selected profile clears the selection. Effective instructions resolve in this order: selected profile, repository default, then the `andy-issues:agent-rules:system-default` setting. An explicitly empty profile remains an intentional empty instruction set.

## API

- `GET /api/repositories/{id}/agent-rules`: backward-compatible `rules` string plus `profiles` and caller `canEdit`.
- `PUT /api/repositories/{id}/agent-rules`: legacy `{ "rules": "..." }` updates the default profile.
- `GET /api/repositories/{id}/agent-rules/profiles`: ordered profile array.
- `POST /api/repositories/{id}/agent-rules`: create using `{ "name": "Review", "body": "...", "isDefault": false, "sortOrder": 0 }`.
- `PUT` or `DELETE /api/repositories/{id}/agent-rules/{ruleId}`: update or remove a profile.
- `POST /api/repositories/{id}/agent-rules/replace`: atomically save an array of profiles. Include IDs for existing profiles; omit IDs for new ones. Omitted existing profiles are deleted. Multiple defaults or duplicate names return 400 without changing saved data.
- `POST /api/agent-rules/{ruleId}/set-default`: choose a default.
- `PUT /api/stories/{id}/agent-rule`: `{ "agentRuleId": "UUID" }`, or null to inherit. Foreign-repository profile IDs return 400.
- `GET /api/stories/{id}/effective-agent-rules`: `rules`, `source`, `agentRuleId`, and `name`.

Profile writes require repository ownership; story selection and reads require repository access. PostgreSQL row locks and transactional unique indexes protect default/name changes. Embedded SQLite uses transactions and a bounded process lock shared by selections and deletions.

## CLI and MCP

```sh
andy-issues repos rules list REPOSITORY_ID
andy-issues repos rules put REPOSITORY_ID --name Review --file rules.md
andy-issues repos rules set-default RULE_ID
andy-issues stories rule set STORY_ID RULE_ID
andy-issues stories rule set STORY_ID --default
```

`put` updates an existing profile by name or creates it. MCP exposes `list_agent_rules`, `create_agent_rule`, `update_agent_rule`, `delete_agent_rule`, and `get_effective_agent_rules`.

Existing repository rule text migrates to a **Default** profile. PostgreSQL performs the backfill in the schema migration; the embedded SQLite startup path also performs an idempotent backfill. The legacy repository field remains synchronized for older consumers. Conductor sandbox injection must adopt the effective-story endpoint separately; this service change preserves its current default-rule contract.
