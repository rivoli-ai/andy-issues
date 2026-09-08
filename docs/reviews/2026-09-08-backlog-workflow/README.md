# Backlog workflow review — 2026-09-08

Issue: #206. The before baseline is main after shared UI PR #220; the older placeholder-only report was already partially addressed by PR #208. Both captures use the real Angular application with synthetic authentication and intercepted API fixtures, not a live backend. Temporary review entrypoints are excluded from the production change.

The change adds repository and parent context, native keyboard form submission, duplicate-submission guards, in-dialog failure recovery with retained fields, loading/retry feedback, readable mobile story controls and in-place refinement. Refinement uses the existing 202 endpoint and refreshes results on request; it does not claim to stop or automatically monitor the remote run. Late requests from a previous repository are cancelled, and stale refreshes cannot overwrite newer results.

## Matching captures

| State | Desktop before / after | 375px before / after |
| --- | --- | --- |
| populated | [Before](before-1440-populated.png) / [After](after-1440-populated.png) | [Before](before-375-populated.png) / [After](after-375-populated.png) |
| empty | [Before](before-1440-empty.png) / [After](after-1440-empty.png) | [Before](before-375-empty.png) / [After](after-375-empty.png) |
| loading | [Before](before-1440-loading.png) / [After](after-1440-loading.png) | [Before](before-375-loading.png) / [After](after-375-loading.png) |
| failed | [Before](before-1440-failed.png) / [After](after-1440-failed.png) | [Before](before-375-failed.png) / [After](after-375-failed.png) |

## Verification

- 39 Chrome Headless tests pass, including duplicate-create suppression, retained epic/feature/story fields, status failure recovery, stale loads, route-change cancellation and refinement request context.
- Production Angular build passes with the existing approximately 611 kB initial-bundle warning (500 kB warning / 1 MB error limits unchanged).
- [Keyboard browser results](keyboard.json): create epic, feature and story at 1440px, 375px, and a 720px CSS viewport at device scale 2 (the layout equivalent of 200% zoom on a 1440px display). The test verifies selected parent request IDs, persistent field labels, one pending request, retained failed input, focus restoration, status rollback, refinement result and unchanged repository URL. No page errors or clipped story controls.
- [Pending form](after-375-pending-create.png), [failed form with retained values](after-375-failed-create.png), [zoom-equivalent refinement](after-720-refinement.png). Keyboard focus was inspected; a disabled-trigger focus restoration bug was corrected during this review.
- [Full-page axe WCAG A/AA results](axe-full.json): no backlog/form/contrast findings in light or dark at desktop/375px, including failed forms. Desktop navigation still has `list` and `listitem` findings because Andy UI 0.1.0 inserts light-DOM wrappers between native lists and items. These are tracked in [Andy UI #37](https://github.com/rivoli-ai/andy-ui/issues/37), under Issues #107. The complete application scan is therefore not clean. A consumer override corrects the library's low-opacity navigation-label contrast.
- Notifications use a bounded, positioned host and replace the previous transient message, so rapid mutations do not cover the viewport. Accepted asynchronous work says “Request accepted.” Persistent failures remain in their form or story.

No native Astryx React integration is claimed. The Angular client consumes the available published Andy UI primitives from PR #220. Shared dock, Markdown/Mermaid and lightbox adoption remains tracked by #107.
