using System.Collections.Generic;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Barrel of pane (Tier-3) registry entries — a port of <c>pane/index.jsx</c>. Panes own their outer shell
/// (rail / aside / surface) and enumerate cards internally, so every entry is <c>meta.bare</c> (the engine
/// adds no framing). The generic <c>pane</c> kind dispatches to <see cref="PaneRenderer"/> (the tier's
/// resolution host), which decides presence — hiding empty rails — before delegating to the concrete
/// <c>pane:&lt;kind&gt;</c>.
/// </summary>
public static class PaneEntries
{
    public static IReadOnlyList<RegistryEntry> All { get; } = new[]
    {
        new RegistryEntry("pane", p => Component<PaneRenderer, NodeProps>(p), Meta: new RegistryMeta(Bare: true)),
        new RegistryEntry("pane:centre", p => Component<CentrePane, NodeProps>(p), Meta: new RegistryMeta(Bare: true)),
        new RegistryEntry("pane:gandalf", p => Component<GandalfPane, NodeProps>(p), Meta: new RegistryMeta(Bare: true)),
        new RegistryEntry("pane:truthset", p => Component<TruthsetExplorePane, NodeProps>(p), Meta: new RegistryMeta(Bare: true)),
    };
}
