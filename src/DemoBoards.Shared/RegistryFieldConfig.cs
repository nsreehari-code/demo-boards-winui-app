using System.Collections.Generic;
using System.Linq;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// The single-field descriptor returned by <see cref="RegistryFieldConfig.GetSingleFieldConfig"/> — a
/// faithful port of the object <c>getSingleFieldConfig</c> returns in <c>fieldConfig.js</c>. Consumed by
/// the selection / searchbox / query cardview adapters.
/// </summary>
public sealed record SingleFieldConfig(
    string FieldKey,
    IReadOnlyDictionary<string, object?> Prop,
    object? CurrentValue,
    IReadOnlyList<object?> Options,
    bool IsRequired);

/// <summary>
/// Single-field form helpers shared by the selection / searchbox / query / form cardview adapters — a
/// faithful port of <c>registry/lib/fieldConfig.js</c>. (<c>getObjectColumns</c> already lives on
/// <see cref="BoardValues"/>; this class adds the loosely-typed <c>mergeRows</c>, the save-value builder
/// and the single-field descriptor.)
/// </summary>
public static class RegistryFieldConfig
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyMap
        = new Dictionary<string, object?>(System.StringComparer.Ordinal);

    /// <summary>
    /// Port of <c>mergeRows(rows)</c> — coerces loose data to an array and shallow-clones each row
    /// (non-object rows, like the JS <c>{...(row ?? {})}</c>, clone to an empty map).
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> MergeRows(object? data)
    {
        IReadOnlyList<object?>? list = BoardData.ToList(data);
        if (list is null)
        {
            return new List<IReadOnlyDictionary<string, object?>>();
        }

        var result = new List<IReadOnlyDictionary<string, object?>>(list.Count);
        foreach (object? item in list)
        {
            result.Add(item is IReadOnlyDictionary<string, object?> row
                ? new Dictionary<string, object?>(row, System.StringComparer.Ordinal)
                : new Dictionary<string, object?>(System.StringComparer.Ordinal));
        }

        return result;
    }

    /// <summary>
    /// Port of <c>buildEditorSaveValue(writeTo, fieldKey, nextValue)</c> — wraps the value in a
    /// single-field map when writing to <c>card_data</c>, otherwise returns the bare value.
    /// </summary>
    public static object? BuildEditorSaveValue(string? writeTo, string fieldKey, object? nextValue)
    {
        if (writeTo == "card_data")
        {
            return new Dictionary<string, object?>(System.StringComparer.Ordinal) { [fieldKey] = nextValue };
        }

        return nextValue;
    }

    /// <summary>
    /// Port of <c>getSingleFieldConfig(spec, data, currentValue, writeTo)</c> — returns the descriptor
    /// when <c>spec.fields.properties</c> declares exactly one property, else <c>null</c>.
    /// </summary>
    public static SingleFieldConfig? GetSingleFieldConfig(
        IReadOnlyDictionary<string, object?>? spec,
        object? data,
        object? currentValue,
        string? writeTo)
    {
        IReadOnlyDictionary<string, object?> schema = GetMap(spec, "fields");
        IReadOnlyDictionary<string, object?> props = GetMap(schema, "properties");
        if (props.Count != 1)
        {
            return null;
        }

        KeyValuePair<string, object?> entry = props.First();
        string fieldKey = entry.Key;
        IReadOnlyDictionary<string, object?> prop = entry.Value as IReadOnlyDictionary<string, object?> ?? EmptyMap;

        object? fieldValue = writeTo == "card_data"
            ? (currentValue is IReadOnlyDictionary<string, object?> cv && cv.TryGetValue(fieldKey, out object? cvValue) ? cvValue : null)
            : currentValue;

        IReadOnlyList<object?> options = System.Array.Empty<object?>();
        if (prop.TryGetValue("enum", out object? enumValue) && enumValue is IReadOnlyList<object?> enumList)
        {
            options = enumList;
        }
        else if (data is IReadOnlyList<object?> dataList)
        {
            options = dataList;
        }
        else if (data is IReadOnlyDictionary<string, object?> dataMap)
        {
            if (dataMap.TryGetValue(fieldKey, out object? fieldOptions) && fieldOptions is IReadOnlyList<object?> fieldOptionsList)
            {
                options = fieldOptionsList;
            }
            else if (dataMap.TryGetValue("options", out object? optionsValue) && optionsValue is IReadOnlyList<object?> optionsList)
            {
                options = optionsList;
            }
        }

        bool isRequired = schema.TryGetValue("required", out object? requiredValue)
            && requiredValue is IReadOnlyList<object?> requiredList
            && requiredList.Any(item => item as string == fieldKey);

        return new SingleFieldConfig(fieldKey, prop, fieldValue, options, isRequired);
    }

    private static IReadOnlyDictionary<string, object?> GetMap(IReadOnlyDictionary<string, object?>? source, string key)
        => source != null && source.TryGetValue(key, out object? value) && value is IReadOnlyDictionary<string, object?> map
            ? map
            : EmptyMap;
}
