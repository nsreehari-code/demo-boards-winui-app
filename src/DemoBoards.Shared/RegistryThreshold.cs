using System.Globalization;
using System.Text.RegularExpressions;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>A parsed threshold comparison — mirrors <c>{ op, value }</c> from <c>threshold.js</c>.</summary>
public sealed record ThresholdExpr(string Op, double Value);

/// <summary>
/// Threshold parsing/evaluation shared by metric + alert views — a faithful port of
/// <c>registry/lib/threshold.js</c>.
/// </summary>
public static class RegistryThreshold
{
    private static readonly Regex Pattern = new(@"^(<=?|>=?|===?)\s*(.+)$", RegexOptions.Compiled);

    /// <summary>Port of <c>parseThreshold(expr)</c> — returns null when the expression does not match.</summary>
    public static ThresholdExpr? ParseThreshold(object? expr)
    {
        string text = expr is null ? string.Empty : BoardValues.Stringify(expr);
        Match match = Pattern.Match(text);
        if (!match.Success)
        {
            return null;
        }

        // Mirror JS `Number.parseFloat(...)`: read the leading numeric run and ignore any trailing
        // characters (e.g. "80%" -> 80), rather than requiring the whole token to parse.
        return new ThresholdExpr(match.Groups[1].Value, JsParseFloat(match.Groups[2].Value));
    }

    /// <summary>Port of JavaScript's <c>Number.parseFloat</c>: parses the leading numeric prefix, NaN if none.</summary>
    private static double JsParseFloat(string input)
    {
        int n = input.Length;
        int i = 0;
        while (i < n && char.IsWhiteSpace(input[i]))
        {
            i++;
        }

        int start = i;
        if (i < n && (input[i] == '+' || input[i] == '-'))
        {
            i++;
        }

        int digitsBefore = 0;
        while (i < n && input[i] >= '0' && input[i] <= '9')
        {
            i++;
            digitsBefore++;
        }

        int digitsAfter = 0;
        if (i < n && input[i] == '.')
        {
            i++;
            while (i < n && input[i] >= '0' && input[i] <= '9')
            {
                i++;
                digitsAfter++;
            }
        }

        if (digitsBefore == 0 && digitsAfter == 0)
        {
            return double.NaN;
        }

        if (i < n && (input[i] == 'e' || input[i] == 'E'))
        {
            int exponentStart = i;
            i++;
            if (i < n && (input[i] == '+' || input[i] == '-'))
            {
                i++;
            }

            int exponentDigits = 0;
            while (i < n && input[i] >= '0' && input[i] <= '9')
            {
                i++;
                exponentDigits++;
            }

            if (exponentDigits == 0)
            {
                i = exponentStart;
            }
        }

        return double.TryParse(
            input.Substring(start, i - start),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double parsed)
            ? parsed
            : double.NaN;
    }

    /// <summary>Port of <c>evalThreshold(value, expr)</c>.</summary>
    public static bool EvalThreshold(double value, object? expr)
    {
        ThresholdExpr? threshold = ParseThreshold(expr);
        if (threshold is null || double.IsNaN(threshold.Value))
        {
            return false;
        }

        return threshold.Op switch
        {
            "<" => value < threshold.Value,
            "<=" => value <= threshold.Value,
            ">" => value > threshold.Value,
            ">=" => value >= threshold.Value,
            "=" or "==" or "===" => value == threshold.Value,
            _ => false,
        };
    }
}
