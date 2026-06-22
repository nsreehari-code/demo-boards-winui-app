# Frontend / Backend Runtime Parity Matrix

Goal: keep this file small and decisive. This is the non-visual parity matrix for the WinUI host versus the combined frontend + backend system.

Status meanings:

- `done`: materially preserved in WinUI
- `needs work`: should be portable, but is not yet carried through fully
- `genuine drift`: host-level difference that is intentionally unavoidable and belongs in the genuine drift doc

## Matrix

| Surface | Canonical source | WinUI handling | Status | Notes |
| --- | --- | --- | --- | --- |
| Frontend global app config transport/server knobs | `demo-boards-frontend/src/lib/appConfig.js` | Collapsed into embedded host runtime/bootstrap behavior | genuine drift | External browser transport selection and server URL configuration are replaced by embedded runtime hosting. |
| Frontend global app config canvas defaults | `demo-boards-frontend/src/lib/appConfig.js` | Loaded through the WinUI app config and applied to native canvas layout defaults | done | WinUI now has a first-class app config surface for portable canvas defaults instead of hardcoded engine constants. |
| Frontend managed board `ui` / `metadata` / `layout` | `manage-boards` board record + layout | Read, written, and consumed directly by WinUI | done | WinUI loads and saves the same logical managed-board contract. |
| Frontend theme selection via `ui.theme.id` | board `ui` | Consumed directly by WinUI theme switching | done | Shared per-board theme contract is preserved. |
| Frontend pane rules | board `ui.paneRules` | Compiled by native `CardPresentationConfig` helper | done | Behavior is preserved even though implementation is C#. |
| Frontend card renderer rules | board `ui.cardRendererRules` | Compiled by native `CardPresentationConfig` helper | done | Behavior is preserved even though implementation is C#. |
| Backend hosted runtime template selectors | `templates-config.json` fields used by add-board/manage-board flows | Enumerated from the canonical backend host config resolver and used directly by WinUI | done | WinUI now loads canonical selector options from the backend resolver instead of relying on freeform text placeholders. |
| Backend sample template catalog | hosted runtime sample template catalog | Packaged into WinUI output and surfaced in board config modal | done | Same template payloads and index are shipped. |
| Backend agentface tools manifest | hosted runtime `agentface.tools.json` | Packaged into WinUI output | done | Manifest artifact is preserved. |
| Backend managed-board persistence | controlface managed-board state + layout state | Preserved through embedded controlface storage seam | done | Same conceptual state surface, different host storage plumbing. |
| Registered chat assistants | `demo-boards-ns-code/demo-board/server/chat-flow/assistant_registry.json` | Invoked through the WinUI host bridge by the real backend chat handler module | done | WinUI now dispatches through the backend assistant registry instead of a stub. |
| Backend chat assistant execution semantics | hosted runtime chat-flow loader and assistant modules | Executed by the real backend chat handler module via the WinUI host bridge | done | The embedded runtime now hands chat requests to the backend assistant loader instead of only recording dispatch metadata. |
| Registered source-def registry | `demo-boards-ns-code/demo-board/server/board-worker/source_def_flows.json` | Resolved by the real backend task executor through the WinUI host bridge | done | WinUI now uses the backend registry-backed executor instead of a placeholder path. |
| Source-def flow execution behavior | backend board-worker flow registry + handlers | Executed by the real backend task executor via the WinUI host bridge | done | Source fetch dispatch now runs the backend registry-driven worker instead of stopping at the embedded adapter seam. |
| Backend AI workspace provisioning/admin surface | backend hosted runtime setup scripts and workspace config | Exposed through WinUI host-config inspection, effective board-config preview, boards-index sync, and board-scoped workspace setup | done | WinUI now points at the canonical hosted runtime config/scripts and can provision board workspaces through the existing backend setup helper. |

## Current conclusion

Already solid:

- WinUI app config surface for portable frontend/backend host settings
- managed board config
- theme contract
- canonical backend template selectors
- pane / renderer rule contract
- sample template catalog
- agentface tools manifest
- embedded controlface persistence path
- effective board-config inspection
- backend workspace bootstrap integration

Still blocking a real frontend+backend parity claim:

- no remaining parity gaps in this matrix beyond the genuine host drifts listed below

## What belongs in genuine drift instead

Only host-level transport/bootstrap differences belong in the genuine drift doc, such as:

- embedded runtime instead of configurable browser server URL transport
- in-process host plumbing instead of browser `EventSource` lifecycle
- native storage/window/file-picker seams

No other items in this matrix belong in the drift doc.