using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;
using DemoBoards.RuntimeHost;

namespace DemoBoards_WinUI.Controls.Shared;

internal sealed record FormValidation(bool Checked, IReadOnlyList<string> Errors);

/// <summary>
/// Mirrors <c>Form.jsx</c> — a self-contained schema form. Owns its own draft state (layered over
/// <c>BaseValues</c>), per-field coercion, rendering of the supported field shapes and the dirty-driven
/// Discard / Save actions.
/// </summary>
/// <remarks>
/// All props are plain data (the frontend's <c>spec</c> / <c>baseValues</c> objects) plus callbacks —
/// the component converts the <c>Spec</c> data into a <see cref="FormSpec"/> internally. <c>Spec.validators</c>
/// are the same declarative JSONata <c>[expr, message]</c> pairs the frontend uses: each is evaluated
/// against <c>{ data: values }</c> via the shared <see cref="JsonataSync"/> bridge (the C# side of
/// <c>compileSync</c>) and must return literal <c>true</c> to pass — run on every edit and on submit.
/// DOM-only props (<c>idPrefix</c>, <c>colSpan</c> grid classes, button class/style overrides) are dropped.
/// </remarks>
public sealed record FormProps(
    IReadOnlyDictionary<string, object?>? Spec = null,
    IReadOnlyDictionary<string, object?>? BaseValues = null,
    Action<IReadOnlyDictionary<string, object?>>? OnSave = null,
    Action? OnCancel = null,
    string CancelLabel = "Discard",
    string? SubmitLabel = null,
    bool Submitting = false,
    bool CanSubmit = true,
    bool AlwaysShowActions = false,
    string Error = "");

public sealed class Form : Component<FormProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        var (overrides, setOverrides) = UseState<IReadOnlyDictionary<string, object?>>(
            new Dictionary<string, object?>(StringComparer.Ordinal));
        var (validation, setValidation) = UseState(new FormValidation(false, Array.Empty<string>()));

        FormSpec spec = FormSpec.FromData(Props.Spec);
        FormFields schema = spec.Fields;
        IReadOnlyDictionary<string, FieldSchema> properties = schema.Properties;
        IReadOnlyList<string> required = schema.Required ?? Array.Empty<string>();
        IReadOnlyList<JsonataValidator> validators = spec.Validators ?? Array.Empty<JsonataValidator>();
        string saveLabel = Props.SubmitLabel ?? spec.SaveLabel ?? "Save";

        object? Effective(string key) =>
            overrides.TryGetValue(key, out object? value) ? value
            : Props.BaseValues != null && Props.BaseValues.TryGetValue(key, out object? baseValue) ? baseValue
            : null;

        IReadOnlyDictionary<string, object?> Merge(IReadOnlyDictionary<string, object?> ov)
        {
            var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (Props.BaseValues != null)
            {
                foreach (KeyValuePair<string, object?> pair in Props.BaseValues)
                {
                    merged[pair.Key] = pair.Value;
                }
            }

            foreach (KeyValuePair<string, object?> pair in ov)
            {
                merged[pair.Key] = pair.Value;
            }

            return merged;
        }

        IReadOnlyList<string> RunValidate(IReadOnlyDictionary<string, object?> values) =>
            validators.Count == 0 ? Array.Empty<string>() : JsonataSync.RunValidators(validators, values);

        void SetField(string key, FieldSchema field, object? raw)
        {
            object? next = field.Type switch
            {
                "boolean" => raw is bool b && b,
                "number" or "integer" => BoardShared.AsNumber(raw) ?? 0d,
                _ => raw,
            };
            var dict = new Dictionary<string, object?>(overrides, StringComparer.Ordinal) { [key] = next };
            setOverrides(dict);

            if (validators.Count > 0)
            {
                setValidation(new FormValidation(true, RunValidate(Merge(dict))));
            }
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

        bool submitDisabled = Props.Submitting || !Props.CanSubmit
            || (validation.Checked && validation.Errors.Count > 0)
            || (Props.AlwaysShowActions && !requiredComplete);

        var fields = properties
            .Select(entry => BuildField(entry.Key, entry.Value, Effective(entry.Key), required.Contains(entry.Key), SetField, theme))
            .ToList();

        bool showActions = Props.AlwaysShowActions || dirty;

        var messages = new List<string>();
        if (!string.IsNullOrWhiteSpace(Props.Error))
        {
            messages.Add(Props.Error);
        }

        messages.AddRange(validation.Errors);

        if (showActions || messages.Count > 0)
        {
            var footer = new List<Element>();
            if (messages.Count > 0)
            {
                var danger = new SolidColorBrush(BoardShared.ToneColor("danger"));
                footer.Add(VStack(2, messages
                    .Select(message => (Element)TextBlock(message).FontSize(11).Foreground(danger))
                    .ToArray()).Flex(grow: 1));
            }

            if (showActions)
            {
                footer.Add(Button(Props.CancelLabel, () =>
                    {
                        setOverrides(new Dictionary<string, object?>(StringComparer.Ordinal));
                        setValidation(new FormValidation(false, Array.Empty<string>()));
                        Props.OnCancel?.Invoke();
                    })
                    .SubtleButton().AutomationName(Props.CancelLabel)
                    .Set(button => button.IsEnabled = !Props.Submitting));
                footer.Add(Button(saveLabel, () =>
                    {
                        IReadOnlyDictionary<string, object?> values = Merge(overrides);
                        IReadOnlyList<string> errors = RunValidate(values);
                        setValidation(new FormValidation(true, errors));
                        if (errors.Count > 0)
                        {
                            return;
                        }

                        Props.OnSave?.Invoke(values);
                    })
                    .AccentButton().AutomationName(saveLabel)
                    .Set(button => button.IsEnabled = !submitDisabled));
            }

            fields.Add(HStack(8, footer.ToArray()));
        }

        return VStack(10, fields.ToArray());
    }

    private static Element BuildField(string key, FieldSchema schema, object? value, bool isRequired, Action<string, FieldSchema, object?> setField, AppTheme theme)
    {
        string title = schema.Title ?? key;
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
        else if (IsDate(schema) || IsTime(schema))
        {
            control = BuildTemporalControl(key, schema, value, setField, disabled);
        }
        else if (schema.Format == "textarea" || schema.Multiline)
        {
            control = TextBox(BoardShared.Stringify(value), text => setField(key, schema, text))
                .AcceptsReturn(true)
                .TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap)
                .AutomationName(title)
                .Set(box => box.IsReadOnly = disabled);
        }
        else if (schema.Type is "number" or "integer")
        {
            double current = BoardShared.AsNumber(value) ?? 0d;
            control = NumberBox(current, number => setField(key, schema, number), schema.Placeholder ?? string.Empty)
                .AutomationName(title)
                .Set(box =>
                {
                    box.IsEnabled = !disabled;
                    if (schema.Minimum is double min)
                    {
                        box.Minimum = min;
                    }

                    if (schema.Maximum is double max)
                    {
                        box.Maximum = max;
                    }

                    box.SmallChange = schema.Type == "integer" ? 1 : 0.1;
                });
        }
        else
        {
            control = TextBox(BoardShared.Stringify(value), text => setField(key, schema, text))
                .PlaceholderText(schema.Placeholder ?? string.Empty)
                .AutomationName(title)
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

    private static bool IsDate(FieldSchema schema) => schema.Format is "date" or "date-time" or "datetime";

    private static bool IsTime(FieldSchema schema) => schema.Format is "time" or "date-time" or "datetime";

    /// <summary>
    /// Renders the temporal field shapes (<c>format: date | time | date-time</c>) as native
    /// pickers, storing the value back as the ISO-ish string the frontend uses
    /// (<c>yyyy-MM-dd</c>, <c>HH:mm</c> or <c>yyyy-MM-ddTHH:mm</c>).
    /// </summary>
    private static Element BuildTemporalControl(string key, FieldSchema schema, object? value, Action<string, FieldSchema, object?> setField, bool disabled)
    {
        bool wantsDate = IsDate(schema);
        bool wantsTime = IsTime(schema);
        string text = BoardShared.Stringify(value);
        DateTimeOffset? parsed = DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset dto)
            ? dto
            : null;

        string Compose(DateTimeOffset? date, TimeSpan? time)
        {
            if (wantsDate && wantsTime)
            {
                DateTimeOffset d = date ?? parsed ?? DateTimeOffset.Now;
                TimeSpan t = time ?? parsed?.TimeOfDay ?? TimeSpan.Zero;
                return new DateTime(d.Year, d.Month, d.Day, 0, 0, 0).Add(t).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
            }

            if (wantsDate)
            {
                return (date ?? parsed)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
            }

            return (time ?? parsed?.TimeOfDay)?.ToString(@"hh\:mm", CultureInfo.InvariantCulture) ?? string.Empty;
        }

        var slots = new List<Element>();
        if (wantsDate)
        {
            slots.Add(CalendarDatePicker(parsed, picked => setField(key, schema, Compose(picked, parsed?.TimeOfDay)))
                .Set(picker => picker.IsEnabled = !disabled));
        }

        if (wantsTime)
        {
            TimeSpan currentTime = parsed?.TimeOfDay ?? TimeSpan.Zero;
            slots.Add(TimePicker(currentTime, picked => setField(key, schema, Compose(parsed, picked)))
                .Set(picker => picker.IsEnabled = !disabled));
        }

        return slots.Count == 1 ? slots[0] : HStack(8, slots.ToArray());
    }

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
