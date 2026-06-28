namespace DemoBoards.RuntimeHost;

public sealed record RuntimeHostOptions(
    int AgentfacePort,
    bool RequireFixedAgentfacePort,
    string InitialBoardId)
{
    public static RuntimeHostOptions Default { get; } = new(43123, false, "winui-board");
}