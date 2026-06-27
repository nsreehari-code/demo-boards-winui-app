using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Hooks;

// =====================================================================================
//  UseEmbeddedClient — Single source of truth for EmbeddedBoardClient access
//
//  Provides a centralized hook for components to access the EmbeddedBoardClient
//  transport layer. This is the Reactor equivalent of the frontend's ability to
//  directly call client methods via async operations. Replaces direct App.Current.BoardClient
//  access in components with a proper hook.
//
//  Usage:
//    EmbeddedBoardClient client = UseEmbeddedClient();
//    var result = await client.SomeMethodAsync();
//
//  Null-safety is checked at the hook level, ensuring consistent error handling across
//  all components that need client access.
// =====================================================================================

public abstract partial class HookComponent<TProps>
{
    /// <summary>
    /// Provides access to the <see cref="EmbeddedBoardClient"/> for transport operations
    /// (async methods like SaveLayoutAsync, ManageBoardsAsync, GetManagedBoardConfigAsync, etc.).
    /// Replaces direct <c>App.Current.BoardClient</c> access with a proper hook accessor.
    /// </summary>
    /// <returns>The app's <see cref="EmbeddedBoardClient"/> instance.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the app is not initialized or the client is null.
    /// </exception>
    protected EmbeddedBoardClient UseEmbeddedClient()
    {
        EmbeddedBoardClient? client = App.Current?.BoardClient;
        if (client is null)
        {
            throw new InvalidOperationException(
                "App not initialized: EmbeddedBoardClient is null. Ensure App.Current and BoardClient are set during startup.");
        }

        return client;
    }
}
