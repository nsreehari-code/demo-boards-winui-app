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
- `Reactor migration`: whether the WinUI surface is already off app-authored XAML and living on an imperative/Reactor-side composition seam, or is still grandfathered on legacy XAML.

`Reactor migration` meanings:

- `done`: the surface no longer depends on an app-authored `.xaml` view file.
- `planned`: the surface still depends on grandfathered app-authored XAML, but the migration slice is identified and intentionally tracked.
- `n/a`: the row is not a surface that needs its own Reactor migration decision.

Current audit posture:

- `Functionality` calls below reflect concrete parity implementation and follow-up validation already completed.
- `Visual appeal` is temporarily reset to `recheck` across component surfaces because visible layout parity issues remain and need a fresh dedicated pass.
- `Interaction / UX` and `Performance` are intentionally stricter: any row still marked `unverified` has not yet had a dedicated enough pass to justify `done`.

## Matched top-level UI surfaces

| Frontend component | WinUI counterpart | Functionality | Visual appeal | Interaction / UX | Performance | Reactor migration | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `AppConfigModal` | `AppConfigModal` | done | recheck | unverified | unverified | done | WinUI now covers add-board, template-backed add-board, import/export, template preview/ingest, config save flows, and the shared theme-pack selection path. The modal is now launched from a floating board-surface action rather than a text button in the top bar, matching the frontend's more detached settings affordance more closely. The control no longer depends on an app-authored `.xaml` view file. |
| `BoardCanvas` | `BoardCanvas` | done | recheck | done | needs work | done | WinUI now provides the infinite-canvas board surface with persisted layout, token focus, card focus, minimap, zoom, fit/reset, renderer-rule-driven card rendering, and theme-aware canvas chrome. Drag, resize, double-tap focus, minimap navigation, and viewport restore behaviors are concretely implemented. Performance still needs work because the surface clears and rebuilds the full canvas visual tree, connection visuals, and background grid on rerender rather than using a more incremental update path. |
| `BoardMarkdown` | `ReactorCardViewElementComponent` markdown render kind | done | recheck | done | needs work | done | WinUI does not keep a standalone `BoardMarkdown` surface anymore; markdown is folded into the Reactor card-view renderer. The behavior covers parser-backed markdown rendering with themed WebView output, but still rebuilds rich markdown content per render. |
| `CardBackface` | `ReactorCardBackfaceComponent` | done | recheck | done | unverified | done | WinUI backface now exposes the same actionable surface for token inspection plus card/source preflight entrypoints when used inside inspect flows, with theme-aware token chips and panels. The interaction surface is concrete and explicit; performance has not yet had a dedicated pass. |
| `CardCore` | `ReactorCardFrontContentComponent` | done | recheck | unverified | needs work | done | WinUI core rendering now lives inside Reactor card-front composition rather than a standalone hosted control. It resolves binds, refs, visibility, and writeback patching against the same runtime/data namespaces used for board rendering. |
| `CardCoreView` | `ReactorCardViewElementComponent` | done | recheck | unverified | needs work | done | WinUI covers the current render kinds, including chart rendering alongside actions, selection, forms, notes, editable tables, todo, alert, metric, markdown, and narrative/text surfaces. Performance still needs work because rich render kinds and WebView-backed paths are rebuilt from scratch on rerender. |
| `CardRenderer` | `ReactorCardRendererComponent` | done | recheck | unverified | needs work | done | The Reactor dispatcher now restores real specialized branches for strategist, ingest, postbox, and default shell rendering instead of flattening everything through the generic shell. Performance still needs work because the dispatcher recreates the chosen specialized control tree on rerender. |
| `CardShell` | `ReactorCardShellComponent` | done | recheck | unverified | needs work | done | WinUI shell carries inspect, chat-popout, path-state, token-row, front/back runtime flip, shared card-front rendering responsibilities, and theme-aware shell/status treatments. Performance still needs work because front-shell content is rebuilt on rerender, including fresh front content and mini-chat instances when present. |
| `CentrePane` | `CentrePane` | done | recheck | done | needs work | done | WinUI now supports both the infinite-canvas and flowing-cards centre-pane roles, with explicit layout-strategy handling from the same orchestration boundary. Interaction is sufficiently aligned at the pane level; performance still needs work because flowing-card mode rebuilds the full grid and canvas mode inherits the audited canvas performance gap. |
| `ChallengeConfirmModal` | `ReactorChallengeConfirmModalComponent` | done | recheck | needs work | unverified | done | WinUI again has a dedicated arithmetic challenge-confirm surface and uses it for destructive inspect/config actions. The remaining gap is modal polish: the current Reactor version does not yet match the frontend's focus and dismissal behavior closely enough to mark interaction as done. |
| `ChatPane` | `ChatPane` | done | recheck | needs work | needs work | done | WinUI now covers live chat, staged attachments, anchored history paging, working-bubble state, compact mode, popout-to-full-chat composition, and the current theme-token chat treatments. The compact chat actions now use the same packaged popout and attach SVGs as the frontend surface. Interaction still needs work because the frontend chat surface includes sticky-scroll behavior, scheduled auto-scroll, expandable message handling, and textarea growth behavior that the WinUI pane does not yet mirror. Performance also needs work because the WinUI pane rebuilds the full message stack and markdown surfaces on state changes without a lighter incremental path. |
| `GandalfPane` | `GandalfPane` | done | recheck | done | done | done | WinUI Gandalf pane now matches the frontend role as a toggleable side rail over filtered ingest cards with navigation and renderer reuse. The pane renders one current card at a time and maintains index/navigation state without additional complexity, and it now uses the shared floating edge-toggle control rather than a one-off text button. |
| `GlobalModal` | `ReactorGlobalModalComponent` | needs work | recheck | needs work | done | done | WinUI now has a dedicated Reactor modal host again, and inspect/chat/config/smoke surfaces all route through it. The current gap is behavior parity: it is still a hosted shell section rather than a true overlay/backdrop portal with Escape and outside-click dismissal semantics. |
| `GandalfChatPane` | `ReactorChatPaneComponent` compact/title variant | done | recheck | needs work | needs work | done | WinUI does not keep a separate named chat wrapper; the ingest-side chat surface is folded into the shared Reactor chat component with compact composition. It still inherits the current chat interaction and performance gaps. |
| `IngestCard` | `ReactorIngestCardComponent` | done | recheck | needs work | needs work | done | WinUI again has a dedicated Reactor ingest card surface instead of flattening ingest cards through the default shell. Because its body is still the compact chat surface, it inherits the current chat interaction and performance gaps. |
| `InspectCard` | `InspectCard` | done | recheck | unverified | needs work | done | WinUI inspect now includes structured trial-run output, token inspection, source/card preflight actions, preview, backface, files, metadata, embedded chat, and the current theme-aware pane/pill treatments. The destructive delete action now uses the same packaged trash SVG as the frontend inspect surface. Performance still needs work because the surface clears and rebuilds multiple content hosts, embeds chat/backface/preview content, and reparses large JSON/text blocks on render. |
| `MainBoard` | `MainBoard` | done | recheck | done | needs work | done | WinUI main board now mirrors the frontend role by orchestrating Gandalf, Truthset, and centre/canvas surfaces from managed-board UI config, renderer rules, and shared theme-pack selection. Interaction is sufficiently aligned at the orchestration level; performance still needs work because it delegates to audited child surfaces with known rebuild costs, especially the centre/canvas path. |
| `MiniChatPane` | `ReactorChatPaneComponent` compact variant | done | recheck | needs work | needs work | done | WinUI does not keep a separate mini-chat class anymore; shell-level compact chat is folded into the shared Reactor chat component with popout support. It inherits the current chat interaction and performance gaps. |
| `PostboxCard` | `ReactorPostboxCardComponent` | done | recheck | needs work | needs work | done | WinUI again has a dedicated Reactor postbox surface instead of routing postbox cards through the generic shell. It now covers submissions/files modes, upload staging, optional comments, and file download links, but still trails the frontend on drag/drop affordances, smoother scrolling, and richer live submission updates. |
| `SmokeRunner` | `SmokeRunner` | done | recheck | done | unverified | done | WinUI now exposes an in-app smoke runner modal from board configuration and executes the embedded GoldenHarness suite directly. The interaction model is simple and explicit for a developer surface; performance still has not had a dedicated pass beyond basic execution behavior. |
| `StrategistCard` | `ReactorStrategistCardComponent` | done | recheck | unverified | needs work | done | WinUI again has a dedicated Reactor strategist card surface instead of routing strategist cards through the default shell. Performance still needs work because the surface rebuilds its front-content composition on rerender. |
| `TruthsetExplorePane` | `TruthsetExplorePane` | done | recheck | done | done | done | WinUI Truthset pane now matches the frontend role as a toggleable filtered side rail with phase badge, navigation, renderer reuse, and theme-aware badge treatment. Like GandalfPane, it renders one current card at a time and keeps its own interaction model narrow and explicit, now using the shared floating edge-toggle control for open/close affordance. |

## Frontend top-level UI surfaces with no distinct WinUI counterpart

None at the current top-level control surface.

## WinUI top-level controls/helpers with no distinct frontend counterpart

None at the current top-level control surface.

## WinUI helper modules with frontend helper-module counterparts

| WinUI helper | Frontend counterpart | Functionality | Visual appeal | Interaction / UX | Performance | Reactor migration | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `BoardCanvasLayoutEngine` | `src/lib/boardCanvasLayout.js` | done | n/a | n/a | needs work | n/a | Genuine retained helper seam: WinUI needs a native C# deterministic canvas layout engine at the board-surface layer, but the logic mirrors the frontend layout module rather than introducing unique product behavior. Performance still needs work because the full layout is recomputed for the current graph rather than incrementally updated. |
| `CardPresentationConfig` | `src/lib/cardPresentationConfig.js` | done | n/a | n/a | done | n/a | Genuine retained helper seam: WinUI needs a native C# helper for pane-filter and renderer-rule resolution, mirroring the frontend presentation-config helper in host language. The helper is stateless and narrow enough to treat current performance as acceptable until contrary evidence appears. |

## Reactor-first migration status

Current state:

- All app-authored WinUI surface XAML and theme-dictionary XAML have been migrated to imperative/code-built composition.
- The only remaining XAML file in the WinUI app is `app/DemoBoards.WinUI/App.xaml`, retained as a minimal bootstrap stub because the WinUI toolchain still requires it to generate the application entrypoint.
- Theme packs now load from code-built resource dictionaries through `BoardTheme.CreateThemeDictionary(...)` rather than XAML dictionary files.

| Remaining XAML file | Role | Status | Notes |
| --- | --- | --- | --- |
| `app/DemoBoards.WinUI/App.xaml` | WinUI bootstrap stub | intentional residue | This file is minimized to an empty `Application.Resources` shell and retained only because removing it breaks WinUI app entrypoint generation in this project/toolchain configuration. |

Working total: 1 grandfathered WinUI `.xaml` file remains, and it is toolchain-required bootstrap residue rather than a presentation surface.

## Frontend internal subcomponents folded into WinUI single controls

These do not show up as separate top-level WinUI controls, but they are not missing behavior by default because WinUI currently folds them into a larger control:

- `AddBoardModal` -> folded into `AppConfigModal`
- `BoardMarkdown` -> folded into `ReactorCardViewElementComponent`
- `GandalfChatPane` -> folded into `ReactorChatPaneComponent`
- `MiniChatPane` -> folded into `ReactorChatPaneComponent`
- `PageDetailsSection` -> folded into `AppConfigModal`
- `TemplateIngestModal` -> folded into `AppConfigModal`

## Remaining outer-join differences only

All frontend top-level UI surfaces currently have a distinct WinUI counterpart or are intentionally folded internal subcomponents listed below.

The remaining differences are the explicit outer-join items above:

- native-language helper duplication where WinUI keeps C# equivalents of frontend helper modules at the UI/runtime boundary