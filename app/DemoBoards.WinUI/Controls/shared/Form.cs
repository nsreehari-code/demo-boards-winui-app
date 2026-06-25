using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>Form.jsx</c> — a self-contained schema form. Owns its own draft state (layered over
/// <c>BaseValues</c>), per-field coercion, rendering of the supported field shapes and the dirty-driven
/// Discard / Save actions.
/// </summary>
/// <remarks>
/// The frontend's JSONata <c>validators</c> are not ported (no C# JSONata runtime here); required-field
/// gating via <c>AlwaysShowActions</c> is preserved.
/// </remarks>
public sealed record FormProps(
    IReadOnlyDictionary<string, FieldSchema> Properties,
    IReadOnlyList<string>? Required = null,
    IReadOnlyDictionary<string, object?>? BaseValues = null,
    string? SubmitLabel = null,
    string CancelLabel = "Discard",
    bool AlwaysShowActions = false,
    Action<IReadOnlyDictionary<string, object?>>? OnSave = null,
    Action? OnCancel = null);

public sealed class Form : Component<FormProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        var (overrides, setOverrides) = UseState<IReadOnlyDictionary<string, object?>>(
            new Dictionary<string, object?>(StringComparer.Ordinal));

        IReadOnlyList<string> required = Props.Required ?? Array.Empty<string>();

        object? Effective(string key) =>
            overrides.TryGetValue(key, out object? value) ? value
            : Props.BaseValues != null && Props.BaseValues.TryGetValue(key, out object? baseValue) ? baseValue
            : null;

        void SetField(string key, FieldSchema schema, object? raw)
        {
            object? next = schema.Type switch
            {
                "boolean" => raw is bool b && b,
                "number" or "integer" => BoardShared.AsNumber(raw) ?? 0d,
                _ => raw,
            };
            var dict = new Dictionary<string, object?>(overrides, StringComparer.Ordinal) { [key] = next };
            setOverrides(dict);
        }

        bool dirty = overrides.Count > 0;
        bool requiredComplete = required.All(key =>
        {
            object? value = Effective(key);
            return value switch
            {
                null => false,
                string s => s.Trim().Length > 0,
                _ => true,
            };
        });

        var fields = Props.Properties
            .Select(entry => BuildField(entry.Key, entry.Value, Effective(entry.Key), SetField, theme))
            .ToList();

        bool showActions = Props.AlwaysShowActions || dirty;
        if (showActions)
        {
            fields.Add(HStack(8,
                Button(Props.CancelLabel, () =>
                    {
                        setOverrides(new Dictionary<string, object?>(StringComparer.Ordinal));
                        Props.OnCancel?.Invoke();
                    })
                    .SubtleButton().AutomationName(Props.CancelLabel),
                Button(Props.SubmitLabel ?? "Save", () =>
                    {
                        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
                        if (Props.BaseValues != null)
                        {
                            foreach (KeyValuePair<string, object?> pair in Props.BaseValues)
                            {
                                merged[pair.Key] = pair.Value;
                            }
                        }

                        foreach (KeyValuePair<string, object?> pair in overrides)
                        {
                            merged[pair.Key] = pair.Value;
                        }

                        Props.OnSave?.Invoke(merged);
                    })
                    .AccentButton().AutomationName(Props.SubmitLabel ?? "Save")
                    .Set(button => button.IsEnabled = !Props.AlwaysShowActions || requiredComplete)));
        }

        return VStack(10, fields.ToArray());
    }

    private static Element BuildField(string key, FieldSchema schema, object? value, Action<string, FieldSchema, object?> setField, AppTheme theme)
    {
        string title = schema.Title ?? key;
        bool isRequired = false;
        bool disabled = schema.ReadOnly || schema.Disabled;
        string? hint = schema.Description ?? schema.Hint;

        Element label = TextBlock(title).FontSize(12).Opacity(0.75).Foreground(theme.TextPrimary);

        Element control;
        if (schema.Type == "boolean")
        {
            bool current = value is bool b && b;
            control = ToggleSwitch(current, on => setField(key, schema, on), title, "On", "Off");
        }
        else if (IsMultiSelect(schema))
        {
            IReadOnlyList<SelectOption> options = BuildMultiOptions(schema);
            var selected = (value as IEnumerable<object?>)?.Select(BoardShared.Stringify).ToHashSet(StringComparer.Ordinal)
                ?? new HashSet<string>(StringComparer.Ordinal);
            control = VStack(2, options
                .Select(option => (Element)CheckBox(
                    selected.Contains(option.Value),
                    on =>
                    {
                        var next = new HashSet<string>(selected, StringComparer.Ordinal);
                        if (on)
                        {
                            next.Add(option.Value);
                        }
                        else
                        {
                            next.Remove(option.Value);
                        }

                        setField(key, schema, next.ToList());
                    },
                    option.Label))
                .ToArray());
        }
        else if (IsSelect(schema))
        {
            IReadOnlyList<object?> options = BuildFieldOptions(schema) ?? Array.Empty<object?>();
            control = Component<SelectControl, SelectControlProps>(new SelectControlProps(
                Value: value,
                Options: options,
                GetOptions: schema.GetOptions,
                AllowEmpty: !isRequired,
                EmptyLabel: schema.Placeholder ?? string.Empty,
                Required: isRequired,
                Disabled: disabled,
                AriaLabel: title,
                Title: title,
                OnChange: next => setField(key, schema, next)));
        }
        else if (schema.Format == "textarea" || schema.Multiline)
        {
            control = TextBox(BoardShared.Stringify(value), text => setField(key, schema, text))
                .AcceptsReturn(true)
                .TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap)
                .Set(box => box.IsReadOnly = disabled);
        }
        else if (schema.Type is "number" or "integer")
        {
            double current = BoardShared.AsNumber(value) ?? 0d;
            control = NumberBox(current, number => setField(key, schema, number), schema.Placeholder ?? string.Empty)
                .Set(box => box.IsEnabled = !disabled);
        }
        else
        {
            control = TextBox(BoardShared.Stringify(value), text => setField(key, schema, text))
                .PlaceholderText(schema.Placeholder ?? string.Empty)
                .Set(box => box.IsReadOnly = disabled);
        }

        var children = new List<Element> { label, control };
        if (!string.IsNullOrWhiteSpace(hint))
        {
            children.Add(TextBlock(hint).FontSize(11).Opacity(0.6).Foreground(theme.TextPrimary));
        }

        return VStack(2, children.ToArray());
    }

    private static bool IsSelect(FieldSchema schema) => schema.GetOptions != null || BuildFieldOptions(schema) != null;

    private static bool IsMultiSelect(FieldSchema schema) =>
        schema.Type == "array"
        && ((schema.Items?.Enum is { Count: > 0 }) || (schema.Items?.OneOf is { Count: > 0 }) || (schema.Options is { Count: > 0 }));

    private static IReadOnlyList<object?>? BuildFieldOptions(FieldSchema schema)
    {
        if (schema.OneOf is { Count: > 0 })
        {
            return schema.OneOf
                .Select(entry => (object?)new SelectOption(BoardShared.Stringify(entry.Const), entry.Title ?? BoardShared.Stringify(entry.Const)))
                .ToList();
        }

        if (schema.Options is { Count: > 0 })
        {
            return schema.Options;
        }

        if (schema.Enum is { Count: > 0 })
        {
            if (schema.EnumNames is { Count: > 0 } && schema.EnumNames.Count == schema.Enum.Count)
            {
                return schema.Enum
                    .Select((value, index) => (object?)new SelectOption(BoardShared.Stringify(value), schema.EnumNames[index]))
                    .ToList();
            }

            return schema.Enum;
        }

        return null;
    }

    private static IReadOnlyList<SelectOption> BuildMultiOptions(FieldSchema schema)
    {
        FieldSchema items = schema.Items ?? new FieldSchema();
        if (items.OneOf is { Count: > 0 })
        {
            return items.OneOf
                .Select(entry => new SelectOption(BoardShared.Stringify(entry.Const), entry.Title ?? BoardShared.Stringify(entry.Const)))
                .ToList();
        }

        if (schema.Options is { Count: > 0 })
        {
            return schema.Options.Select(SelectOption.Normalize).ToList();
        }

        if (items.Enum is { Count: > 0 })
        {
            if (items.EnumNames is { Count: > 0 } && items.EnumNames.Count == items.Enum.Count)
            {
                return items.Enum.Select((value, index) => new SelectOption(BoardShared.Stringify(value), items.EnumNames[index])).ToList();
            }

            return items.Enum.Select(SelectOption.Normalize).ToList();
        }

        return Array.Empty<SelectOption>();
    }
}
