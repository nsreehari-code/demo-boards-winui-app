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

## Documentation

- Treat `docs/sot/*` as maintained source-of-truth documents for parity status, runtime/config behavior, and genuine drift.
- When a change affects parity, drift, or workflow assumptions, update the relevant `docs/sot/*` file in the same slice.
- Keep `docs/sot/*` honest: do not mark work done, parity matched, or drift accepted unless the current implementation actually supports that claim.

## Validation

- For WinUI changes, prefer a focused `dotnet build` in `app/DemoBoards.WinUI` after edits.
- Do not use `dotnet watch` for this repo; it is not a reliable refresh workflow here.
- After WinUI changes, refresh by rebuilding and restarting the app instead of relying on watch or hot reload.
- When generated XAML state is unstable, prefer rebuild and relaunch over widening the implementation.
