using System;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DemoBoards_WinUI.Assets;

public static class HostIconSources
{
    public const string AppConfigForward = "Assets/Icons/appconfig-forward.svg";
    public const string AppConfigPlus = "Assets/Icons/appconfig-plus.svg";
    public const string CardCloseDetails = "Assets/Icons/card-close-details.svg";
    public const string CardResizeHandle = "Assets/Icons/card-resize-handle.svg";
    public const string ChatUserBubble = "Assets/Icons/chat-user-bubble.svg";
    public const string ChatAssistantBubble = "Assets/Icons/chat-assistant-bubble.svg";
    public const string ChatWorkingBubble = "Assets/Icons/chat-working-bubble.svg";
    public const string ChatPopout = "Assets/Icons/chat-popout.svg";
    public const string ChatAttach = "Assets/Icons/chat-attach.svg";
    public const string ChatExpandChevron = "Assets/Icons/chat-expand-chevron.svg";
    public const string InspectDeleteCard = "Assets/Icons/inspect-delete-card.svg";

    public static SvgImageSource CreateSvg(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return new SvgImageSource(new Uri($"ms-appx:///{relativePath}"));
    }
}