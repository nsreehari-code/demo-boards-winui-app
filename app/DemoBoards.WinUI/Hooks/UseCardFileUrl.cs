using System.Collections.Generic;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Hooks;

// =====================================================================================
//  UseCardFileUrl — Reactor port of useCardFileUrl.js
//
//  Resolves the download URL for a card attachment. In the embedded app the runtime's
//  local file face answers synchronously, so the web hook's get-then-ensure async dance
//  collapses to a single GetCardFileUrl lookup: the URL is known immediately once a
//  stored name is present, and is '' for invalid inputs.
// =====================================================================================

public abstract partial class HookComponent<TProps>
{
    /// <summary>Port of <c>useCardFileUrl</c>: the attachment's download URL, or '' when unknown/invalid.</summary>
    protected string UseCardFileUrl(string boardId, string cardId, int index, IReadOnlyDictionary<string, object?>? file)
    {
        EmbeddedBoardClient client = App.Current.BoardClient;
        string storedName = file is null ? string.Empty : BoardData.Str(file, "stored_name") ?? string.Empty;

        return UseMemo<string>(
            () =>
            {
                if (string.IsNullOrEmpty(cardId) || string.IsNullOrEmpty(storedName) || index < 0)
                {
                    return string.Empty;
                }

                return client.GetCardFileUrl(cardId, index, storedName) ?? string.Empty;
            },
            cardId,
            index,
            storedName);
    }
}
