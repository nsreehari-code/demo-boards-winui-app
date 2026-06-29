using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
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
    public override Element Render()
    {
        IReadOnlyList<RegistryNode> panes = ResolvePanes(Props.Spec);

        Element centrePane = RenderPane(FindPane(panes, "centre"));
        Element gandalfPane = RenderPane(FindPane(panes, "gandalf"));
        Element truthsetPane = RenderPane(FindPane(panes, "truthset"));

        return Grid(
                new[] { GridSize.Star() },
                new[] { GridSize.Star() },
                centrePane,
                gandalfPane,
                truthsetPane)
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch);
    }

    private Element RenderPane(RegistryNode? pane)
    {
        if (pane is null)
        {
            return Empty();
        }

        return Component<NodeRenderer, NodeRendererProps>(
                new NodeRendererProps(
                    Node: pane,
                    Services: Props.Services,
                    OnSave: Props.OnSave,
                    Status: Props.Status))
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch);
    }

    private static IReadOnlyList<RegistryNode> ResolvePanes(IReadOnlyDictionary<string, object?> spec)
    {
        return spec.TryGetValue("panes", out object? panes)
            && panes is IReadOnlyList<RegistryNode> paneNodes
                ? paneNodes
                : System.Array.Empty<RegistryNode>();
    }

    private static RegistryNode? FindPane(IReadOnlyList<RegistryNode> panes, string paneKind)
    {
        foreach (RegistryNode pane in panes)
        {
            if (pane.Spec?.TryGetValue("paneKind", out object? value) == true
                && value is string kind
                && string.Equals(kind, paneKind, System.StringComparison.Ordinal))
            {
                return pane;
            }
        }

        return null;
    }
}
