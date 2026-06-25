using System;

namespace DemoBoards_WinUI.Controls;

public static class ReactorShellBridge
{
    public static event Action? SmokeRunnerRequested;

    public static void RequestSmokeRunner()
    {
        SmokeRunnerRequested?.Invoke();
    }
}