using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;

namespace DemoBoards.RuntimeHost;

internal sealed class HostConfigRunner
{
    private readonly string hostConfigRunnerPath;

    public HostConfigRunner(string hostConfigRunnerPath)
    {
        this.hostConfigRunnerPath = hostConfigRunnerPath;
    }

    public JsonObject Invoke(JsonObject payload)
    {
        if (!File.Exists(hostConfigRunnerPath))
        {
            throw new FileNotFoundException("The WinUI host-config runner is missing from the runtime output.", hostConfigRunnerPath);
        }

        ProcessStartInfo startInfo = new("node")
        {
            WorkingDirectory = Path.GetDirectoryName(hostConfigRunnerPath) ?? Directory.GetCurrentDirectory(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(hostConfigRunnerPath);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch the WinUI host-config runner.");
        process.StandardInput.Write(payload.ToJsonString());
        process.StandardInput.Close();

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            string errorText = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorText)
                ? "The WinUI host-config runner failed."
                : errorText.Trim());
        }

        JsonNode? parsed = JsonNode.Parse(string.IsNullOrWhiteSpace(stdout) ? "{}" : stdout);
        if (parsed is not JsonObject jsonObject)
        {
            throw new InvalidOperationException("The WinUI host-config runner returned invalid JSON.");
        }

        if (jsonObject["ok"]?.GetValue<bool?>() == false)
        {
            throw new InvalidOperationException(jsonObject["error"]?.GetValue<string>() ?? "The WinUI host-config runner reported a failure.");
        }

        return jsonObject;
    }

    public void SetupSingleAiWorkspace(string boardId, string hostConfigPath, string setupSingleAiWorkspaceScriptPath)
    {
        ProcessStartInfo startInfo = new("node")
        {
            WorkingDirectory = Path.GetDirectoryName(setupSingleAiWorkspaceScriptPath) ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(setupSingleAiWorkspaceScriptPath);
        startInfo.ArgumentList.Add(boardId);
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(hostConfigPath);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch the board workspace setup helper.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            string errorText = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorText)
                ? $"Workspace setup failed for board '{boardId}'."
                : errorText.Trim());
        }
    }
}
