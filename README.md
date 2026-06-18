# demo-boards-winui-app

Reactor desktop shell: a WinUI app that embeds the local board runtime in-process.

## Architecture

- The board "brain" is the platform-free JS stack from `yaml-flow` (producer
  `server-runtime-core`, `compute-jsonata`, reducer/consumer `board-state-reducer`
  + `notification-consumer` + `board-sse-state`). It is carried over **unchanged** —
  never re-ported to C# (a C# port would reintroduce rounding/JSONata divergence).
- The C# layer supplies only the I/O adapters and the UI:
  - storage adapter (localfs)
  - LLM executor plugin (copilot / foundry)
  - WinUI shell rendering from the reducer snapshot
- Transport boundary (see migration plan):
  - `agentface` (`/mcp`, `/mcp-raw`) is the **single HTTP face** — external agents connect inbound.
  - controlface, watchers/SSE, and webhooks **collapse to in-process calls** (no HTTP).

## Status

Phase E (WinUI shell) scaffold — repo initialized. Implementation pending the
contract goldens (Phase A) and the in-process composition seam (Phase B).
