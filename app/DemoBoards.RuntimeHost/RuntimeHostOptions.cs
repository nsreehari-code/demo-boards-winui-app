namespace DemoBoards.RuntimeHost;

public sealed record RuntimeHostOptions(
    int AgentfacePort,
    bool RequireFixedAgentfacePort)
{
    public static RuntimeHostOptions Default { get; } = new(43123, false);
}