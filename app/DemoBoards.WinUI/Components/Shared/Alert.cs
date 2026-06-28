using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>Mirrors <c>Alert.jsx</c> — a threshold alert tile (value / label / severity level).</summary>
public sealed record AlertProps(object? Value = null, string Label = "", string Level = "unknown");

public sealed class Alert : Component<AlertProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        Brush tone = theme.BrushForTone(Props.Level);

        Element dot = Ellipse()
            .Width(10)
            .Height(10)
            .Fill(tone)
            .VAlign(VerticalAlignment.Center);

        var body = new System.Collections.Generic.List<Element>
        {
            TextBlock(Props.Value != null ? BoardShared.Stringify(Props.Value) : "—").Bold().Foreground(theme.TextPrimary),
        };
        if (!string.IsNullOrEmpty(Props.Label))
        {
            body.Add(TextBlock(Props.Label).FontSize(12).Opacity(0.7).Foreground(theme.TextPrimary));
        }

        Element levelPill = Border(TextBlock(Props.Level).FontSize(11).Foreground(theme.TextOnAccent))
            .Background(tone)
            .CornerRadius(8)
            .Set(border =>
            {
                border.Padding = new Thickness(8, 2, 8, 2);
                border.VerticalAlignment = VerticalAlignment.Center;
            });

        return Border(HStack(10, dot, VStack(2, body.ToArray()).Flex(grow: 1), levelPill))
            .Padding(12)
            .Background(theme.CardBackground)
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(10);
    }
}
