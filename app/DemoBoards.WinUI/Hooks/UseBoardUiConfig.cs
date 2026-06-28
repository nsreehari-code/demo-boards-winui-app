using System.Threading.Tasks;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Hooks;

// =====================================================================================
//  UseBoardUiConfig — Reactor port of useBoardUiConfig.js
//
//  Loads the board's resolved `ui` template. The frontend returns a typed plain object;
//  WinUI callers consume the ui config as a raw JSON string throughout (passed to
//  CardPresentationConfig.ResolvePaneFilters, CompileRendererRules, and
//  BoardTheme.ResolveThemePackIdFromUiJson), so returning the raw JSON string is the
//  natural normalised form for this host — it eliminates the need to re-serialize a
//  JsonObject at each call site.
//  Returns null before load or on failure; callers fall back to built-in defaults.
// =====================================================================================

public abstract partial class HookComponent<TProps>
{
    /// <summary>
    /// Port of <c>useBoardUiConfig</c>: the board's resolved ui template as a raw JSON string,
    /// or null before load or on failure. Pass the result directly to
    /// <see cref="CardPresentationConfig"/> helpers or <see cref="BoardTheme"/> resolvers.
    /// </summary>
    protected string? UseBoardUiConfig(string boardId)
    {
        EmbeddedBoardClient client = UseEmbeddedClient();
        var (uiConfig, setUiConfig) = UseState<string?>(null);

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

                    setUiConfig(string.IsNullOrWhiteSpace(state?.RawUiJson) ? null : state!.RawUiJson);
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
        }, boardId ?? string.Empty, client.ServerBaseUri.AbsoluteUri);

        return uiConfig;
    }
}
