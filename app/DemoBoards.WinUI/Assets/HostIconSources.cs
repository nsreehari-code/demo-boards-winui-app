using System;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DemoBoards_WinUI.Assets;

public static class HostIconSources
{
    public const string AppConfigForward = "ms-appx:///Assets/Icons/appconfig-forward.svg";
    public const string AppConfigPlus = "ms-appx:///Assets/Icons/appconfig-plus.svg";
    public const string AppConfigSettings = "ms-appx:///Assets/Icons/appconfig-settings.svg";
    public const string CardCloseDetails = "ms-appx:///Assets/Icons/card-close-details.svg";
    public const string CardResizeHandle = "ms-appx:///Assets/Icons/card-resize-handle.svg";
    public const string ChatUserBubble = "ms-appx:///Assets/Icons/chat-user-bubble.svg";
    public const string ChatAssistantBubble = "ms-appx:///Assets/Icons/chat-assistant-bubble.svg";
    public const string ChatWorkingBubble = "ms-appx:///Assets/Icons/chat-working-bubble.svg";
    public const string ChatPopout = "ms-appx:///Assets/Icons/chat-popout.svg";
    public const string ChatAttach = "ms-appx:///Assets/Icons/chat-attach.svg";
    public const string ChatExpandChevron = "ms-appx:///Assets/Icons/chat-expand-chevron.svg";
    public const string InspectDeleteCard = "ms-appx:///Assets/Icons/inspect-delete-card.svg";
    public const string ControlZoomIn = "ms-appx:///Assets/Icons/control-zoom-in.svg";
    public const string ControlZoomOut = "ms-appx:///Assets/Icons/control-zoom-out.svg";
    public const string ControlFitView = "ms-appx:///Assets/Icons/control-fit-view.svg";
    public const string ControlActualSize = "ms-appx:///Assets/Icons/control-actual-size.svg";
    public const string NavChevronUp = "ms-appx:///Assets/Icons/nav-chevron-up.svg";
    public const string NavChevronDown = "ms-appx:///Assets/Icons/nav-chevron-down.svg";
    public const string PostboxClock = "ms-appx:///Assets/Icons/postbox-clock.svg";

    public static SvgImageSource CreateSvg(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        string iconUri = relativePath.StartsWith("ms-appx:///", StringComparison.OrdinalIgnoreCase)
            ? relativePath
            : $"ms-appx:///{relativePath.TrimStart('/')}";
        return new SvgImageSource(new Uri(iconUri));
    }
}