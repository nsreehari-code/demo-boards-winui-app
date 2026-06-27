namespace DemoBoards.RuntimeHost;

public sealed record RuntimeStatus(
    bool IsRunning,
    string AgentfaceEndpoint,
    string RootDirectory,
    string StorageDirectory,
    string? LastInvocationJson,
    string? BoardSnapshotJson);