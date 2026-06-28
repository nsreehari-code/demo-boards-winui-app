using System.Text.Json.Nodes;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Hooks;

/// <summary>The managed board config slice that belongs to board configuration rather than visuals.</summary>
public sealed record BoardConfig(
    JsonObject Metadata,
    JsonObject? Board);

public abstract partial class HookComponent<TProps>
{
    /// <summary>
    /// Reads the current board's persisted managed configuration from BoardStore, restricted to
    /// the non-visual slices that callers should treat as board config.
    /// </summary>
    protected BoardConfig? UseBoardConfig(string boardId)
    {
        BoardStore store = UseBoardStoreSubscription(includeUiState: false);
        string normalizedBoardId = boardId?.Trim() ?? string.Empty;
        string currentBoardId = store.GetBoardInfo().BoardId;
        if (normalizedBoardId.Length > 0
            && !string.Equals(normalizedBoardId, currentBoardId, System.StringComparison.Ordinal))
        {
            return null;
        }

        ManagedBoardConfigState? state = store.State.ManagedBoardConfig;
        if (state is null)
        {
            return null;
        }

        return new BoardConfig(
            ParseManagedBoardObjectOrEmpty(state.RawMetadataJson),
            ParseManagedBoardObjectOrNull(state.RawBoardJson));
    }
}