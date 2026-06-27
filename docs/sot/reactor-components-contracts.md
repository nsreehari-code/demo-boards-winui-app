# Reactor Component Contracts

This file is a WinUI-side implementation note, not the product source of truth. The real source of truth for behavior and architecture is the current frontend implementation. Use this document only as a compact reference for the Reactor-side patterns the WinUI app currently relies on, and recheck it against the frontend before treating any statement here as authoritative.

## `core/TimerButton`

Abstract countdown button. Shows remaining time, fires its action at zero, then resets. If configured clickable, a click fires the same action and resets the countdown. All other button concerns (props, styling, content) are normal button extensibility on top.

Required props: `duration` (seconds), `action`.

## `core/FloatingCircularButton`

`FloatingCircularButton` is the standard shell-level affordance for detached actions that float over content instead of participating in normal layout flow. Its contract is: circular silhouette, anchored corner placement, one primary icon, one explicit active/inactive mode, and consistent hover/focus/disabled treatment. The caller chooses intent and placement; the component owns the floating-button feel.

- Placement is configured by corner, inset, and offset.
- The icon contract supports either SVG or glyph sources behind one visually consistent button surface.
- Active state is first-class and must remain visually distinct from the resting state.
- Size and opacity behavior are component-owned so floating actions feel consistent across the app.

## `core/PanelRail`

`PanelRail` is the standard pattern for a dismissible side rail opened by a floating button. Its contract is not just “show a panel”; it is “pair a floating toggle affordance with a side-attached content rail and make open/close state predictable.” The rail owns placement, backdrop behavior, spacing, and toggle-state semantics. Callers provide content and configuration, not custom rail mechanics.

- The rail is configured by side, width, toggle position, and open/close affordance text.
- Open and closed state keep the toggle button visually synchronized with the rail state.
- The panel surface owns backdrop, border, padding, and optional scroll wrapping so rail-based flows share one shell pattern.
- The intended use is secondary shell surfaces such as settings, inspect-style rails, and developer utilities.

## Scope note

This file should stay at the decision-point level. If a future component needs full parity tracking across props, local state, theming, and lifecycle behavior, use the frontend implementation as the real authority and treat the broader parity docs under `docs/sot` as secondary audit/reference material rather than SoT.