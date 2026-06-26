using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Controls.Shared;

namespace DemoBoards_WinUI.Controls.Registry;

// Tier-1 cardview adapters — faithful ports of components/registry/cardview/*Kind.jsx. Each is a thin
// Component<NodeProps> that maps the uniform engine contract onto an existing Controls/shared/* presenter,
// preserving the frontend prop names/shapes/defaults exactly. DOM-only props (className/style/idPrefix,
// the <form> submit wrappers) are dropped; the save contract is onSave(value, info{ kind, writeTo, ... }).

/// <summary>Shared helpers for the cardview adapters.</summary>
internal static class CardViewShared
{
    /// <summary>Coerces loose data to the typed object-row list the table/actions presenters expect.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>>? AsRows(object? data)
    {
        IReadOnlyList<object?>? list = BoardData.ToList(data);
        if (list is null)
        {
            return null;
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>(list.Count);
        foreach (object? item in list)
        {
            if (item is IReadOnlyDictionary<string, object?> row)
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    /// <summary>Muted small text used for the "nothing configured" empty states.</summary>
    public static Element Muted(AppTheme theme, string text)
        => TextBlock(text).FontSize(12).Opacity(0.65).Foreground(theme.TextPrimary);

    /// <summary>Builds the loose <c>info</c> bag the save contract carries as its second argument.</summary>
    public static IReadOnlyDictionary<string, object?> Info(params (string Key, object? Value)[] entries)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string key, object? value) in entries)
        {
            map[key] = value;
        }

        return map;
    }

    /// <summary>JS <c>spec.x !== false</c>: true unless the value is exactly boolean false.</summary>
    public static bool NotFalse(IReadOnlyDictionary<string, object?> spec, string key)
        => BoardData.Get(spec, key) as bool? != false;
}

public sealed class TableKind : Component<NodeProps>
{
    public override Element Render()
    {
        IReadOnlyDictionary<string, object?> spec = Props.Spec;
        return Component<Table, TableProps>(new TableProps(
            Data: CardViewShared.AsRows(Props.Data),
            Columns: BoardData.StrList(spec, "columns"),
            MaxRows: BoardData.Int(spec, "maxRows") ?? 200,
            Sortable: CardViewShared.NotFalse(spec, "sortable"),
            Placeholder: BoardData.Str(spec, "placeholder") ?? "No data"));
    }
}

public sealed class ListKind : Component<NodeProps>
{
    public override Element Render()
    {
        IReadOnlyDictionary<string, object?> spec = Props.Spec;
        return Component<List, ListProps>(new ListProps(
            Data: Props.Data,
            Columns: BoardData.StrList(spec, "columns"),
            MaxRows: BoardData.Int(spec, "maxRows"),
            Sortable: CardViewShared.NotFalse(spec, "sortable"),
            Placeholder: BoardData.Str(spec, "placeholder") ?? "Empty",
            TablePlaceholder: BoardData.Str(spec, "placeholder") ?? "No data"));
    }
}

public sealed class ChartKind : Component<NodeProps>
{
    public override Element Render()
        => Component<Chart, ChartProps>(new ChartProps(
            Variant: Props.Variant,
            Data: Props.Data,
            Spec: Props.Spec));
}

public sealed class MetricKind : Component<NodeProps>
{
    public override Element Render()
        => Component<Metric, MetricProps>(new MetricProps(
            Title: Props.Meta.Label ?? "",
            Value: Props.Data != null ? BoardValues.Stringify(Props.Data) : "—"));
}

public sealed class AlertKind : Component<NodeProps>
{
    public override Element Render()
    {
        IReadOnlyDictionary<string, object?> thresholds = BoardData.AsMap(BoardData.Get(Props.Spec, "thresholds"));
        double? value = Props.Data switch
        {
            double d => d,
            int i => i,
            long l => l,
            float f => f,
            _ => null,
        };

        string level = "unknown";
        if (value is double v)
        {
            object? green = BoardData.Get(thresholds, "green");
            object? amber = BoardData.Get(thresholds, "amber");
            if (NodeResolver.JsTruthy(green) && RegistryThreshold.EvalThreshold(v, green))
            {
                level = "green";
            }
            else if (NodeResolver.JsTruthy(amber) && RegistryThreshold.EvalThreshold(v, amber))
            {
                level = "amber";
            }
            else
            {
                level = "red";
            }
        }

        return Component<Alert, AlertProps>(new AlertProps(
            Value: value,
            Label: Props.Meta.Label ?? "",
            Level: level));
    }
}

public sealed class BadgeKind : Component<NodeProps>
{
    public override Element Render()
    {
        IReadOnlyDictionary<string, object?> colorMap = BoardData.AsMap(BoardData.Get(Props.Spec, "colorMap"));
        string value = Props.Data != null ? BoardValues.Stringify(Props.Data) : string.Empty;
        return Component<Badge, BadgeProps>(new BadgeProps(
            Value: value,
            Tone: BoardData.Str(colorMap, value) ?? "secondary"));
    }
}

public sealed class NarrativeKind : Component<NodeProps>
{
    public override Element Render()
        => Component<Narrative, NarrativeProps>(new NarrativeProps(
            Text: Props.Data as string ?? string.Empty,
            EmptyMessage: "No narrative yet. Click refresh to generate."));
}

public sealed class TextKind : Component<NodeProps>
{
    public override Element Render()
    {
        IReadOnlyDictionary<string, object?> spec = Props.Spec;
        var services = Props.Services as NodeServices;
        return Component<Text, TextProps>(new TextProps(
            Value: Props.Data,
            Format: BoardData.Str(spec, "format") ?? "default",
            Style: BoardData.Str(spec, "style") ?? "default",
            HideIfEmpty: BoardData.Bool(spec, "hideIfEmpty"),
            ResolveFileUrl: services?.FileUrlForIndex));
    }
}

public sealed class ActionsKind : Component<NodeProps>
{
    public override Element Render()
    {
        string? id = Props.Meta.Id;
        Action<object?, IReadOnlyDictionary<string, object?>>? onSave = Props.OnSave;
        return Component<Actions, ActionsProps>(new ActionsProps(
            Buttons: CardViewShared.AsRows(BoardData.Get(Props.Spec, "buttons"))
                ?? Array.Empty<IReadOnlyDictionary<string, object?>>(),
            OnAction: buttonId => onSave?.Invoke(
                null,
                CardViewShared.Info(("kind", "actions"), ("buttonId", buttonId), ("elemId", id)))));
    }
}

public sealed class SelectionKind : Component<NodeProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        string? writeTo = Props.WriteTo;
        Action<object?, IReadOnlyDictionary<string, object?>>? onSave = Props.OnSave;

        SingleFieldConfig? field = RegistryFieldConfig.GetSingleFieldConfig(Props.Spec, Props.Data, Props.CurrentValue, writeTo);
        if (field is null)
        {
            return CardViewShared.Muted(theme, "No selection configured");
        }

        return Component<SelectControl, SelectControlProps>(new SelectControlProps(
            Value: field.CurrentValue,
            Options: field.Options,
            AllowEmpty: !field.IsRequired,
            EmptyLabel: "All",
            Required: field.IsRequired,
            AriaLabel: BoardData.Str(field.Prop, "title") ?? field.FieldKey,
            OnChange: next => onSave?.Invoke(
                RegistryFieldConfig.BuildEditorSaveValue(writeTo, field.FieldKey, next),
                CardViewShared.Info(("kind", "selection"), ("writeTo", writeTo)))));
    }
}

public sealed class SearchboxKind : Component<NodeProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        string? writeTo = Props.WriteTo;
        Action<object?, IReadOnlyDictionary<string, object?>>? onSave = Props.OnSave;

        SingleFieldConfig? field = RegistryFieldConfig.GetSingleFieldConfig(Props.Spec, Props.Data, Props.CurrentValue, writeTo);
        string buttonLabel = BoardData.Str(Props.Spec, "actionLabel") ?? "Search";
        if (field is null)
        {
            return CardViewShared.Muted(theme, "No search field configured");
        }

        return Component<Searchbox, SearchboxProps>(new SearchboxProps(
            Prop: field.Prop,
            FieldKey: field.FieldKey,
            Value: field.CurrentValue,
            IsRequired: field.IsRequired,
            ButtonLabel: buttonLabel,
            OnSubmit: next => onSave?.Invoke(
                RegistryFieldConfig.BuildEditorSaveValue(writeTo, field.FieldKey, next),
                CardViewShared.Info(("kind", "searchbox"), ("writeTo", writeTo)))));
    }
}

public sealed class FormKind : Component<NodeProps>
{
    public override Element Render()
    {
        string? writeTo = Props.WriteTo;
        Action<object?, IReadOnlyDictionary<string, object?>>? onSave = Props.OnSave;

        IReadOnlyDictionary<string, object?> baseValues = Props.Data is IReadOnlyDictionary<string, object?> data
            ? new Dictionary<string, object?>(data, StringComparer.Ordinal)
            : Props.CurrentValue is IReadOnlyDictionary<string, object?> current
                ? new Dictionary<string, object?>(current, StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal);

        return Component<Form, FormProps>(new FormProps(
            Spec: Props.Spec,
            BaseValues: baseValues,
            OnSave: values => onSave?.Invoke(values, CardViewShared.Info(("kind", "form"), ("writeTo", writeTo)))));
    }
}

public sealed class NotesKind : Component<NodeProps>
{
    public override Element Render()
    {
        string? writeTo = Props.WriteTo;
        Action<object?, IReadOnlyDictionary<string, object?>>? onSave = Props.OnSave;
        return Component<Notes, NotesProps>(new NotesProps(
            BaseContent: Props.Data as string ?? string.Empty,
            OnSave: content => onSave?.Invoke(content, CardViewShared.Info(("kind", "notes"), ("writeTo", writeTo)))));
    }
}

public sealed class EditableTableKind : Component<NodeProps>
{
    public override Element Render()
    {
        string? writeTo = Props.WriteTo;
        Action<object?, IReadOnlyDictionary<string, object?>>? onSave = Props.OnSave;
        return Component<EditableTable, EditableTableProps>(new EditableTableProps(
            Spec: Props.Spec,
            BaseRows: RegistryFieldConfig.MergeRows(Props.Data),
            OnSave: rows => onSave?.Invoke(rows, CardViewShared.Info(("kind", "editable-table"), ("writeTo", writeTo)))));
    }
}

public sealed class TodoKind : Component<NodeProps>
{
    public override Element Render()
    {
        string? writeTo = Props.WriteTo;
        Action<object?, IReadOnlyDictionary<string, object?>>? onSave = Props.OnSave;
        return Component<Todo, TodoProps>(new TodoProps(
            BaseItems: RegistryFieldConfig.MergeRows(Props.Data),
            OnSave: items => onSave?.Invoke(items, CardViewShared.Info(("kind", "todo"), ("writeTo", writeTo)))));
    }
}

public sealed class MarkdownKind : Component<NodeProps>
{
    public override Element Render()
    {
        string text = Props.Data as string ?? string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            return Empty();
        }

        return Component<BoardMarkdown, BoardMarkdownProps>(new BoardMarkdownProps(Text: text));
    }
}
