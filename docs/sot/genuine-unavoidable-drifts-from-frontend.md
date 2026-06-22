# Genuine Unavoidable Drifts From Frontend

Goal: parity with the frontend in all functionality, with this document limited to drifts that remain genuinely unavoidable.

This file records only drifts that appear necessary because the WinUI app is an embedded desktop host, not a browser client.

## Accepted drifts

- No configurable local server URL transport in the main WinUI app flow.
  Reason: the WinUI app uses embedded controlface, agentface, and queue-runner/runtime hosting in-process rather than talking to an external hosted runtime over browser HTTP + SSE.

- No external browser `EventSource` lifecycle as the primary board-state ingress path.
  Reason: WinUI consumes the embedded runtime snapshot/notification flow via in-process host plumbing, even when the app still exposes localhost agentface for external clients.

- Platform host plumbing remains in native C#.
  Reason: storage, app window lifecycle, packaged/unpackaged launch behavior, and native file picking are desktop host concerns, not frontend behavior concerns.

- Some UI/runtime helper modules remain native C# mirrors of frontend helper modules.
  Reason: the WinUI host needs local, strongly-typed helper seams at the native UI boundary for deterministic canvas placement and pane/renderer rule resolution. Concretely, `BoardCanvasLayoutEngine` mirrors `src/lib/boardCanvasLayout.js`, and `CardPresentationConfig` mirrors `src/lib/cardPresentationConfig.js`. This is retained for host-language modularity, not because WinUI needs distinct product behavior.

- Shared iconography now requires host-packaged SVG assets in addition to frontend inline SVG fragments.
  Reason: the frontend can keep many icons inline inside React component markup, while the WinUI host needs those same shapes surfaced as packaged asset files and icon-source helpers to reuse them across native controls.