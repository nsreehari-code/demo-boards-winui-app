using System;
using System.Collections.Generic;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Barrel of board (Tier-4) registry entries — a port of <c>board/index.jsx</c>. A board is "just an
/// entry that supplies a <see cref="RegistryEntry.ChildResolver"/>": the resolver enumerates the board's
/// panes (the <c>RegistryNode</c> list on <c>spec.panes</c>); the engine renders each through
/// <c>NodeRenderer</c> and hands the results to <see cref="BoardShell"/> as children. <c>meta.bare</c>
/// because the board owns no chrome. The board's resolution host (data → pane nodes) lives in
/// <see cref="BoardRenderer"/>, outside the registry, so this barrel never imports the engine.
/// </summary>
public static class BoardEntries
{
    public static IReadOnlyList<RegistryEntry> All { get; } = new[]
    {
        new RegistryEntry(
            "board:default",
            p => Component<BoardShell, NodeProps>(p),
            ChildResolver: (spec, _) => spec.TryGetValue("panes", out object? panes)
                && panes is IReadOnlyList<RegistryNode> paneNodes
                    ? paneNodes
                    : Array.Empty<RegistryNode>(),
            Meta: new RegistryMeta(Bare: true)),
    };
}
