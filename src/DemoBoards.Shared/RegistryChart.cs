using System.Collections.Generic;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Chart variant detection — a faithful port of the variant half of <c>registry/lib/chart.js</c>
/// (<c>detectChartType</c> / <c>resolveChartVariant</c>). The palette and data normalisation already live
/// on <see cref="BoardShared"/> / <c>Chart</c>; this class supplies the chart entry's <c>resolveVariant</c>.
/// </summary>
public static class RegistryChart
{
    /// <summary>
    /// Port of <c>detectChartType(data)</c> — inspects the first row to choose pie / line / bar. Equivalent
    /// to running detection over the normalised rows: for object-row arrays the normaliser leaves the rows
    /// untouched, and every other shape (Chart.js-shaped, primitive, empty) detects as <c>bar</c>.
    /// </summary>
    public static string DetectChartType(object? data)
    {
        if (data is not IReadOnlyList<object?> list || list.Count == 0)
        {
            return "bar";
        }

        if (list[0] is not IReadOnlyDictionary<string, object?> sample)
        {
            return "bar";
        }

        bool hasLabel = sample.ContainsKey("label");
        bool hasValue = sample.ContainsKey("value");
        bool hasX = sample.ContainsKey("x");
        bool hasDate = sample.ContainsKey("date");

        if (hasLabel && hasValue && !hasX && !hasDate)
        {
            return "pie";
        }

        if (hasX || hasDate)
        {
            return "line";
        }

        return "bar";
    }

    /// <summary>
    /// Port of <c>resolveChartVariant(spec, data)</c> — an explicit <c>spec.chartType</c> wins, otherwise
    /// the type is detected from the data.
    /// </summary>
    public static string ResolveChartVariant(IReadOnlyDictionary<string, object?>? spec, object? data)
    {
        if (spec != null && spec.TryGetValue("chartType", out object? chartType) && chartType is string s && s.Length > 0)
        {
            return s;
        }

        return DetectChartType(data);
    }
}
