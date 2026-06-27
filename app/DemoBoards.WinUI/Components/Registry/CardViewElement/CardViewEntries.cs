using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI.Controls.Shared;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>
/// Barrel of cardview (Tier-1) registry entries — a faithful port of <c>cardview/index.js</c>. Keeps the
/// kind → component → meta wiring in one place so the adapter components stay free of boilerplate. The
/// shared meta presets and the <c>query</c>/<c>searchbox</c> and <c>markdown</c>/<c>markup</c> aliases
/// match the frontend exactly.
/// </summary>
public static class CardViewEntries
{
    // Shared meta presets (engine framing + interaction model).
    private static readonly RegistryMeta ReadOnly = new(ShowLabel: true, IsReadonly: true);   // labelled value display
    private static readonly RegistryMeta Headline = new(ShowLabel: false, IsReadonly: true);  // self-titled tile (label inline)
    private static readonly RegistryMeta Commit = new(ShowLabel: true, Controlled: "commit");  // committed input control

    public static IReadOnlyList<RegistryEntry> All { get; } = new[]
    {
        new RegistryEntry("table", p => Component<TableKind, NodeProps>(p), Meta: ReadOnly),
        new RegistryEntry("list", p => Component<ListKind, NodeProps>(p), Meta: ReadOnly),
        new RegistryEntry("chart", p => Component<ChartKind, NodeProps>(p),
            DefaultVariant: "bar",
            ResolveVariant: (spec, data) => RegistryChart.ResolveChartVariant(spec, data),
            Meta: ReadOnly),
        new RegistryEntry("metric", p => Component<MetricKind, NodeProps>(p), Meta: Headline),
        new RegistryEntry("alert", p => Component<AlertKind, NodeProps>(p), Meta: Headline),
        new RegistryEntry("badge", p => Component<BadgeKind, NodeProps>(p), Meta: ReadOnly),
        new RegistryEntry("narrative", p => Component<NarrativeKind, NodeProps>(p), Meta: ReadOnly),
        new RegistryEntry("text", p => Component<TextKind, NodeProps>(p), Meta: ReadOnly),
        new RegistryEntry("actions", p => Component<ActionsKind, NodeProps>(p),
            Meta: new RegistryMeta(ShowLabel: true, IsReadonly: false)),
        new RegistryEntry("selection", p => Component<SelectionKind, NodeProps>(p), Meta: Commit),
        // `query` and `searchbox` are two explicit kinds sharing one component.
        new RegistryEntry("query", p => Component<SearchboxKind, NodeProps>(p), Meta: Commit),
        new RegistryEntry("searchbox", p => Component<SearchboxKind, NodeProps>(p), Meta: Commit),
        new RegistryEntry("form", p => Component<FormKind, NodeProps>(p), Meta: Commit),
        new RegistryEntry("notes", p => Component<NotesKind, NodeProps>(p), Meta: Commit),
        new RegistryEntry("editable-table", p => Component<EditableTableKind, NodeProps>(p), Meta: Commit),
        new RegistryEntry("todo", p => Component<TodoKind, NodeProps>(p), Meta: Commit),
        // `markdown` and `markup` are two explicit kinds sharing one component.
        new RegistryEntry("markdown", p => Component<MarkdownKind, NodeProps>(p), Meta: ReadOnly),
        new RegistryEntry("markup", p => Component<MarkdownKind, NodeProps>(p), Meta: ReadOnly),
        new RegistryEntry("multi-file-upload", p => Component<MultiFileUploadKind, NodeProps>(p),
            Meta: new RegistryMeta(ShowLabel: true, IsReadonly: false)),
    };
}
