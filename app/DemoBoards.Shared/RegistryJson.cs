using System.Collections.Generic;
using System.Text.Json;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Bridges the WinUI host's serialized card payloads (<c>RawDefinitionJson</c> / <c>RawRuntimeJson</c>)
/// into the registry engine's loosely-typed object model so binding namespaces can be walked with the
/// same fidelity the frontend has, where <c>card</c>/<c>card_data</c>/<c>computed_values</c>/
/// <c>runtime_state</c> are plain JS objects. Objects become <see cref="Dictionary{TKey,TValue}"/>
/// (ordinal), arrays become <see cref="List{T}"/>, numbers become <c>double</c> (matching JS), and
/// strings/bools/null pass through — exactly the shape <c>BoardData</c>/<c>ResolveBind</c> expect.
/// </summary>
public static class RegistryJson
{
    /// <summary>Parses a JSON string into the loose object model, or returns null on empty/invalid input.</summary>
    public static object? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return ToLoose(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Parses a JSON string into the loose model, falling back to the raw string when it is not JSON.</summary>
    public static object? ParseOrString(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return Parse(value) ?? value;
    }

    /// <summary>Converts a <see cref="JsonElement"/> into the registry's loose object model.</summary>
    public static object? ToLoose(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var map = new Dictionary<string, object?>(System.StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    map[property.Name] = ToLoose(property.Value);
                }

                return map;

            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    list.Add(ToLoose(item));
                }

                return list;

            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                return element.GetDouble();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            default:
                return null;
        }
    }
}
