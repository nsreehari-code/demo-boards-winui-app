using System;
using System.Collections.Generic;
using System.Linq;
using DemoBoards.RuntimeHost;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls;

public sealed record ReactorCardBackfaceProps(BoardCard Card);

public sealed class ReactorCardBackfaceComponent : Component<ReactorCardBackfaceProps>
{
    public override Element Render()
    {
        var sections = new List<Element>
        {
            TextBlock(Props.Card.Id)
                .FontSize(16)
                .Bold()
                .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords)
        };

        AddInfoSection(sections, "Depends On", Props.Card.Requires, "require");
        AddInfoSection(sections, "Produces", Props.Card.Provides, "provide");
        AddSourceDefinitions(sections, Props.Card.SourceDefinitions);
        AddInfoSection(sections, "Rendered Card Elements", Props.Card.ViewKinds, null);

        sections.Add(TextBlock("Computed values").Bold());
        sections.Add(Props.Card.ComputedValues.Count == 0
            ? TextBlock("No computed values.").Opacity(0.6)
            : BuildFieldList(Props.Card.ComputedValues));

        if (Props.Card.Requires.Count == 0
            && Props.Card.Provides.Count == 0
            && Props.Card.SourceDefinitions.Count == 0
            && Props.Card.ViewKinds.Count == 0)
        {
            sections.Add(TextBlock("No configuration found.").Opacity(0.6));
        }

        return ScrollViewer(VStack(12, sections.ToArray()))
            .Set(scrollViewer => scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto);
    }

    private static void AddInfoSection(List<Element> sections, string title, IReadOnlyList<string> values, string? tokenKind)
    {
        if (values.Count == 0)
        {
            return;
        }

        var chips = new List<Element>
        {
            TextBlock(title).Bold()
        };

        chips.Add(HStack(6, values.Select(value =>
            (Element)Border(TextBlock(value).FontSize(12))
                .Background(CardToneBrushes.CreateToneBrush(!string.IsNullOrWhiteSpace(tokenKind) ? "fresh" : "completed", 0x22))
                .CornerRadius(8)
                .Padding(8, 2, 8, 2)).ToArray()));

        sections.Add(VStack(6, chips.ToArray()));
    }

    private static void AddSourceDefinitions(List<Element> sections, IReadOnlyList<BoardSourceDefinition> sourceDefinitions)
    {
        if (sourceDefinitions.Count == 0)
        {
            return;
        }

        var sourceSections = new List<Element>
        {
            TextBlock("External Data").Bold()
        };

        foreach (BoardSourceDefinition sourceDefinition in sourceDefinitions)
        {
            sourceSections.Add(VStack(4,
                TextBlock(string.IsNullOrWhiteSpace(sourceDefinition.BindTo) ? "unbound" : sourceDefinition.BindTo)
                    .Bold()
                    .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                BuildFieldList(sourceDefinition.DetailFields)));
        }

        sections.Add(VStack(8, sourceSections.ToArray()));
    }

    private static Element BuildFieldList(IReadOnlyList<BoardCardField> fields)
    {
        return VStack(2, fields.Select(field =>
            (Element)HStack(6,
                TextBlock($"{field.Key}:").Bold().Opacity(0.8),
                TextBlock(field.Value).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords))
        ).ToArray());
    }
}