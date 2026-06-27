using System;
using System.Collections.Generic;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>A single JSONata validator — mirrors a frontend <c>[expr, message]</c> pair.</summary>
public sealed record JsonataValidator(string Expr, string Message);

/// <summary>The schema half of a <see cref="FormSpec"/> — mirrors <c>spec.fields</c>.</summary>
public sealed record FormFields(
    IReadOnlyDictionary<string, FieldSchema> Properties,
    IReadOnlyList<string>? Required = null);

/// <summary>
/// The typed form spec the component works with internally. Callers do <b>not</b> build this — they pass
/// the plain <c>spec</c> data object and the component converts it via <see cref="FromData"/>, mirroring
/// how the frontend reads its plain <c>spec</c> object.
/// </summary>
public sealed record FormSpec(
    FormFields Fields,
    string? SaveLabel = null,
    IReadOnlyList<JsonataValidator>? Validators = null)
{
    /// <summary>
    /// Parses the frontend-shaped <c>spec</c> data object —
    /// <c>{ fields: { properties, required }, saveLabel?, validators? }</c> — into a typed
    /// <see cref="FormSpec"/>. <c>validators</c> are the same <c>[expr, message]</c> pairs (arrays or
    /// <c>{ expr, message }</c> objects) the frontend authors.
    /// </summary>
    public static FormSpec FromData(IReadOnlyDictionary<string, object?>? data)
    {
        IReadOnlyDictionary<string, object?> map = data ?? BoardData.Empty;
        IReadOnlyDictionary<string, object?> fields = BoardData.AsMap(BoardData.Get(map, "fields"));

        var properties = new Dictionary<string, FieldSchema>(StringComparer.Ordinal);
        if (BoardData.Get(fields, "properties") is IReadOnlyDictionary<string, object?> propMap)
        {
            foreach (KeyValuePair<string, object?> entry in propMap)
            {
                properties[entry.Key] = FieldSchema.FromData(entry.Value);
            }
        }

        return new FormSpec(
            new FormFields(properties, BoardData.StrList(fields, "required")),
            BoardData.Str(map, "saveLabel"),
            ParseValidators(BoardData.List(map, "validators")));
    }

    private static IReadOnlyList<JsonataValidator>? ParseValidators(IReadOnlyList<object?>? list)
    {
        if (list is null || list.Count == 0)
        {
            return null;
        }

        var result = new List<JsonataValidator>();
        foreach (object? entry in list)
        {
            switch (entry)
            {
                case JsonataValidator ready:
                    result.Add(ready);
                    break;
                case IReadOnlyDictionary<string, object?> m when !string.IsNullOrWhiteSpace(BoardData.Str(m, "expr")):
                    result.Add(new JsonataValidator(BoardData.Str(m, "expr")!, BoardData.Str(m, "message") ?? "Invalid value"));
                    break;
                default:
                    IReadOnlyList<object?>? pair = BoardData.ToList(entry);
                    if (pair is { Count: > 0 } && pair[0] is { } exprObj)
                    {
                        string expr = BoardValues.Stringify(exprObj);
                        if (!string.IsNullOrWhiteSpace(expr))
                        {
                            string message = pair.Count > 1 && pair[1] is { } msg ? BoardValues.Stringify(msg) : "Invalid value";
                            result.Add(new JsonataValidator(expr, message));
                        }
                    }

                    break;
            }
        }

        return result;
    }
}

/// <summary>The schema half of the editable-table spec — the per-column <see cref="FieldSchema"/> map.</summary>
public sealed record EditableTableSchema(
    IReadOnlyDictionary<string, FieldSchema>? Properties = null);

/// <summary>
/// The typed table spec the component works with internally. Callers do <b>not</b> build this — they
/// pass the plain <c>spec</c> data object and the component converts it via <see cref="FromData"/>,
/// mirroring the frontend's plain <c>spec</c> object
/// (<c>{ schema: { properties }, columns?, addRow?, deleteRow?, placeholder? }</c>).
/// </summary>
public sealed record EditableTableSpec(
    EditableTableSchema? Schema = null,
    IReadOnlyList<string>? Columns = null,
    bool AllowAddRow = true,
    bool AllowDeleteRow = true,
    string Placeholder = "No data")
{
    /// <summary>Parses the frontend-shaped <c>spec</c> data object into a typed <see cref="EditableTableSpec"/>.</summary>
    public static EditableTableSpec FromData(IReadOnlyDictionary<string, object?>? data)
    {
        IReadOnlyDictionary<string, object?> map = data ?? BoardData.Empty;

        var properties = new Dictionary<string, FieldSchema>(StringComparer.Ordinal);
        if (BoardData.Get(BoardData.AsMap(BoardData.Get(map, "schema")), "properties") is IReadOnlyDictionary<string, object?> propMap)
        {
            foreach (KeyValuePair<string, object?> entry in propMap)
            {
                properties[entry.Key] = FieldSchema.FromData(entry.Value);
            }
        }

        return new EditableTableSpec(
            new EditableTableSchema(properties),
            BoardData.StrList(map, "columns"),
            BoardData.BoolOr(map, "addRow", true),
            BoardData.BoolOr(map, "deleteRow", true),
            BoardData.Str(map, "placeholder") ?? "No data");
    }
}

/// <summary>
/// One button in an <c>Actions</c> row — the typed conversion target the component builds internally
/// from each plain <c>{ id, label?, style?, size?, disabled? }</c> data object.
/// </summary>
public sealed record ActionButton(string Id, string? Label = null, string? Style = null, string? Size = null, bool Disabled = false)
{
    /// <summary>Parses a frontend-shaped button data object into a typed <see cref="ActionButton"/>.</summary>
    public static ActionButton FromData(IReadOnlyDictionary<string, object?>? data)
    {
        IReadOnlyDictionary<string, object?> map = data ?? BoardData.Empty;
        return new ActionButton(
            BoardData.Str(map, "id") ?? string.Empty,
            BoardData.Str(map, "label"),
            BoardData.Str(map, "style"),
            BoardData.Str(map, "size"),
            BoardData.Bool(map, "disabled"));
    }
}

/// <summary>
/// One todo entry — the typed conversion target the component builds internally from each plain
/// <c>{ text, done }</c> data object.
/// </summary>
public sealed record TodoItem(string Text, bool Done = false)
{
    /// <summary>Parses a frontend-shaped item data object into a typed <see cref="TodoItem"/>.</summary>
    public static TodoItem FromData(IReadOnlyDictionary<string, object?>? data)
    {
        IReadOnlyDictionary<string, object?> map = data ?? BoardData.Empty;
        return new TodoItem(BoardData.Str(map, "text") ?? string.Empty, BoardData.Bool(map, "done"));
    }

    /// <summary>Projects this item back to the plain <c>{ text, done }</c> data object the callback emits.</summary>
    public IReadOnlyDictionary<string, object?> ToData() =>
        new Dictionary<string, object?> { ["text"] = Text, ["done"] = Done };
}

/// <summary>
/// A staged/stored file descriptor for the <c>Text</c> component's <c>file-links</c> format — the typed
/// conversion target built internally from each plain <c>{ name, stored_name, size }</c> data object.
/// </summary>
public sealed record TextFile(string? Name = null, string? StoredName = null, double? Size = null)
{
    /// <summary>Parses a frontend-shaped file data object into a typed <see cref="TextFile"/>.</summary>
    public static TextFile FromData(object? data)
    {
        if (data is TextFile ready)
        {
            return ready;
        }

        if (data is not IReadOnlyDictionary<string, object?> map)
        {
            return new TextFile();
        }

        return new TextFile(
            BoardData.Str(map, "name"),
            BoardData.Str(map, "stored_name"),
            BoardData.Dbl(map, "size"));
    }
}
