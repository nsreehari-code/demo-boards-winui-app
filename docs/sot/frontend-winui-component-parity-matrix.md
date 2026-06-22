# Frontend vs WinUI Surface Parity Matrix

This matrix tracks parity across frontend top-level UI surfaces, WinUI top-level controls, and the small number of helper-module counterparts that sit at the same presentation/runtime boundary.

Status meanings:

- `done`: the current implementation is close enough to treat as the parity baseline for the relevant dimension.
- `recheck`: the surface exists, but the relevant dimension should be re-audited before being treated as closed.
- `needs work`: a corresponding surface/helper exists, but the relevant dimension still differs materially.
- `unverified`: the surface/helper may be in a good place, but that dimension has not yet had a dedicated parity pass strong enough to mark `done`.
- `missing counterpart`: no distinct top-level surface or helper counterpart exists on the other side.
- `n/a`: not a user-facing visual surface, so visual-appeal parity does not apply.

Column intent:

- `Functionality`: state consumption, inputs, rendering logic, effects/actions, and workflow coverage.
- `Visual appeal`: layout role, styling, theme-pack behavior, and overall presentation quality.
- `Interaction / UX`: focus, keyboarding, hover/press/drag behavior, modal mechanics, paging/scroll feel, and other interaction details.
- `Performance`: responsiveness and behavior under realistic board/chat/card load. This is judged on its own bar, not merely by matching any frontend weakness.

Current audit posture:

- `Functionality` calls below reflect concrete parity implementation and follow-up validation already completed.
- `Visual appeal` is temporarily reset to `recheck` across component surfaces because visible layout parity issues remain and need a fresh dedicated pass.
- `Interaction / UX` and `Performance` are intentionally stricter: any row still marked `unverified` has not yet had a dedicated enough pass to justify `done`.

## Matched top-level UI surfaces

| Frontend component | WinUI counterpart | Functionality | Visual appeal | Interaction / UX | Performance | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| `AppConfigModal` | `AppConfigModal` | done | recheck | unverified | unverified | WinUI now covers add-board, template-backed add-board, import/export, template preview/ingest, config save flows, and the shared theme-pack selection path. |
| `BoardCanvas` | `BoardCanvas` | done | recheck | done | needs work | WinUI now provides the infinite-canvas board surface with persisted layout, token focus, card focus, minimap, zoom, fit/reset, renderer-rule-driven card rendering, and theme-aware canvas chrome. Drag, resize, double-tap focus, minimap navigation, and viewport restore behaviors are concretely implemented. Performance still needs work because the surface clears and rebuilds the full canvas visual tree, connection visuals, and background grid on rerender rather than using a more incremental update path. |
| `BoardMarkdown` | `BoardMarkdown` | done | recheck | done | needs work | WinUI now uses a parser-backed markdown surface with GFM-style rendering for tables, images, code blocks, richer inline formatting, optional wrapper class/style inputs, and active-theme WebView styling. Interaction is simple and sufficiently aligned; performance still needs work because the control drives WebView2 through `NavigateToString()` on render rather than a lighter incremental path. |
| `CardBackface` | `CardBackface` | done | recheck | done | unverified | WinUI backface now exposes the same actionable surface for token inspection plus card/source preflight entrypoints when used inside inspect flows, with theme-aware token chips and panels. The interaction surface is concrete and explicit; performance has not yet had a dedicated pass. |
| `CardCore` | `CardCore` | done | recheck | unverified | needs work | WinUI core renderer resolves binds, refs, visibility, and writeback patching against the same runtime/data namespaces used for board rendering. Performance still needs work because each render clears the host and recreates `CardCoreView` instances for every visible element. |
| `CardCoreView` | `CardCoreView` | done | recheck | unverified | needs work | WinUI now covers the current render kinds, including chart rendering alongside actions, selection, forms, notes, editable tables, todo, alert, metric, markdown, and narrative/text surfaces, with theme-aware WebView output. Performance still needs work because several rich render kinds rebuild controls from scratch and chart/markdown paths create WebView-backed surfaces per render. |
| `CardRenderer` | `CardRenderer` | done | recheck | unverified | needs work | Both sides dispatch cards into specialized renderers such as strategist, ingest, postbox, or shell. Performance still needs work because the dispatcher clears the host and recreates the chosen specialized control tree on render. |
| `CardShell` | `CardShell` | done | recheck | unverified | needs work | WinUI shell now carries inspect, chat-popout, path-state, token-row, front/back runtime flip, shared card-core rendering responsibilities, and theme-aware shell/status treatments. Performance still needs work because front-shell content is rebuilt on render, including fresh `CardCore` and mini-chat instances when present. |
| `CentrePane` | `CentrePane` | done | recheck | done | needs work | WinUI now supports both the infinite-canvas and flowing-cards centre-pane roles, with explicit layout-strategy handling from the same orchestration boundary. Interaction is sufficiently aligned at the pane level; performance still needs work because flowing-card mode rebuilds the full grid and canvas mode inherits the audited canvas performance gap. |
| `ChallengeConfirmModal` | `ChallengeConfirmModal` | done | recheck | done | unverified | WinUI now implements the arithmetic challenge flow, answer validation, focus behavior, Enter/Escape keyboard handling, and parity-appropriate modal presentation. The keyboard/focus path has been explicitly audited more than most surfaces. |
| `ChatPane` | `ChatPane` | done | recheck | needs work | needs work | WinUI now covers live chat, staged attachments, anchored history paging, working-bubble state, compact mode, popout-to-full-chat composition, and the current theme-token chat treatments. Interaction still needs work because the frontend chat surface includes sticky-scroll behavior, scheduled auto-scroll, expandable message handling, and textarea growth behavior that the WinUI pane does not yet mirror. Performance also needs work because the WinUI pane rebuilds the full message stack and markdown surfaces on state changes without a lighter incremental path. |
| `GandalfPane` | `GandalfPane` | done | recheck | done | done | WinUI Gandalf pane now matches the frontend role as a toggleable side rail over filtered ingest cards with navigation and renderer reuse. The pane renders one current card at a time and maintains index/navigation state without additional complexity. |
| `GlobalModal` | `GlobalModal` | done | recheck | done | done | The generic modal host pattern is aligned, including the current theme-token overlay and shell treatment. It is a thin host with explicit close/backdrop behavior and no meaningful performance risk beyond hosted content. |
| `GandalfChatPane` | `GandalfChatPane` | done | recheck | needs work | needs work | WinUI now exposes the ingest-side named chat wrapper as a real control over the shared chat pane. Because it is a thin wrapper over `ChatPane`, it inherits the current chat interaction and performance gaps rather than escaping them. |
| `IngestCard` | `IngestCard` | done | recheck | needs work | needs work | WinUI ingest card now mirrors the frontend pattern as a thin compact-chat wrapper with popout support. Because its body is essentially the compact chat surface, it inherits the current `ChatPane` interaction and performance gaps. |
| `InspectCard` | `InspectCard` | done | recheck | unverified | needs work | WinUI inspect now includes structured trial-run output, token inspection, source/card preflight actions, preview, backface, files, metadata, embedded chat, and the current theme-aware pane/pill treatments. Performance still needs work because the surface clears and rebuilds multiple content hosts, embeds chat/backface/preview content, and reparses large JSON/text blocks on render. |
| `MainBoard` | `MainBoard` | done | recheck | done | needs work | WinUI main board now mirrors the frontend role by orchestrating Gandalf, Truthset, and centre/canvas surfaces from managed-board UI config, renderer rules, and shared theme-pack selection. Interaction is sufficiently aligned at the orchestration level; performance still needs work because it delegates to audited child surfaces with known rebuild costs, especially the centre/canvas path. |
| `MiniChatPane` | `MiniChatPane` | done | recheck | needs work | needs work | WinUI now exposes the shell-level compact chat as a distinct control and uses it for inline shell chat with popout support. Because it is only a thin wrapper over `ChatPane`, it inherits the current chat interaction and performance gaps. |
| `PostboxCard` | `PostboxCard` | done | recheck | needs work | needs work | WinUI postbox now covers submission history, stored files, upload staging, upload comments, compact/live chat composition, and theme-aware card/drop-zone treatments. Interaction still needs work because the frontend surface has explicit submissions/files view toggles, incremental history paging, smoother scroll behavior, and richer stage/composer affordances that the WinUI card currently folds into a simpler single-flow layout. Performance also needs work because WinUI currently loads full submission history in one call and rerenders the full grouped history on refresh. |
| `SmokeRunner` | `SmokeRunner` | done | recheck | done | unverified | WinUI now exposes an in-app smoke runner modal from board configuration and executes the embedded GoldenHarness suite directly. The interaction model is simple and explicit for a developer surface; performance still has not had a dedicated pass beyond basic execution behavior. |
| `StrategistCard` | `StrategistCard` | done | recheck | unverified | needs work | WinUI strategist card provides the same specialized status/path-state/core-renderer composition used for strategist-rendered cards. Performance still needs work because the surface clears and recreates its `CardCore` content on render. |
| `TruthsetExplorePane` | `TruthsetExplorePane` | done | recheck | done | done | WinUI Truthset pane now matches the frontend role as a toggleable filtered side rail with phase badge, navigation, renderer reuse, and theme-aware badge treatment. Like GandalfPane, it renders one current card at a time and keeps its own interaction model narrow and explicit. |

## Frontend top-level UI surfaces with no distinct WinUI counterpart

None at the current top-level control surface.

## WinUI top-level controls/helpers with no distinct frontend counterpart

| WinUI control/helper | Frontend counterpart | Functionality | Visual appeal | Interaction / UX | Performance | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| `FrePage` | missing counterpart | done | recheck | done | done | New WinUI-only first-run entry experience that keeps app startup on a trivial surface and defers navigation into the heavier board canvas until the user explicitly clicks `Enter the Board`. This is a new host requirement, not a missed frontend parity surface. |

## WinUI helper modules with frontend helper-module counterparts

| WinUI helper | Frontend counterpart | Functionality | Visual appeal | Interaction / UX | Performance | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| `BoardCanvasLayoutEngine` | `src/lib/boardCanvasLayout.js` | done | n/a | n/a | needs work | Genuine retained helper seam: WinUI needs a native C# deterministic canvas layout engine at the board-surface layer, but the logic mirrors the frontend layout module rather than introducing unique product behavior. Performance still needs work because the full layout is recomputed for the current graph rather than incrementally updated. |
| `CardPresentationConfig` | `src/lib/cardPresentationConfig.js` | done | n/a | n/a | done | Genuine retained helper seam: WinUI needs a native C# helper for pane-filter and renderer-rule resolution, mirroring the frontend presentation-config helper in host language. The helper is stateless and narrow enough to treat current performance as acceptable until contrary evidence appears. |

## Frontend internal subcomponents folded into WinUI single controls

These do not show up as separate top-level WinUI controls, but they are not missing behavior by default because WinUI currently folds them into a larger control:

- `AddBoardModal` -> folded into `AppConfigModal`
- `PageDetailsSection` -> folded into `AppConfigModal`
- `TemplateIngestModal` -> folded into `AppConfigModal`

## Remaining outer-join differences only

All frontend top-level UI surfaces currently have a distinct WinUI counterpart or are intentionally folded internal subcomponents listed below.

The remaining differences are the explicit outer-join items above:

- native-language helper duplication where WinUI keeps C# equivalents of frontend helper modules at the UI/runtime boundary