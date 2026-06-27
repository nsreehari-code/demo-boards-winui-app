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

    // Bootstrap Icons (1.11.3) ports + authored equivalents, mirroring the frontend's bi-* glyphs.
    public const string XLg = "ms-appx:///Assets/Icons/x-lg.svg";
    public const string X = "ms-appx:///Assets/Icons/x.svg";
    public const string Chat = "ms-appx:///Assets/Icons/chat.svg";
    public const string ChatClose = "ms-appx:///Assets/Icons/chat-close.svg";
    public const string Sliders2 = "ms-appx:///Assets/Icons/sliders2.svg";
    public const string ArrowClockwise = "ms-appx:///Assets/Icons/arrow-clockwise.svg";
    public const string ChevronDown = "ms-appx:///Assets/Icons/chevron-down.svg";
    public const string ChevronRight = "ms-appx:///Assets/Icons/chevron-right.svg";
    public const string ChevronLeft = "ms-appx:///Assets/Icons/chevron-left.svg";
    public const string ChevronUp = "ms-appx:///Assets/Icons/chevron-up.svg";
    public const string List = "ms-appx:///Assets/Icons/list.svg";
    public const string Paperclip = "ms-appx:///Assets/Icons/paperclip.svg";
    public const string Send = "ms-appx:///Assets/Icons/send.svg";
    public const string Search = "ms-appx:///Assets/Icons/search.svg";
    public const string CloudArrowUp = "ms-appx:///Assets/Icons/cloud-arrow-up.svg";
    public const string GearFill = "ms-appx:///Assets/Icons/gear-fill.svg";
    public const string Compass = "ms-appx:///Assets/Icons/compass.svg";
    public const string Diagram3 = "ms-appx:///Assets/Icons/diagram-3.svg";
    public const string BoundingBox = "ms-appx:///Assets/Icons/bounding-box.svg";
    public const string ExclamationTriangleFill = "ms-appx:///Assets/Icons/exclamation-triangle-fill.svg";
    public const string Flask = "ms-appx:///Assets/Icons/flask.svg";

    public static SvgImageSource CreateSvg(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        string iconUri = relativePath.StartsWith("ms-appx:///", StringComparison.OrdinalIgnoreCase)
            ? relativePath
            : $"ms-appx:///{relativePath.TrimStart('/')}";
        return new SvgImageSource(new Uri(iconUri));
    }
}