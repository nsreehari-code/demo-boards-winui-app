using System;
using System.Collections.Generic;

namespace DemoBoards.RuntimeHost;

internal sealed class RuntimeSnapshotPublisher
{
    public event EventHandler<BoardSnapshot>? SnapshotChanged;

    public string? LastBoardSnapshotJson { get; private set; }

    public BoardSnapshot Publish(object sender, string payload)
    {
        LastBoardSnapshotJson = payload;
        BoardSnapshot snapshot = BoardSnapshot.Parse(payload);
        SnapshotChanged?.Invoke(sender, snapshot);
        return snapshot;
    }

    public void SetSnapshotJson(string? payload)
    {
        LastBoardSnapshotJson = payload;
    }

    public BoardSnapshot ParseSnapshot()
    {
        return BoardSnapshot.Parse(LastBoardSnapshotJson);
    }

    public IReadOnlyDictionary<string, BoardWatchpartyState> ParseWatchparties()
    {
        return BoardSnapshot.ParseWatchparties(LastBoardSnapshotJson);
    }

    public IReadOnlyDictionary<string, BoardWatchpartyState> ParseWatchparties(string agentOutputChannel, string agentToolsChannel)
    {
        return BoardSnapshot.ParseWatchparties(LastBoardSnapshotJson, agentOutputChannel, agentToolsChannel);
    }
}
