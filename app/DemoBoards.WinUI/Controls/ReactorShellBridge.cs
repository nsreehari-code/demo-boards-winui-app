using System;

namespace DemoBoards_WinUI.Controls;

public sealed record ChatPopoutRequest(string CardId, string? Title);

public static class ReactorShellBridge
{
    public static event Action<ChatPopoutRequest>? ChatPopoutRequested;
    public static event Action? SmokeRunnerRequested;

    public static void RequestChatPopout(string cardId, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return;
        }

        ChatPopoutRequested?.Invoke(new ChatPopoutRequest(cardId, title));
    }

    public static void RequestSmokeRunner()
    {
        SmokeRunnerRequested?.Invoke();
    }
}