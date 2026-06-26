using System;
using System.Collections.Generic;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// A single select option — mirrors the frontend's <c>normalizeOption</c> output (<c>{ value, label }</c>).
/// Inputs may be a scalar or an object exposing <c>value/id/label/title</c>.
/// </summary>
public sealed record SelectOption(string Value, string Label)
{
    /// <summary>Port of <c>normalizeOption(option)</c> from <c>Select.jsx</c>.</summary>
    public static SelectOption Normalize(object? option)
    {
        if (option is SelectOption ready)
        {
            return ready;
        }

        if (option is IReadOnlyDictionary<string, object?> map)
        {
            string value = BoardValues.Stringify(
                Pick(map, "value") ?? Pick(map, "id") ?? Pick(map, "label") ?? string.Empty);
            string label = BoardValues.Stringify(
                Pick(map, "label") ?? Pick(map, "title") ?? Pick(map, "value") ?? Pick(map, "id") ?? string.Empty);
            return new SelectOption(value, label);
        }

        string scalar = BoardValues.Stringify(option);
        return new SelectOption(scalar, scalar);
    }

    private static object? Pick(IReadOnlyDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out object? value) ? value : null;
}

/// <summary>A labelled enum entry — mirrors JSON Schema <c>oneOf: [{ const, title }]</c>.</summary>
public sealed record OneOfEntry(object? Const, string? Title = null);

/// <summary>
/// A JSON-Schema-ish field descriptor shared by <c>Form</c>, <c>EditableTable</c>, <c>Searchbox</c> and
/// <c>Select</c>. Mirrors the <c>spec.*.properties.&lt;key&gt;</c> shapes those frontend components read.
/// </summary>
public sealed record FieldSchema(
    string? Type = null,
    string? Format = null,
    string? Title = null,
    string? Description = null,
    string? Hint = null,
    string? Placeholder = null,
    IReadOnlyList<object?>? Enum = null,
    IReadOnlyList<string>? EnumNames = null,
    IReadOnlyList<OneOfEntry>? OneOf = null,
    IReadOnlyList<object?>? Options = null,
    FieldSchema? Items = null,
    double? Minimum = null,
    double? Maximum = null,
    int? MinLength = null,
    int? MaxLength = null,
    string? Pattern = null,
    bool ReadOnly = false,
    bool Disabled = false,
    int? ColSpan = null,
    int? Rows = null,
    bool Multiline = false,
    Func<object?>? GetOptions = null)
{
    /// <summary>
    /// Parses a plain data field descriptor (the JSON-schema-ish <c>properties.&lt;key&gt;</c> object the
    /// frontend passes) into a typed <see cref="FieldSchema"/>. Accepts a dictionary, an already-typed
    /// <see cref="FieldSchema"/>, or anything else (treated as an empty schema). A <c>getOptions</c>
    /// callback (<see cref="Func{Object}"/>) is carried through unchanged.
    /// </summary>
    public static FieldSchema FromData(object? data)
    {
        if (data is FieldSchema ready)
        {
            return ready;
        }

        if (data is not IReadOnlyDictionary<string, object?> map)
        {
            return new FieldSchema();
        }

        return new FieldSchema(
            Type: BoardData.Str(map, "type"),
            Format: BoardData.Str(map, "format"),
            Title: BoardData.Str(map, "title"),
            Description: BoardData.Str(map, "description"),
            Hint: BoardData.Str(map, "hint"),
            Placeholder: BoardData.Str(map, "placeholder"),
            Enum: BoardData.List(map, "enum"),
            EnumNames: BoardData.StrList(map, "enumNames"),
            OneOf: ParseOneOf(BoardData.List(map, "oneOf")),
            Options: BoardData.List(map, "options"),
            Items: map.ContainsKey("items") ? FromData(map["items"]) : null,
            Minimum: BoardData.Dbl(map, "minimum"),
            Maximum: BoardData.Dbl(map, "maximum"),
            MinLength: BoardData.Int(map, "minLength"),
            MaxLength: BoardData.Int(map, "maxLength"),
            Pattern: BoardData.Str(map, "pattern"),
            ReadOnly: BoardData.Bool(map, "readOnly"),
            Disabled: BoardData.Bool(map, "disabled"),
            ColSpan: BoardData.Int(map, "colSpan"),
            Rows: BoardData.Int(map, "rows"),
            Multiline: BoardData.Bool(map, "multiline"),
            GetOptions: BoardData.Func(map, "getOptions"));
    }

    private static IReadOnlyList<OneOfEntry>? ParseOneOf(IReadOnlyList<object?>? list)
    {
        if (list is null)
        {
            return null;
        }

        var entries = new List<OneOfEntry>(list.Count);
        foreach (object? item in list)
        {
            entries.Add(item switch
            {
                OneOfEntry ready => ready,
                IReadOnlyDictionary<string, object?> m => new OneOfEntry(BoardData.Get(m, "const"), BoardData.Str(m, "title")),
                _ => new OneOfEntry(item),
            });
        }

        return entries;
    }
}
