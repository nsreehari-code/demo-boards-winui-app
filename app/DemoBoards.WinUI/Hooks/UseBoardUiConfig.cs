using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Hooks;

// =====================================================================================
//  UseBoardUiConfig — Reactor port of useBoardUiConfig.js
//
//  Loads the board's resolved `ui` template (filters etc.). The web hook is server-mode
//  only and reads it from get-board; the embedded app exposes the same record via
//  EmbeddedBoardClient.GetManagedBoardConfigAsync (whose RawUiJson is the board's ui).
//  Returns the ui object (or {} when present), or null on failure / before load.
// =====================================================================================

public abstract partial class HookComponent<TProps>
{
    /// <summary>Port of <c>useBoardUiConfig</c>: the board's resolved ui template, or null.</summary>
    protected JsonObject? UseBoardUiConfig(string boardId)
    {
        EmbeddedBoardClient client = App.Current.BoardClient;
        var (uiConfig, setUiConfig) = UseState<JsonObject?>(null);

        UseEffect(() =>
        {
            if (string.IsNullOrWhiteSpace(boardId))
            {
                return () => { };
            }

            bool cancelled = false;

            async Task Load()
            {
                try
                {
                    ManagedBoardConfigState? state = await client.GetManagedBoardConfigAsync(boardId.Trim());
                    if (cancelled)
                    {
                        return;
                    }

                    setUiConfig(ParseObjectOrEmpty(state?.RawUiJson));
                }
                catch
                {
                    if (cancelled)
                    {
                        return;
                    }

                    setUiConfig(null);
                }
            }

            _ = Load();
            return () => { cancelled = true; };
        }, boardId ?? string.Empty);

        return uiConfig;
    }
}
