using System;
using System.Linq;
using DemoBoards.RuntimeHost;
using Microsoft.UI.Xaml.Controls;

namespace DemoBoards_WinUI.Controls;

public sealed class CardRenderer : UserControl
{
    private readonly Grid Host;

    public CardRenderer()
    {
        Host = new Grid();
        Content = Host;
    }

    public void Render(BoardCard card, IReadOnlyList<RendererRule>? rendererRules = null)
    {
        Host.Children.Clear();
        Host.Children.Add(CreateRenderer(card, rendererRules));
    }

    private static Control CreateRenderer(BoardCard card, IReadOnlyList<RendererRule>? rendererRules)
    {
        string resolvedRenderer = CardPresentationConfig.ResolveCardRenderer(card, rendererRules);

        if (string.Equals(resolvedRenderer, "strategist", StringComparison.OrdinalIgnoreCase)
            || card.ViewKinds.Any(kind => string.Equals(kind, "strategist", StringComparison.OrdinalIgnoreCase)))
        {
            var control = new StrategistCard();
            control.Render(card);
            return control;
        }

        if (string.Equals(resolvedRenderer, "ingest", StringComparison.OrdinalIgnoreCase)
            || card.ViewKinds.Any(kind => string.Equals(kind, "ingest", StringComparison.OrdinalIgnoreCase)))
        {
            var control = new IngestCard();
            control.Render(card);
            return control;
        }

        if (string.Equals(resolvedRenderer, "postbox", StringComparison.OrdinalIgnoreCase)
            || card.ViewKinds.Any(kind => string.Equals(kind, "postbox", StringComparison.OrdinalIgnoreCase)))
        {
            var control = new PostboxCard();
            control.Render(card);
            return control;
        }

        var shell = new CardShell();
        shell.Render(card);
        return shell;
    }
}
