using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>Chart.jsx</c> — a read-only chart renderer. The chart type comes from <c>Variant</c>; config
/// and series come from <c>Spec</c> + <c>Data</c> via the ported <c>normalizeChartData</c>. Rendered natively
/// on a <c>Canvas</c> with <c>Rectangle</c>/<c>Ellipse</c>/<c>Path2D</c> primitives (no charting dependency).
/// </summary>
public sealed record ChartProps(
    string? Variant = null,
    object? Data = null,
    IReadOnlyDictionary<string, object?>? Spec = null);

public sealed class Chart : Component<ChartProps>
{
    private sealed record ChartModel(List<Dictionary<string, object?>> Rows, string LabelKey, List<string> SeriesKeys);

    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        IReadOnlyDictionary<string, object?> spec = Props.Spec ?? new Dictionary<string, object?>();

        ChartModel? model = Normalize(Props.Data, spec);
        if (model is null || model.Rows.Count == 0 || model.SeriesKeys.Count == 0)
        {
            return TextBlock("No chart data").FontSize(12).Opacity(0.7).Foreground(theme.TextPrimary);
        }

        string variant = Props.Variant ?? "bar";
        double height = BoardShared.AsNumber(GetValue(spec, "height")) ?? 220;
        const double width = 360;
        bool stacked = GetValue(spec, "stacked") is true;
        bool showLegend = GetValue(spec, "legend") is not false
            && (model.SeriesKeys.Count > 1 || variant is "pie" or "doughnut");

        var palette = theme.CreateChartPalette().ToList();

        const double left = 6;
        const double top = 8;
        const double right = 8;
        const double bottom = 18;
        double plotW = width - left - right;
        double plotH = height - top - bottom;

        List<Element> children = variant switch
        {
            "pie" or "doughnut" => BuildPie(model, palette, width, height, variant == "doughnut", theme),
            "line" or "area" => BuildLineArea(model, palette, left, top, plotW, plotH, variant == "area", theme),
            "scatter" => BuildScatter(model, palette, left, top, plotW, plotH, theme),
            _ => BuildBar(model, palette, left, top, plotW, plotH, stacked, theme),
        };

        Element canvas = Canvas(children.ToArray()).Width(width).Height(height);

        if (!showLegend)
        {
            return canvas;
        }

        IReadOnlyList<string> legendLabels = variant is "pie" or "doughnut"
            ? model.Rows.Select(row => BoardShared.Stringify(row.TryGetValue(model.LabelKey, out object? l) ? l : string.Empty)).ToList()
            : model.SeriesKeys;

        Element legend = HStack(10, legendLabels
            .Select((labelText, index) => (Element)HStack(4,
                Rectangle().Width(10).Height(10).Fill(palette[index % palette.Count]),
                TextBlock(labelText).FontSize(11).Foreground(theme.TextPrimary)))
            .ToArray());

        return VStack(6, canvas, legend);
    }

    private static List<Element> BuildBar(ChartModel model, List<Brush> palette, double left, double top, double plotW, double plotH, bool stacked, AppTheme theme)
    {
        var children = new List<Element> { Baseline(left, top, plotW, plotH, theme) };
        int rowCount = model.Rows.Count;
        int seriesCount = model.SeriesKeys.Count;
        double max = MaxValue(model, stacked);
        double groupW = plotW / rowCount;

        for (int i = 0; i < rowCount; i++)
        {
            Dictionary<string, object?> row = model.Rows[i];
            double runningBottom = top + plotH;

            for (int j = 0; j < seriesCount; j++)
            {
                double value = BoardShared.AsNumber(row.TryGetValue(model.SeriesKeys[j], out object? v) ? v : null) ?? 0;
                double barHeight = max > 0 ? value / max * plotH : 0;

                double x;
                double barWidth;
                double y;
                if (stacked)
                {
                    barWidth = groupW * 0.6;
                    x = left + i * groupW + groupW * 0.2;
                    runningBottom -= barHeight;
                    y = runningBottom;
                }
                else
                {
                    barWidth = groupW * 0.8 / seriesCount;
                    x = left + i * groupW + groupW * 0.1 + j * barWidth;
                    y = top + plotH - barHeight;
                }

                if (barHeight > 0)
                {
                    children.Add(Rectangle()
                        .Width(Math.Max(1, barWidth * 0.9))
                        .Height(barHeight)
                        .Fill(palette[j % palette.Count])
                        .Canvas(x, y)
                        .WithKey($"bar-{i}-{j}"));
                }
            }

            children.Add(AxisLabel(model, row, left + i * groupW, top + plotH + 2, theme, $"barlbl-{i}"));
        }

        return children;
    }

    private static List<Element> BuildLineArea(ChartModel model, List<Brush> palette, double left, double top, double plotW, double plotH, bool area, AppTheme theme)
    {
        var children = new List<Element> { Baseline(left, top, plotW, plotH, theme) };
        int rowCount = model.Rows.Count;
        double max = MaxValue(model, false);

        double XAt(int index) => rowCount <= 1 ? left + plotW / 2 : left + index * (plotW / (rowCount - 1));
        double YAt(double value) => top + plotH - (max > 0 ? value / max * plotH : 0);

        for (int j = 0; j < model.SeriesKeys.Count; j++)
        {
            Brush brush = palette[j % palette.Count];
            var points = new List<Point>();
            for (int i = 0; i < rowCount; i++)
            {
                double value = BoardShared.AsNumber(model.Rows[i].TryGetValue(model.SeriesKeys[j], out object? v) ? v : null) ?? 0;
                points.Add(new Point(XAt(i), YAt(value)));
            }

            if (points.Count == 0)
            {
                continue;
            }

            if (area)
            {
                var areaPoints = new List<Point>(points)
                {
                    new(XAt(rowCount - 1), top + plotH),
                    new(XAt(0), top + plotH),
                };
                children.Add(Path2D()
                    .Set(path =>
                    {
                        path.Data = BuildPolyline(areaPoints, true);
                        path.Fill = brush;
                        path.Opacity = 0.3;
                    })
                    .Canvas(0, 0)
                    .WithKey($"area-{j}"));
            }

            children.Add(Path2D()
                .Set(path => path.Data = BuildPolyline(points, false))
                .Stroke(brush)
                .StrokeThickness(2)
                .Canvas(0, 0)
                .WithKey($"line-{j}"));
        }

        for (int i = 0; i < rowCount; i++)
        {
            children.Add(AxisLabel(model, model.Rows[i], XAt(i) - 8, top + plotH + 2, theme, $"linelbl-{i}"));
        }

        return children;
    }

    private static List<Element> BuildScatter(ChartModel model, List<Brush> palette, double left, double top, double plotW, double plotH, AppTheme theme)
    {
        var children = new List<Element> { Baseline(left, top, plotW, plotH, theme) };
        int rowCount = model.Rows.Count;
        string yKey = model.SeriesKeys[0];
        double max = MaxValue(model, false);
        Brush brush = palette[0];

        for (int i = 0; i < rowCount; i++)
        {
            double value = BoardShared.AsNumber(model.Rows[i].TryGetValue(yKey, out object? v) ? v : null) ?? 0;
            double x = rowCount <= 1 ? left + plotW / 2 : left + i * (plotW / (rowCount - 1));
            double y = top + plotH - (max > 0 ? value / max * plotH : 0);
            children.Add(Ellipse().Width(8).Height(8).Fill(brush).Canvas(x - 4, y - 4).WithKey($"pt-{i}"));
        }

        return children;
    }

    private static List<Element> BuildPie(ChartModel model, List<Brush> palette, double width, double height, bool doughnut, AppTheme theme)
    {
        var children = new List<Element>();
        string valueKey = model.SeriesKeys[0];
        double total = model.Rows.Sum(row => Math.Max(0, BoardShared.AsNumber(row.TryGetValue(valueKey, out object? v) ? v : null) ?? 0));
        if (total <= 0)
        {
            return children;
        }

        double centerX = width / 2;
        double centerY = height / 2;
        double radius = Math.Min(width, height) / 2 * 0.8;
        double angle = -90;

        for (int i = 0; i < model.Rows.Count; i++)
        {
            double value = Math.Max(0, BoardShared.AsNumber(model.Rows[i].TryGetValue(valueKey, out object? v) ? v : null) ?? 0);
            if (value <= 0)
            {
                continue;
            }

            double sweep = value / total * 360;
            double startAngle = angle;
            double endAngle = angle + sweep;
            angle = endAngle;

            Point start = PointOnCircle(centerX, centerY, radius, startAngle);
            Point end = PointOnCircle(centerX, centerY, radius, endAngle);
            bool largeArc = sweep > 180;
            Brush brush = palette[i % palette.Count];

            children.Add(Path2D()
                .Set(path =>
                {
                    path.Data = BuildWedge(new Point(centerX, centerY), start, end, radius, largeArc);
                    path.Fill = brush;
                })
                .Canvas(0, 0)
                .WithKey($"slice-{i}"));
        }

        if (doughnut)
        {
            double inner = radius * 0.55;
            children.Add(Ellipse()
                .Width(inner * 2)
                .Height(inner * 2)
                .Fill(theme.WindowBackground)
                .Canvas(centerX - inner, centerY - inner)
                .WithKey("doughnut-hole"));
        }

        return children;
    }

    private static Element Baseline(double left, double top, double plotW, double plotH, AppTheme theme)
    {
        return Rectangle()
            .Width(plotW)
            .Height(1)
            .Fill(theme.CardBorder)
            .Canvas(left, top + plotH)
            .WithKey("chart-baseline");
    }

    private static Element AxisLabel(ChartModel model, Dictionary<string, object?> row, double x, double y, AppTheme theme, string key)
    {
        string text = BoardShared.Stringify(row.TryGetValue(model.LabelKey, out object? value) ? value : string.Empty);
        return TextBlock(text).FontSize(9).Opacity(0.7).Foreground(theme.TextPrimary).Canvas(Math.Max(0, x), y).WithKey(key);
    }

    private static double MaxValue(ChartModel model, bool stacked)
    {
        double max = 0;
        foreach (Dictionary<string, object?> row in model.Rows)
        {
            if (stacked)
            {
                double sum = model.SeriesKeys.Sum(key => Math.Max(0, BoardShared.AsNumber(row.TryGetValue(key, out object? v) ? v : null) ?? 0));
                max = Math.Max(max, sum);
            }
            else
            {
                foreach (string key in model.SeriesKeys)
                {
                    max = Math.Max(max, BoardShared.AsNumber(row.TryGetValue(key, out object? v) ? v : null) ?? 0);
                }
            }
        }

        return max;
    }

    private static Point PointOnCircle(double cx, double cy, double radius, double angleDegrees)
    {
        double radians = angleDegrees * Math.PI / 180;
        return new Point(cx + radius * Math.Cos(radians), cy + radius * Math.Sin(radians));
    }

    private static PathGeometry BuildPolyline(IReadOnlyList<Point> points, bool closed)
    {
        var figure = new PathFigure { StartPoint = points[0], IsClosed = closed };
        var segment = new PolyLineSegment();
        for (int i = 1; i < points.Count; i++)
        {
            segment.Points.Add(points[i]);
        }

        figure.Segments.Add(segment);
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static PathGeometry BuildWedge(Point center, Point start, Point end, double radius, bool largeArc)
    {
        var figure = new PathFigure { StartPoint = center, IsClosed = true };
        figure.Segments.Add(new LineSegment { Point = start });
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            IsLargeArc = largeArc,
            SweepDirection = SweepDirection.Clockwise,
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static ChartModel? Normalize(object? data, IReadOnlyDictionary<string, object?> spec)
    {
        if (data is IReadOnlyDictionary<string, object?> map
            && map.TryGetValue("labels", out object? labelsObj) && labelsObj is IEnumerable<object?> labelsEnum
            && map.TryGetValue("datasets", out object? datasetsObj) && datasetsObj is IEnumerable<object?> datasetsEnum)
        {
            var labels = labelsEnum.ToList();
            var datasets = datasetsEnum.OfType<IReadOnlyDictionary<string, object?>>().ToList();
            var seriesNames = datasets
                .Select((dataset, index) => dataset.TryGetValue("label", out object? l) && l != null ? l.ToString()! : $"series{index + 1}")
                .ToList();

            var rows = new List<Dictionary<string, object?>>();
            for (int i = 0; i < labels.Count; i++)
            {
                var row = new Dictionary<string, object?>(StringComparer.Ordinal) { ["__label"] = labels[i] };
                for (int j = 0; j < datasets.Count; j++)
                {
                    object? value = datasets[j].TryGetValue("data", out object? dataArray) && dataArray is IReadOnlyList<object?> arr && i < arr.Count
                        ? arr[i]
                        : null;
                    row[seriesNames[j]] = value;
                }

                rows.Add(row);
            }

            return new ChartModel(rows, "__label", seriesNames);
        }

        if (data is not IEnumerable<object?> sequence)
        {
            return null;
        }

        var list = sequence.ToList();
        if (list.Count == 0)
        {
            return null;
        }

        if (list[0] is not IReadOnlyDictionary<string, object?>)
        {
            var rows = list
                .Select((value, index) => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["__label"] = (index + 1).ToString(),
                    ["value"] = value,
                })
                .ToList();
            return new ChartModel(rows, "__label", new List<string> { "value" });
        }

        var objectRows = list
            .OfType<IReadOnlyDictionary<string, object?>>()
            .Select(dict => new Dictionary<string, object?>(dict, StringComparer.Ordinal))
            .ToList();

        IReadOnlyList<string>? columns = GetStringList(spec, "columns");
        var allKeys = objectRows[0].Keys.ToList();
        string labelKey = (columns is { Count: > 0 } ? columns[0] : null)
            ?? GetString(spec, "labelKey")
            ?? GetString(spec, "xKey")
            ?? (allKeys.Count > 0 ? allKeys[0] : string.Empty);

        List<string> seriesKeys;
        IReadOnlyList<string>? specSeries = GetStringList(spec, "series");
        if (specSeries is { Count: > 0 })
        {
            seriesKeys = specSeries.ToList();
        }
        else if (columns is { Count: > 1 })
        {
            seriesKeys = columns.Skip(1).ToList();
        }
        else
        {
            seriesKeys = allKeys
                .Where(key => key != labelKey && objectRows[0].TryGetValue(key, out object? v) && v is double or int or long or float or decimal)
                .ToList();
            if (seriesKeys.Count == 0)
            {
                seriesKeys = allKeys.Where(key => key != labelKey).Take(1).ToList();
            }
        }

        return new ChartModel(objectRows, labelKey, seriesKeys);
    }

    private static object? GetValue(IReadOnlyDictionary<string, object?> spec, string key) =>
        spec.TryGetValue(key, out object? value) ? value : null;

    private static string? GetString(IReadOnlyDictionary<string, object?> spec, string key) =>
        GetValue(spec, key) is string s && s.Length > 0 ? s : null;

    private static IReadOnlyList<string>? GetStringList(IReadOnlyDictionary<string, object?> spec, string key) =>
        GetValue(spec, key) is IEnumerable<object?> seq ? seq.Select(BoardShared.Stringify).ToList() : null;
}
