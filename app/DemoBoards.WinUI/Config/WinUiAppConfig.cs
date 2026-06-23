using System;

namespace DemoBoards_WinUI.Config;

public sealed record BoardCanvasLayoutDefaults(
    double DefaultCardWidth,
    double DefaultCardHeight,
    double ColumnGap,
    double RowGap,
    double OriginX,
    double OriginY)
{
    public static BoardCanvasLayoutDefaults Default { get; } = new(360, 240, 420, 280, 40, 40);
}

public sealed record WinUiBoardServerConstants(string AgentOutputChannel, string AgentToolsChannel)
{
    public static WinUiBoardServerConstants Default { get; } = new("agent-output", "agent-tools");
}

public sealed record WinUiHostTemplateCatalog(
    IReadOnlyList<string> AssistantNames,
    IReadOnlyList<string> AiWorkspaceTemplateNames,
    IReadOnlyList<string> UiTemplateNames,
    IReadOnlyList<string> RefsTemplateNames,
    string HostConfigPath,
    string TemplatesConfigPath,
    string SetupSingleAiWorkspaceScriptPath,
    string SampleTemplateCatalogDir,
    string RuntimeBoardsIndexRef,
    string RuntimeBoardsLayoutRef,
    string RawHostSummaryJson);

public sealed record WinUiFrontendAppConfig(BoardCanvasLayoutDefaults CanvasLayout, WinUiBoardServerConstants BoardServerConstants)
{
    public static WinUiFrontendAppConfig Default { get; } = new(BoardCanvasLayoutDefaults.Default, WinUiBoardServerConstants.Default);
}

public sealed record WinUiBackendAppConfig(
    string HostConfigPath,
    string TemplatesConfigPath,
    string LocalFsConfigLoaderPath,
    string HostedPrestartScriptPath,
    string SetupSingleAiWorkspaceScriptPath,
    string AssistantRegistryPath)
{
    public static WinUiBackendAppConfig CreateDefault(string repoRoot)
    {
        return new(
            System.IO.Path.Combine(repoRoot, "demo-boards-ns-code", "demo-board", "server", "hosted-board-runtime", "hosted-board-runtime.localfs.config.json"),
            System.IO.Path.Combine(repoRoot, "demo-boards-ns-code", "demo-board", "server", "hosted-board-runtime", "templates-config.json"),
            System.IO.Path.Combine(repoRoot, "demo-boards-ns-code", "demo-board", "server", "hosted-board-runtime", "localfs-adapter", "load-config.js"),
            System.IO.Path.Combine(repoRoot, "demo-boards-ns-code", "demo-board", "server", "hosted-board-runtime", "scripts", "prestart.js"),
            System.IO.Path.Combine(repoRoot, "demo-boards-ns-code", "demo-board", "server", "hosted-board-runtime", "scripts", "setup-single-ai-workspace.js"),
            System.IO.Path.Combine(repoRoot, "demo-boards-ns-code", "demo-board", "server", "chat-flow", "assistant_registry.json"));
    }
}

public sealed record WinUiAppConfig(
    string AppConfigPath,
    string RepoRoot,
    WinUiFrontendAppConfig Frontend,
    WinUiBackendAppConfig Backend);