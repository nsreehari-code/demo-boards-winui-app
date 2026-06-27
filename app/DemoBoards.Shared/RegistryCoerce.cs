using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Equality + the registry-level coercion used when data falls through to the fallback (<c>text</c>) kind
/// — a faithful port of <c>registry/lib/coerce.js</c>. Both helpers lean on a JSON serializer that mirrors
/// <c>JSON.stringify</c> over the loosely-typed registry data model (objects, arrays, scalars).
/// </summary>
public static class RegistryCoerce
{
    /// <summary>Port of <c>deepEqual(left, right)</c> — structural equality via canonical JSON.</summary>
    public static bool DeepEqual(object? left, object? right) =>
        string.Equals(Stringify(left, 0), Stringify(right, 0), StringComparison.Ordinal);

    /// <summary>
    /// Port of <c>coerceUnknownData(data)</c>: strings pass through, null becomes empty, and everything
    /// else is pretty-printed JSON (two-space indent, matching <c>JSON.stringify(data, null, 2)</c>).
    /// </summary>
    public static string CoerceUnknownData(object? data)
    {
        if (data is string s)
        {
            return s;
        }

        return data is null ? string.Empty : Stringify(data, 2);
    }

    /// <summary>
    /// Serializes a registry value to JSON. <paramref name="indent"/> of 0 produces the compact form used
    /// by <see cref="DeepEqual"/>; a positive value produces pretty output like <c>JSON.stringify(x, null, n)</c>.
    /// </summary>
    public static string Stringify(object? value, int indent)
    {
        var builder = new StringBuilder();
        Write(builder, value, indent, 0);
        return builder.ToString();
    }

    private static void Write(StringBuilder builder, object? value, int indent, int depth)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                return;
            case bool b:
                builder.Append(b ? "true" : "false");
                return;
            case string s:
                WriteString(builder, s);
                return;
            case double or float or int or long or short or byte or decimal:
                WriteNumber(builder, value);
                return;
            case IReadOnlyDictionary<string, object?> map:
                WriteObject(builder, map, indent, depth);
                return;
            case IEnumerable sequence:
                WriteArray(builder, sequence, indent, depth);
                return;
            default:
                WriteString(builder, value.ToString() ?? string.Empty);
                return;
        }
    }

    private static void WriteObject(StringBuilder builder, IReadOnlyDictionary<string, object?> map, int indent, int depth)
    {
        if (map.Count == 0)
        {
            builder.Append("{}");
            return;
        }

        builder.Append('{');
        string childPad = indent > 0 ? new string(' ', indent * (depth + 1)) : string.Empty;
        string closePad = indent > 0 ? new string(' ', indent * depth) : string.Empty;
        bool first = true;
        foreach (KeyValuePair<string, object?> entry in map)
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            if (indent > 0)
            {
                builder.Append('\n').Append(childPad);
            }

            WriteString(builder, entry.Key);
            builder.Append(indent > 0 ? ": " : ":");
            Write(builder, entry.Value, indent, depth + 1);
        }

        if (indent > 0)
        {
            builder.Append('\n').Append(closePad);
        }

        builder.Append('}');
    }

    private static void WriteArray(StringBuilder builder, IEnumerable sequence, int indent, int depth)
    {
        var items = new List<object?>();
        foreach (object? item in sequence)
        {
            items.Add(item);
        }

        if (items.Count == 0)
        {
            builder.Append("[]");
            return;
        }

        builder.Append('[');
        string childPad = indent > 0 ? new string(' ', indent * (depth + 1)) : string.Empty;
        string closePad = indent > 0 ? new string(' ', indent * depth) : string.Empty;
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            if (indent > 0)
            {
                builder.Append('\n').Append(childPad);
            }

            Write(builder, items[i], indent, depth + 1);
        }

        if (indent > 0)
        {
            builder.Append('\n').Append(closePad);
        }

        builder.Append(']');
    }

    private static void WriteNumber(StringBuilder builder, object value)
    {
        double number = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            short s => s,
            byte b => b,
            decimal m => (double)m,
            _ => 0,
        };

        if (double.IsNaN(number) || double.IsInfinity(number))
        {
            // JSON.stringify renders non-finite numbers as null.
            builder.Append("null");
            return;
        }

        builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void WriteString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (c < ' ')
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}
