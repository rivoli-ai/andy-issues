# AI configuration access review — #93

Reviewed by: cedar-heron (implementation agent), 2026-09-08, before endpoint implementation.
Decision: proceed only with the controls and regression coverage below. This is
an engineering review, not a claim of an independent human security approval.

## Findings in the existing implementation

- There is no `/api/ai-config` endpoint. Conductor’s consumer is an explicit stub.
- LLM DTOs omit keys and CRUD checks setting ownership.
- `SecretStore.StoreAsync` fabricates a Settings reference without writing a
  secret, or stores plaintext in local mode. Its comments claiming encrypted
  persistence do not establish it. Keys lost by historical reference-only
  writes cannot be reconstructed and must be re-entered.
- Most generation paths hand the stored string directly to a provider; adding
  encrypted persistence must update those paths together.
- The API pipeline does not enable HTTP body logging. Telemetry must receive
  metadata only; neither audit records nor error messages may contain the key.

## Reviewed implementation constraints

1. Store new LLM keys as ASP.NET Data Protection payloads, with a stable
   application purpose and persistent key ring. Protect legacy plaintext rows
   at startup before serving requests. Continue resolving existing Settings
   references; fail closed when resolution fails. Preserve ordinary DTO masking.
2. Read a repository override only when the caller owns both repository and
   configuration. Otherwise return 404 without resolving any secret. Default
   lookup is scoped to the caller. Sharing a repository does not share its key.
3. Require authentication even in development; do not grant an implicit admin
   bypass. Return the raw key only on this dedicated controller action.
4. Limit to ten requests per minute per authenticated caller with no queue.
   Persist a metadata-only access attempt before handling each request and
   record its outcome. Refuse the response if initial audit persistence fails.
5. Set `Cache-Control: no-store`, no ETag, and no response body logging. Return
   generic errors for unavailable/unreadable secrets. Document the sensitivity
   in OpenAPI and verify headers, authorization, limits, storage, and audits.
6. Require HTTPS at the remote edge. The embedded desktop proxy may use
   loopback HTTP within the local machine; it must never be exposed publicly.
   Consumers hold keys in memory solely for sandbox injection, never ordinary
   settings caches or Keychain persistence.

The key ring must be retained across restarts and protected using deployment
filesystem permissions (and a platform key protector where available). Database
backups alone must not contain plaintext keys; this does not protect against a
machine administrator who can read both the database and key ring. Historical
backups may still contain plaintext and require separate retention handling.

## Approval status

Automatic approval review rejected the raw-key endpoint and its new access-audit
changes because broad issue-work authorization did not explicitly authorize
credential exposure. Explicit user approval was requested. The current patch
contains only encrypted LLM storage, legacy plaintext protection, and provider
credential resolution; it adds no raw-key API. The endpoint portion remains open.
