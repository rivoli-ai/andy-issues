# UI conventions

The Issues web client uses the published `@andy-ui/angular` and `@andy-ui/tokens` 0.1.0 packages for the application shell, sidebar navigation, breadcrumbs, theme control and mutation notifications. Rivoli colors override the shared semantic tokens; existing feature controls use aliases to those tokens.

The theme starts from `andy-issues.theme`, falling back to the system preference. The shared toggle persists the choice. Breadcrumbs follow the active route and retain the repository navigation level. The navigation drawer supports Escape, focus trapping and focus restoration; modified link clicks retain browser behavior.

Successful API mutations and failures use the shared toast service. Forms also retain their contextual validation and errors. The toast host is a polite live region.

The published package does not yet export the dock/noVNC, Markdown/Mermaid or image-lightbox primitives requested in issue #107. Track [Andy UI #2](https://github.com/rivoli-ai/andy-ui/issues/2) for the dock/diagram contract and [Andy UI #36](https://github.com/rivoli-ai/andy-ui/issues/36) for safe Markdown and accessible image viewing. Adopt versioned exports after release. The current sanitized profile preview remains in place; no replacement viewer framework is bundled here.

The 0.1.0 theme-toggle markup and token selector disagree; a scoped selector adapter supplies sizing and visible theme icons until the upstream package aligns them. The production initial bundle is approximately 602 kB, above the existing 500 kB warning threshold and below its unchanged 1 MB error limit. Root Angular bindings currently register the complete core library.

Backlog descriptions, agent rules and help support GitHub-flavored Markdown.
Mermaid code fences render diagrams; invalid diagrams retain their source.
Activate an image with a click, Enter or Space to enlarge it. Escape closes the
viewer, and arrow keys move between images.

Your sandbox viewers remain available while you move between pages. Use the
tray to restore the dock, or Connect on the Sandboxes page. The dock can move to
the right, bottom or a floating position. Closing a viewer does not destroy its
sandbox; use the explicit controls on the Sandboxes page to end environments.
