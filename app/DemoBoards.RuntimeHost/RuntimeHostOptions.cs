using System.IO;

namespace DemoBoards.RuntimeHost;

public sealed record RuntimeHostOptions(
    int AgentfacePort,
    bool RequireFixedAgentfacePort,
    string InitialBoardId,
    string? HostConfigPath = null,
    string? LocalFsConfigLoaderPath = null,
    string? TemplatesConfigPath = null,
    string? SetupSingleAiWorkspaceScriptPath = null)
{
    public static RuntimeHostOptions Default { get; } = new(43123, false, "winui-board");

    /// <summary>
    /// Builds options whose host-config paths default to the standard hosted-board-runtime
    /// asset layout under <paramref name="nsCodeRepoRoot"/>. Used by headless/host entrypoints
    /// that do not load winui-app-config.json but still need the converged manage-boards seeding
    /// path (resolve board config -> seed admin template cards).
    /// </summary>
    public static RuntimeHostOptions CreateWithNsCodeDefaults(
        int agentfacePort,
        bool requireFixedAgentfacePort,
        string initialBoardId,
        string nsCodeRepoRoot)
    {
        string hostedRuntimeDir = Path.Combine(nsCodeRepoRoot, "demo-board", "server", "hosted-board-runtime");
        return new RuntimeHostOptions(
            agentfacePort,
            requireFixedAgentfacePort,
            initialBoardId,
            Path.Combine(hostedRuntimeDir, "hosted-board-runtime.localfs.config.json"),
            Path.Combine(hostedRuntimeDir, "localfs-adapter", "load-config.js"),
            Path.Combine(hostedRuntimeDir, "templates-config.json"),
            Path.Combine(hostedRuntimeDir, "scripts", "setup-single-ai-workspace.js"));
    }
}