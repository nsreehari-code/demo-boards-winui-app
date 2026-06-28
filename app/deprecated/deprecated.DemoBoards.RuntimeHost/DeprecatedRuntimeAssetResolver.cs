using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DemoBoards.RuntimeHost;

internal static class DeprecatedRuntimeAssetResolver
{
    private const string HostInvocationRunnerPathEnvVar = "DEMOBOARDS_HOST_INVOCATION_RUNNER_PATH";

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

        string? workspaceRoot = RuntimeAssetResolver.ResolveWorkspaceRoot(baseDirectory);
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            candidates.Add(Path.Combine(workspaceRoot, "demo-boards-winui-app", "app", "deprecated.DemoBoards.RuntimeHost", "node", "host-invocation-runner.mjs"));
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
            "Unable to resolve host invocation runner path for deprecated runtime host. "
            + "Set DEMOBOARDS_HOST_INVOCATION_RUNNER_PATH if you still need deprecated runtime invocation support."
            + Environment.NewLine
            + (checkedPaths.Length == 0 ? "No candidate paths were found." : "Checked paths:" + Environment.NewLine + checkedPaths));
    }
}