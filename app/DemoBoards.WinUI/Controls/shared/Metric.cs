using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>Mirrors <c>Metric.jsx</c> — read-only metric tile (title / value / detail).</summary>
public sealed record MetricProps(string Title = "", string Value = "—", string Detail = "");

public sealed class Metric : Component<MetricProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);

        var children = new System.Collections.Generic.List<Element>();
        if (!string.IsNullOrEmpty(Props.Title))
        {
            children.Add(TextBlock(Props.Title).FontSize(12).Opacity(0.7).Foreground(theme.TextPrimary));
        }

        children.Add(TextBlock(Props.Value).FontSize(22).Bold().Foreground(theme.TextPrimary));

        if (!string.IsNullOrEmpty(Props.Detail))
        {
            children.Add(TextBlock(Props.Detail).FontSize(12).Opacity(0.7).Foreground(theme.TextPrimary));
        }

        return Border(VStack(2, children.ToArray()))
            .Padding(12)
            .Background(theme.CardBackground)
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(10);
    }
}
