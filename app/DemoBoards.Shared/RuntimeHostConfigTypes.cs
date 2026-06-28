using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DemoBoards.RuntimeHost;

public sealed record RuntimeStatus(
    bool IsRunning,
    string AgentfaceEndpoint,
    string RootDirectory,
    string StorageDirectory,
    string? LastInvocationJson,
    string? BoardSnapshotJson);

public sealed record RuntimePaths(
    string RootDir,
    string HostStorageDir,
    string AgentfaceSocketPath)
{
    public static RuntimePaths CreateDefault()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string rootDir = Path.Combine(localAppData, "DemoBoards.WinUI", "runtime");
        return new RuntimePaths(
            rootDir,
            Path.Combine(rootDir, "storage"),
            Path.Combine(rootDir, "agentface.sock"));
    }
}

public sealed record RuntimeHostOptions(
    int AgentfacePort,
    bool RequireFixedAgentfacePort,
    string InitialBoardId,
    int QueuePumpIntervalMs = 250,
    string? HostConfigPath = null,
    string? LocalFsConfigLoaderPath = null,
    string? TemplatesConfigPath = null,
    string? SetupSingleAiWorkspaceScriptPath = null)
{
    public static RuntimeHostOptions Default { get; } = new(43123, false, "winui-board");

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
            250,
            Path.Combine(hostedRuntimeDir, "hosted-board-runtime.localfs.config.json"),
            Path.Combine(hostedRuntimeDir, "localfs-adapter", "load-config.js"),
            Path.Combine(hostedRuntimeDir, "templates-config.json"),
            Path.Combine(hostedRuntimeDir, "scripts", "setup-single-ai-workspace.js"));
    }
}

public static class RuntimeAssetResolver
{
    public const string NsCodeRepoRootEnvVar = "DEMOBOARDS_NSCODE_REPO_ROOT";

    public static string ResolveWorkspaceRootOrThrow(string startPath, string consumerLabel)
    {
        string? resolved = ResolveWorkspaceRoot(startPath);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved;
        }

        throw new InvalidOperationException(
            $"Unable to locate workspace root for {consumerLabel}. "
            + "Expected either a monorepo root containing demo-boards-winui-app, or a standalone demo-boards-winui-app checkout root.");
    }

    public static string? ResolveWorkspaceRoot(string startPath)
    {
        string? standaloneWinUiRoot = null;
        foreach (string seed in new[] { startPath, Directory.GetCurrentDirectory() })
        {
            DirectoryInfo? current = Directory.Exists(seed)
                ? new DirectoryInfo(seed)
                : Path.GetDirectoryName(seed) is string parentPath
                    ? new DirectoryInfo(parentPath)
                    : null;
            while (current is not null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "demo-boards-winui-app")))
                {
                    return current.FullName;
                }

                bool hasWinUiProject = Directory.Exists(Path.Combine(current.FullName, "app", "DemoBoards.WinUI"));
                if (standaloneWinUiRoot is null && hasWinUiProject)
                {
                    standaloneWinUiRoot = current.FullName;
                }

                current = current.Parent;
            }
        }

        return standaloneWinUiRoot;
    }

    public static string ResolveNsCodeRepoRootOrThrow(string startPath, string? configuredPath = null)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            candidates.Add(Path.GetFullPath(configuredPath));
        }

        string? configuredEnv = Environment.GetEnvironmentVariable(NsCodeRepoRootEnvVar);
        if (!string.IsNullOrWhiteSpace(configuredEnv))
        {
            candidates.Add(Path.GetFullPath(configuredEnv));
        }

        string? workspaceRoot = ResolveWorkspaceRoot(startPath);
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            candidates.Add(Path.Combine(workspaceRoot, "demo-boards-ns-code"));
        }

        string? directRepoRoot = FindRepoRootContainingNsCode(startPath);
        if (!string.IsNullOrWhiteSpace(directRepoRoot))
        {
            candidates.Add(directRepoRoot);
        }

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(Path.Combine(candidate, "demo-board", "server", "hosted-board-runtime")))
            {
                return candidate;
            }
        }

        string checkedPaths = string.Join(Environment.NewLine, candidates.Distinct(StringComparer.OrdinalIgnoreCase).Select(path => "- " + path));
        throw new InvalidOperationException(
            "Unable to resolve demo-boards-ns-code repo root. "
            + "Configure backend.nsCodeRepoRoot in winui-app-config.json or set DEMOBOARDS_NSCODE_REPO_ROOT." + Environment.NewLine
            + (checkedPaths.Length == 0 ? "No candidate paths were found." : "Checked paths:" + Environment.NewLine + checkedPaths));
    }

    public static string? ResolvePathTemplate(string? rawPath, string workspaceRoot, string baseDirectory, string nsCodeRepoRoot)
    {
        string normalized = rawPath?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return null;
        }

        string expanded = normalized
            .Replace("${repoRoot}", workspaceRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("${baseDirectory}", baseDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace("${nsCodeRepoRoot}", nsCodeRepoRoot, StringComparison.OrdinalIgnoreCase);
        return Path.GetFullPath(expanded);
    }

    private static string? FindRepoRootContainingNsCode(string startPath)
    {
        foreach (string seed in new[] { startPath, Directory.GetCurrentDirectory() })
        {
            DirectoryInfo? current = Directory.Exists(seed)
                ? new DirectoryInfo(seed)
                : Path.GetDirectoryName(seed) is string parentPath
                    ? new DirectoryInfo(parentPath)
                    : null;
            while (current is not null)
            {
                bool hasHostedRuntime = Directory.Exists(Path.Combine(current.FullName, "demo-board", "server", "hosted-board-runtime"));
                if (hasHostedRuntime)
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        return null;
    }
}