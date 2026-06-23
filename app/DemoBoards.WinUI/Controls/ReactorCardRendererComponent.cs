using System;
using DemoBoards.RuntimeHost;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;

namespace DemoBoards_WinUI.Controls;

public sealed record ReactorCardRendererProps(
    BoardCard Card,
    IReadOnlyList<RendererRule>? RendererRules);

public sealed class ReactorCardRendererComponent : Component<ReactorCardRendererProps>
{
    public override Element Render()
    {
        string renderer = CardPresentationConfig.ResolveCardRenderer(Props.Card, Props.RendererRules);

        return renderer switch
        {
            "strategist" => Microsoft.UI.Reactor.Factories.Component<ReactorStrategistCardComponent, ReactorStrategistCardProps>(new ReactorStrategistCardProps(Props.Card)),
            "ingest" => Microsoft.UI.Reactor.Factories.Component<ReactorIngestCardComponent, ReactorIngestCardProps>(new ReactorIngestCardProps(Props.Card)),
            "postbox" => Microsoft.UI.Reactor.Factories.Component<ReactorPostboxCardComponent, ReactorPostboxCardProps>(new ReactorPostboxCardProps(Props.Card)),
            _ => new ReactorCardShellComponent(Props.Card, static () => { }).Render(),
        };
    }
}