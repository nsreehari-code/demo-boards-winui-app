# Copilot Instructions

## WinUI UI Architecture

- Use only Reactor for WinUI UI work in this repo.
- Do not introduce new XAML binding patterns for app behavior, including `x:Bind`, `Binding`, or MVVM-style binding surfaces.
- Do not treat XAML-first controls or XAML-driven state wiring as the target architecture.
- If a touched UI slice still mixes XAML binding with Reactor-era or imperative code, treat that as migration residue and remove the binding-side behavior where practical.

## UI Change Guidance

- Prefer Reactor-style composition and explicit state flow.
- Keep behavior and styling separated: reusable visual treatment should live in shared theme resources or styles, not duplicated inline.
- Use imperative bridges only where migration seams still exist and avoid expanding those seams.
- For WinUI iconography, prefer packaged SVG assets that match the existing frontend icon style and visual philosophy.
- If WinUI needs a new icon, add a matching SVG asset in the host icon set rather than falling back to unrelated platform glyphs.

## Reactor (Microsoft.UI.Reactor) Patterns and Gotchas

Reactor is React-style declarative UI for WinUI. Import factories with `using static Microsoft.UI.Reactor.Factories;`; build UI by composing `Element`s returned from a `Component<TProps>.Render()`.

Hooks — call them unconditionally at the top of `Render()`; never inside conditionals or loops, because hook order must stay stable across renders:
- `UseState<T>(initial)` returns `(value, setValue)`. The setter takes a VALUE (`Action<T>`), NOT a functional updater. Setters apply on the next render, so the captured `value` is stale within the same handler — when you need the just-set value later in that handler, thread the new value through explicitly (e.g. pass an override argument) rather than re-reading the state variable.
- `UseRef<T>(initial)` returns a ref exposing `.Current`; use it for mutable, non-rendering values (drag/pan state, captured controls, one-time guards).
- `UseEffect(() => cleanupAction, deps...)` runs after render and returns an `Action` for teardown; dependencies are trailing variadic args (e.g. `UseEffect(() => {...}, Props.CardId, a, b)`).
- `UseReducer<T>(initial)` returns `(value, Action<Func<T,T>> update)` — its functional updater receives the live previous value, so use it (not `UseState`) when an update derives from the previous value or several updates run in one event. `UseMemo<T>(() => ..., deps...)` and `UseCallback(action, deps...)` are also available.

Custom hooks (preview.4 specifics):
- The hook primitives are **protected** instance methods on `Component` and **public** instance methods on `Microsoft.UI.Reactor.Core.RenderContext`. This pinned Reactor version has **no public `RenderContext.Current`** accessor (that exists only in newer docs), so a custom hook cannot be a free static function or an extension method.
- Author a reusable custom hook as a **protected method on a thin `Component<TProps>` base class** (e.g. `HookComponent<TProps> : Component<TProps>` in `Hooks/`), calling the inherited protected `UseState`/`UseReducer`/`UseEffect`/`UseMemo`. Components opt in by extending that base. Name custom hooks `Use*` so the Reactor analyzer treats them as hook contexts.
- Hook rules apply everywhere: call hooks unconditionally and in stable order; never inside `if`/`for`/`while`/`switch`/`try` (the slot table throws `HookOrderException`). Put the condition inside the hook body instead.

Composition and styling:
- `VStack(double spacing, params Element[])` / `HStack(double spacing, ...)` — the FIRST argument is spacing, not a child.
- `.Set(control => ...)` runs imperative setup on the materialized control. Do NOT pass an explicit generic type arg to `.Set<T>(...)`; it selects the wrong overload (`ItemsViewElement<T>`). Let type inference resolve the control type.
- `.Margin(0)` is a no-op (zero is treated as unset). Force a zero margin via `.Set(c => c.Margin = new Thickness(0))`.
- `.Canvas(x, y)` positions a child inside a `Canvas`; `.WithBorder(brush, thickness)`, `.CornerRadius(n)`, `.SubtleButton()`, `.AccentButton()` are available helpers.
- Resolve theme brushes with `ReactorMainShellComponent.ResolveBrush("<ThemeResourceKey>")`.
- Theme palette gotcha: `SolidBackgroundFillColor*` brushes are NOT ordered light→dark in light theme (`Tertiary`/`Quarternary` are near-white). The genuinely darker neutral gray is `SolidBackgroundFillColorBaseAltBrush`.

Geometry and drawing:
- Draw vector connectors with `Path2D()` + `.Set(p => p.Data = geometry)`, plus `.Stroke` / `.StrokeThickness` / `.StrokeDashArray` / `.Opacity`.
- A `Geometry`/`PathGeometry` can be attached to only ONE `Path`. Reusing one instance across multiple `Path` elements throws `ArgumentException: Value does not fall within the expected range` at `Path.set_Data` — build a fresh geometry per path.

## Documentation

- The current frontend implementation remains the real product source of truth for UI behavior and architecture.
- Do not create or rely on WinUI-side parity/source-of-truth shadow docs when the frontend implementation already answers the question.

## Validation

- For WinUI changes, prefer a focused `dotnet build` in `app/DemoBoards.WinUI` after edits.
- Do not use `dotnet watch` for this repo; it is not a reliable refresh workflow here.
- After WinUI changes, refresh by rebuilding and restarting the app instead of relying on watch or hot reload.
- When generated XAML state is unstable, prefer rebuild and relaunch over widening the implementation.
