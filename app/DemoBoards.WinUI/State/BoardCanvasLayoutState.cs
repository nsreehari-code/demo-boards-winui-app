using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DemoBoards_WinUI.State;

public sealed record BoardCanvasPointState(double X, double Y);

public sealed record BoardCanvasViewportState(double X, double Y, double Zoom);

public sealed record BoardCanvasLayoutState(
    IReadOnlyList<string> CardIds,
    IReadOnlyDictionary<string, BoardCanvasPointState> Positions,
    IReadOnlyDictionary<string, double> Widths,
    BoardCanvasViewportState? Viewport,
    JsonElement? InfiniteCanvasBlob)
{
    public static BoardCanvasLayoutState Empty { get; } = new(
        Array.Empty<string>(),
        new Dictionary<string, BoardCanvasPointState>(StringComparer.Ordinal),
        new Dictionary<string, double>(StringComparer.Ordinal),
        null,
        null);
}