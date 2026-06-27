using System;
using System.IO;

namespace DemoBoards.RuntimeHost;

public sealed record RuntimePaths(
    string RootDir,
    string HostStorageDir,
    string AgentfaceSocketPath)
{
    public static RuntimePaths CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var rootDir = Path.Combine(localAppData, "DemoBoards.WinUI", "runtime");
        return new RuntimePaths(
            rootDir,
            Path.Combine(rootDir, "storage"),
            Path.Combine(rootDir, "agentface.sock"));
    }
}