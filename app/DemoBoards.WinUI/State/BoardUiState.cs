using System.Collections.Generic;

namespace DemoBoards_WinUI.State;

public sealed record BoardUiState(
    string? InspectedCardId,
    IReadOnlySet<string> FlippedCardIds,
    IReadOnlySet<string> MiniChatOpenCardIds)
{
    public static BoardUiState Empty { get; } = new(null, new HashSet<string>(), new HashSet<string>());
}
