using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Assets;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>GlobalModal.jsx</c> — a titled modal dialog card with a close affordance. The host is
/// responsible for placing it on an overlay layer; this renders the dialog surface itself.
/// </summary>
public sealed record GlobalModalProps(string Title, Action OnClose, Element? Children = null);

public sealed class GlobalModal : Component<GlobalModalProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);

        Element header = HStack(12,
            TextBlock(Props.Title).Bold().FontSize(16).Foreground(theme.TextPrimary).Flex(grow: 1),
            Button(Component<SvgIcon, SvgIconProps>(new SvgIconProps(HostIconSources.XLg, 14)), Props.OnClose).SubtleButton().AutomationName($"Close {Props.Title}"));

        return SurfaceUi.DialogSurface(theme, VStack(12, header, Props.Children ?? Empty()));
    }
}
