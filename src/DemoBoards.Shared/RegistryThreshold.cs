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

        double value = double.TryParse(
            match.Groups[2].Value.Trim(),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out double parsed)
            ? parsed
            : double.NaN;

        return new ThresholdExpr(match.Groups[1].Value, value);
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
