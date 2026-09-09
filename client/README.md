
The shared Markdown component renders backlog descriptions, refinement output,
agent-rule previews and help topics. Mermaid fences load their renderer only
when needed; rendered images open Andy UI's keyboard-accessible lightbox.
Rendering and sanitization stay in the versioned Andy UI package.

Signed-in users get a persistent sandbox dock from `/api/sandboxes/mine`.
It starts minimized, preserves connections across routes and polls without
concurrent requests. Connect on the Sandboxes page restores a viewer. Closing a
dock viewer only dismisses the view; sandbox destruction remains an explicit
Sandboxes-page operation. Authorization failures remove viewer URLs immediately.
