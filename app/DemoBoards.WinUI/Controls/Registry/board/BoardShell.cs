using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Board-tier shell (port of <c>board/BoardShell.jsx</c>). A board owns no chrome of its own — it is
/// purely a container whose children (the panes) are enumerated by the board entry's
/// <see cref="RegistryEntry.ChildResolver"/> and rendered by the engine, then handed back as
/// <see cref="NodeProps.Children"/>. Kept as a component (rather than baked into the entry) so future
/// board kinds can add board-level framing without touching the engine.
/// </summary>
public sealed class BoardShell : Component<NodeProps>
{
    public override Element Render() => Props.Children ?? Empty();
}
