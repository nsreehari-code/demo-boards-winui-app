using System.Collections.Generic;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Hooks;

// =====================================================================================
//  UseCardFileUrl — Reactor port of useCardFileUrl.js
//
//  Resolves the download URL for a card attachment. The frontend resolves synchronously
//  first, then falls back to the async ensureCardFileUrl when the URL is not yet known.
//  In the embedded app the runtime's local file face always constructs the URL
//  synchronously from storedName (no server round-trip needed), so the two paths collapse
//  to the same synchronous lookup — matching ensureCardFileUrl's own fallback behavior
//  when the transport has no explicit async ensure method.
// =====================================================================================

public abstract partial class HookComponent<TProps>
{
    /// <summary>Port of <c>useCardFileUrl</c>: the attachment's download URL, or '' when unknown/invalid.</summary>
    protected string UseCardFileUrl(string boardId, string cardId, int index, IReadOnlyDictionary<string, object?>? file)
    {
        EmbeddedBoardClient client = UseEmbeddedClient();
        string storedName = file is null ? string.Empty : BoardData.Str(file, "stored_name") ?? string.Empty;

        // Synchronous initial resolve, matching the frontend's resolveCardFileUrl fast path.
        var (href, setHref) = UseState(ResolveCardFileUrlSync(client, cardId, index, storedName));

        // Re-resolve on input changes. boardId is included to align with the frontend's effect deps
        // even though the embedded URL construction does not embed boardId in the path.
        UseEffect(
            () =>
            {
                setHref(ResolveCardFileUrlSync(client, cardId, index, storedName));
                return () => { };
            },
            boardId,
            cardId,
            index,
            storedName,
            client.ServerBaseUri.AbsoluteUri);

        return href;
    }

    private static string ResolveCardFileUrlSync(EmbeddedBoardClient client, string cardId, int index, string storedName)
    {
        if (string.IsNullOrEmpty(cardId) || string.IsNullOrEmpty(storedName) || index < 0)
        {
            return string.Empty;
        }

        return client.GetCardFileUrl(cardId, index, storedName) ?? string.Empty;
    }
}
