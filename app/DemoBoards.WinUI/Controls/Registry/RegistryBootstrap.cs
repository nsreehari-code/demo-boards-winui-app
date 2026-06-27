using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Idempotent registration of all registry tiers — the C# stand-in for the import-time <c>register(...)</c>
/// calls in <c>registry.js</c>. Hosts call <see cref="EnsureRegistered"/> before rendering through
/// <see cref="NodeRenderer"/>. Tiers are added here as each is ported (cardview + card + pane + board).
/// </summary>
public static class RegistryBootstrap
{
    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        ComponentRegistry.RegisterEntries(CardViewEntries.All);
        ComponentRegistry.RegisterEntries(CardEntries.All);
        ComponentRegistry.RegisterEntries(PaneEntries.All);
        ComponentRegistry.RegisterEntries(BoardEntries.All);

        WireCardChromeSeams();
    }

    /// <summary>
    /// Wires the deferred <see cref="CardChromeSeams"/> to the connected chat / inspect clusters. On the web
    /// these are plain imports inside <c>CardChrome.jsx</c>; here the chrome tier stays decoupled and the
    /// delegates are assigned once during registration.
    /// </summary>
    private static void WireCardChromeSeams()
    {
        CardChromeSeams.MiniChatPane = (boardId, cardId, onPopout) => ConnectedChat.Mini(boardId, cardId, onPopout);
        CardChromeSeams.ChatPane = (boardId, cardId) => ConnectedChat.Pane(boardId, cardId);
        CardChromeSeams.InspectCard = (boardId, cardId, title, onClose) =>
            Component<InspectCard, InspectCardProps>(new InspectCardProps(boardId, cardId, title, onClose));
    }
}
