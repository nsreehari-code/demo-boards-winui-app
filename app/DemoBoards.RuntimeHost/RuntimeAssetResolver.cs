using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DemoBoards.RuntimeHost;

public static class RuntimeAssetResolver
{
    public const string NsCodeRepoRootEnvVar = "DEMOBOARDS_NSCODE_REPO_ROOT";
    public const string HostInvocationRunnerPathEnvVar = "DEMOBOARDS_HOST_INVOCATION_RUNNER_PATH";

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
                bool hasRuntimeHostProject = Directory.Exists(Path.Combine(current.FullName, "app", "DemoBoards.RuntimeHost"));
                if (standaloneWinUiRoot is null && hasWinUiProject && hasRuntimeHostProject)
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

    public static string ResolveHostInvocationRunnerPathOrThrow(string baseDirectory, string? configuredPath = null)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            candidates.Add(Path.GetFullPath(configuredPath));
        }

        string? configuredEnv = Environment.GetEnvironmentVariable(HostInvocationRunnerPathEnvVar);
        if (!string.IsNullOrWhiteSpace(configuredEnv))
        {
            candidates.Add(Path.GetFullPath(configuredEnv));
        }

        candidates.Add(Path.Combine(baseDirectory, "node", "host-invocation-runner.mjs"));

        string? workspaceRoot = ResolveWorkspaceRoot(baseDirectory);
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            candidates.Add(Path.Combine(workspaceRoot, "demo-boards-winui-app", "app", "DemoBoards.RuntimeHost", "node", "host-invocation-runner.mjs"));
        }

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string checkedPaths = string.Join(Environment.NewLine, candidates.Distinct(StringComparer.OrdinalIgnoreCase).Select(path => "- " + path));
        throw new FileNotFoundException(
            "Unable to resolve host invocation runner path. "
            + "Configure backend.hostInvocationRunnerPath in winui-app-config.json or set DEMOBOARDS_HOST_INVOCATION_RUNNER_PATH."
            + Environment.NewLine
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
