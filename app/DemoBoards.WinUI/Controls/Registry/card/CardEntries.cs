using System.Collections.Generic;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Barrel of card (Tier-2) registry entries — a partial port of <c>card/index.jsx</c>. Each card reads
/// <c>boardId</c>/<c>cardId</c> from <c>spec</c>, so the engine renders it directly with no adapter.
/// <c>meta.bare</c> opts these out of the engine's column framing — a card owns its own outer shell
/// (<see cref="CardChrome"/>). The <c>card:ingest</c> entry depends on the (separately ported) connected
/// chat cluster and is registered with that batch.
/// </summary>
public static class CardEntries
{
    private static readonly RegistryMeta Bare = new(Bare: true);

    public static IReadOnlyList<RegistryEntry> All { get; } = new[]
    {
        new RegistryEntry("card:default", p => Component<CardShell, NodeProps>(p), Meta: Bare),
        new RegistryEntry("card:strategist", p => Component<StrategistCard, NodeProps>(p), Meta: Bare),
        new RegistryEntry("card:postbox", p => Component<PostboxCard, NodeProps>(p), Meta: Bare),
        new RegistryEntry("card:postbox-universal", p => Component<PostboxCard, NodeProps>(p), Meta: Bare),
    };
}
