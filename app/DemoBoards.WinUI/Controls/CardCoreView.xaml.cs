using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DemoBoards_WinUI.Controls;

public sealed partial class CardCoreView : UserControl
{
    private static readonly string[] ChartPalette =
    {
        "#4e79a7", "#f28e2b", "#e15759", "#76b7b2", "#59a14f",
        "#edc948", "#b07aa1", "#ff9da7", "#9c755f", "#bab0ac"
    };

    public sealed record SaveRequest(string Kind, string? WriteTo, string? ButtonId = null, string? ElemId = null);
    private sealed record ChoiceOption(string Value, string Label);
    private sealed record ChartModel(IReadOnlyList<Dictionary<string, object?>> Rows, string LabelKey, IReadOnlyList<string> SeriesKeys);
    private sealed record TableEditorConfig(
        IReadOnlyList<string> Columns,
        IReadOnlyDictionary<string, string> ColumnTypes,
        bool CanAdd,
        bool CanDelete,
        string? Placeholder);
    private sealed record FieldConfig(
        string Key,
        string? Title,
        string? Type,
        string? Format,
        string? Placeholder,
        bool IsRequired,
        IReadOnlyList<ChoiceOption> Options,
        string? ActionLabel,
        string? DiscardLabel,
        string? SaveLabel);
    private sealed record SingleFieldConfig(string? WriteTo, string FieldKey, object? CurrentValue, FieldConfig Field);

    public CardCoreView()
    {
        InitializeComponent();
    }

    public void Render(string kind, string label, JsonElement data, string rawRenderDefJson, Func<object?, SaveRequest, Task>? onSave = null)
    {
        Root.Children.Clear();

        string effectiveKind = NormalizeLegacyKind(kind, rawRenderDefJson, data);
        if (!string.IsNullOrWhiteSpace(label) && effectiveKind != "metric" && effectiveKind != "alert")
        {
            Root.Children.Add(new TextBlock
            {
                Text = label,
                Opacity = 0.72,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 2)
            });
        }

        Root.Children.Add(BuildBody(effectiveKind, data, rawRenderDefJson, onSave));
    }

    private static string NormalizeLegacyKind(string kind, string rawRenderDefJson, JsonElement data)
    {
        if (string.Equals(kind, "query", StringComparison.OrdinalIgnoreCase)) return "searchbox";
        if (!string.Equals(kind, "filter", StringComparison.OrdinalIgnoreCase)) return kind;

        using JsonDocument renderDef = JsonDocument.Parse(rawRenderDefJson);
        JsonElement renderDefRoot = renderDef.RootElement;
        JsonElement renderData = renderDefRoot.TryGetProperty("data", out JsonElement dataElement) ? dataElement : default;
        JsonElement fields = renderData.ValueKind == JsonValueKind.Object && renderData.TryGetProperty("fields", out JsonElement fieldsElement) ? fieldsElement : default;
        JsonElement properties = fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty("properties", out JsonElement propertiesElement) ? propertiesElement : default;
        if (properties.ValueKind != JsonValueKind.Object || properties.EnumerateObject().Count() != 1)
        {
            return "form";
        }

        JsonProperty prop = properties.EnumerateObject().First();
        if ((prop.Value.TryGetProperty("enum", out JsonElement enumElement) && enumElement.ValueKind == JsonValueKind.Array)
            || data.ValueKind == JsonValueKind.Array)
        {
            return "selection";
        }

        if (!prop.Value.TryGetProperty("type", out JsonElement typeElement)
            || typeElement.ValueKind != JsonValueKind.String
            || string.Equals(typeElement.GetString(), "string", StringComparison.OrdinalIgnoreCase))
        {
            return "searchbox";
        }

        return "form";
    }

    private static UIElement BuildBody(string kind, JsonElement data, string rawRenderDefJson, Func<object?, SaveRequest, Task>? onSave)
    {
        return kind switch
        {
            "table" => BuildTable(data, rawRenderDefJson),
            "metric" => BuildMetric(data, rawRenderDefJson),
            "list" => BuildList(data, rawRenderDefJson),
            "chart" => BuildChart(data, rawRenderDefJson),
            "narrative" => BuildNarrative(data),
            "badge" => BuildBadge(data, rawRenderDefJson),
            "alert" => BuildAlert(data),
            "markdown" => BuildMarkdown(data),
            "markup" => BuildMarkdown(data),
            "actions" => BuildActions(data, rawRenderDefJson, onSave),
            "text" => BuildText(data, rawRenderDefJson),
            "searchbox" => BuildSimpleEditor(data, rawRenderDefJson, onSave),
            "selection" => BuildSimpleSelection(data, rawRenderDefJson, onSave),
            "form" => BuildObjectForm(data, rawRenderDefJson, onSave),
            "notes" => BuildNotes(data, rawRenderDefJson, onSave),
            "editable-table" => BuildEditableTable(data, rawRenderDefJson, onSave),
            "todo" => BuildTodo(data, rawRenderDefJson, onSave),
            _ => BuildText(data, rawRenderDefJson),
        };
    }

    private static UIElement BuildTable(JsonElement data, string rawRenderDefJson)
    {
        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            return BuildMutedText("No data");
        }

        var stack = new StackPanel { Spacing = 4 };
        JsonElement first = data.EnumerateArray().First();
        if (first.ValueKind != JsonValueKind.Object)
        {
            int idx = 1;
            foreach (JsonElement item in data.EnumerateArray())
            {
                stack.Children.Add(new TextBlock { Text = $"{idx++}. {RenderScalar(item)}", TextWrapping = TextWrapping.WrapWholeWords });
            }
            return stack;
        }

        string[] columns = first.EnumerateObject().Select(prop => prop.Name).ToArray();
        stack.Children.Add(new TextBlock
        {
            Text = string.Join(" | ", columns),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.WrapWholeWords
        });
        foreach (JsonElement row in data.EnumerateArray())
        {
            string line = string.Join(" | ", columns.Select(column => row.TryGetProperty(column, out JsonElement value) ? RenderScalar(value) : string.Empty));
            stack.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.WrapWholeWords });
        }
        return new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static UIElement BuildMetric(JsonElement data, string rawRenderDefJson)
    {
        string title = string.Empty;
        string value = "—";
        string detail = string.Empty;
        if (data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("title", out JsonElement titleElement)) title = RenderScalar(titleElement);
            else if (data.TryGetProperty("label", out JsonElement labelElement)) title = RenderScalar(labelElement);
            if (data.TryGetProperty("value", out JsonElement valueElement)) value = RenderScalar(valueElement);
            if (data.TryGetProperty("detail", out JsonElement detailElement)) detail = RenderScalar(detailElement);
        }
        else if (data.ValueKind != JsonValueKind.Undefined && data.ValueKind != JsonValueKind.Null)
        {
            value = RenderScalar(data);
        }

        var stack = new StackPanel { Spacing = 4 };
        if (!string.IsNullOrWhiteSpace(title)) stack.Children.Add(new TextBlock { Text = title, Opacity = 0.72 });
        stack.Children.Add(new TextBlock { Text = value, FontSize = 28, FontWeight = FontWeights.SemiBold });
        if (!string.IsNullOrWhiteSpace(detail)) stack.Children.Add(new TextBlock { Text = detail, Opacity = 0.72, TextWrapping = TextWrapping.WrapWholeWords });
        return stack;
    }

    private static UIElement BuildChart(JsonElement data, string rawRenderDefJson)
    {
        using JsonDocument renderDef = JsonDocument.Parse(rawRenderDefJson);
        JsonElement root = renderDef.RootElement;
        JsonElement viewData = root.TryGetProperty("data", out JsonElement renderData) ? renderData : default;
        ChartModel? model = NormalizeChartData(data, viewData);
        if (model is null || model.Rows.Count == 0 || model.SeriesKeys.Count == 0)
        {
            return BuildMutedText("No chart data");
        }

        string chartType = viewData.ValueKind == JsonValueKind.Object
            && viewData.TryGetProperty("chartType", out JsonElement chartTypeElement)
            && chartTypeElement.ValueKind == JsonValueKind.String
            ? chartTypeElement.GetString()?.Trim().ToLowerInvariant() ?? string.Empty
            : DetectChartType(model.Rows);
        if (string.IsNullOrWhiteSpace(chartType))
        {
            chartType = "bar";
        }

        bool stacked = viewData.ValueKind == JsonValueKind.Object
            && viewData.TryGetProperty("stacked", out JsonElement stackedElement)
            && stackedElement.ValueKind == JsonValueKind.True;
        bool showLegend = !(viewData.ValueKind == JsonValueKind.Object
            && viewData.TryGetProperty("legend", out JsonElement legendElement)
            && legendElement.ValueKind == JsonValueKind.False)
            && (model.SeriesKeys.Count > 1 || chartType is "pie" or "doughnut");
        bool showGrid = !(viewData.ValueKind == JsonValueKind.Object
            && viewData.TryGetProperty("grid", out JsonElement gridElement)
            && gridElement.ValueKind == JsonValueKind.False);
        int height = viewData.ValueKind == JsonValueKind.Object
            && viewData.TryGetProperty("height", out JsonElement heightElement)
            && heightElement.ValueKind == JsonValueKind.Number
            ? Math.Max(140, heightElement.GetInt32())
            : 220;

        var webView = new WebView2
        {
            Height = height,
            MinHeight = 140
        };
        webView.NavigateToString(BuildChartDocument(model, chartType, height, stacked, showLegend, showGrid));
        return webView;
    }

    private static UIElement BuildList(JsonElement data, string rawRenderDefJson)
    {
        if (data.ValueKind == JsonValueKind.Array)
        {
            var stack = new StackPanel { Spacing = 4 };
            foreach (JsonElement item in data.EnumerateArray())
            {
                stack.Children.Add(new TextBlock { Text = $"• {RenderScalar(item)}", TextWrapping = TextWrapping.WrapWholeWords });
            }
            return stack;
        }

        if (data.ValueKind == JsonValueKind.Object)
        {
            var stack = new StackPanel { Spacing = 2 };
            foreach (JsonProperty property in data.EnumerateObject())
            {
                stack.Children.Add(new TextBlock { Text = $"{property.Name}: {RenderScalar(property.Value)}", TextWrapping = TextWrapping.WrapWholeWords });
            }
            return stack;
        }

        return new TextBlock { Text = RenderScalar(data), TextWrapping = TextWrapping.WrapWholeWords };
    }

    private static ChartModel? NormalizeChartData(JsonElement data, JsonElement viewData)
    {
        if (data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("labels", out JsonElement labelsElement)
            && labelsElement.ValueKind == JsonValueKind.Array
            && data.TryGetProperty("datasets", out JsonElement datasetsElement)
            && datasetsElement.ValueKind == JsonValueKind.Array)
        {
            string[] labels = labelsElement.EnumerateArray().Select(RenderScalar).ToArray();
            JsonElement[] datasets = datasetsElement.EnumerateArray().ToArray();
            string[] seriesNames = datasets.Select((dataset, index) =>
                dataset.ValueKind == JsonValueKind.Object && dataset.TryGetProperty("label", out JsonElement labelElement)
                    ? RenderScalar(labelElement)
                    : $"series{index + 1}").ToArray();
            var rows = new List<Dictionary<string, object?>>();
            for (int index = 0; index < labels.Length; index++)
            {
                var row = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["__label"] = labels[index]
                };
                for (int datasetIndex = 0; datasetIndex < datasets.Length; datasetIndex++)
                {
                    JsonElement dataset = datasets[datasetIndex];
                    object? value = dataset.ValueKind == JsonValueKind.Object
                        && dataset.TryGetProperty("data", out JsonElement pointsElement)
                        && pointsElement.ValueKind == JsonValueKind.Array
                        && index < pointsElement.GetArrayLength()
                        ? ConvertJsonElement(pointsElement[index])
                        : null;
                    row[seriesNames[datasetIndex]] = value;
                }
                rows.Add(row);
            }
            return new ChartModel(rows, "__label", seriesNames);
        }

        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            return null;
        }

        JsonElement first = data[0];
        if (first.ValueKind != JsonValueKind.Object)
        {
            var rows = new List<Dictionary<string, object?>>();
            int index = 1;
            foreach (JsonElement item in data.EnumerateArray())
            {
                rows.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["__label"] = index++.ToString(),
                    ["value"] = ConvertJsonElement(item)
                });
            }

            return new ChartModel(rows, "__label", new[] { "value" });
        }

        string[] allKeys = first.EnumerateObject().Select(property => property.Name).ToArray();
        string[]? configuredColumns = viewData.ValueKind == JsonValueKind.Object
            && viewData.TryGetProperty("columns", out JsonElement columnsElement)
            && columnsElement.ValueKind == JsonValueKind.Array
            ? columnsElement.EnumerateArray().Select(RenderScalar).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()
            : null;
        string labelKey = configuredColumns?.FirstOrDefault()
            ?? (viewData.ValueKind == JsonValueKind.Object && viewData.TryGetProperty("labelKey", out JsonElement labelKeyElement) ? RenderScalar(labelKeyElement) : null)
            ?? (viewData.ValueKind == JsonValueKind.Object && viewData.TryGetProperty("xKey", out JsonElement xKeyElement) ? RenderScalar(xKeyElement) : null)
            ?? allKeys.FirstOrDefault()
            ?? "label";

        string[] seriesKeys = viewData.ValueKind == JsonValueKind.Object
            && viewData.TryGetProperty("series", out JsonElement seriesElement)
            && seriesElement.ValueKind == JsonValueKind.Array
            ? seriesElement.EnumerateArray().Select(RenderScalar).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()
            : Array.Empty<string>();
        if (seriesKeys.Length == 0 && configuredColumns is { Length: > 1 })
        {
            seriesKeys = configuredColumns.Skip(1).ToArray();
        }

        if (seriesKeys.Length == 0)
        {
            seriesKeys = allKeys
                .Where(key => !string.Equals(key, labelKey, StringComparison.Ordinal)
                    && first.TryGetProperty(key, out JsonElement valueElement)
                    && valueElement.ValueKind == JsonValueKind.Number)
                .ToArray();
        }

        if (seriesKeys.Length == 0)
        {
            seriesKeys = allKeys.Where(key => !string.Equals(key, labelKey, StringComparison.Ordinal)).Take(1).ToArray();
        }

        var normalizedRows = new List<Dictionary<string, object?>>();
        foreach (JsonElement rowElement in data.EnumerateArray())
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (JsonProperty property in rowElement.EnumerateObject())
            {
                row[property.Name] = ConvertJsonElement(property.Value);
            }
            normalizedRows.Add(row);
        }

        return new ChartModel(normalizedRows, labelKey, seriesKeys);
    }

    private static string DetectChartType(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        if (rows.Count == 0)
        {
            return "bar";
        }

        Dictionary<string, object?> sample = rows[0];
        if (sample.ContainsKey("label") && sample.ContainsKey("value") && !sample.ContainsKey("x") && !sample.ContainsKey("date"))
        {
            return "pie";
        }

        if (sample.ContainsKey("x") || sample.ContainsKey("date"))
        {
            return "line";
        }

        return "bar";
    }

    private static string BuildChartDocument(ChartModel model, string chartType, int height, bool stacked, bool showLegend, bool showGrid)
    {
        string rowsJson = JsonSerializer.Serialize(model.Rows);
        string seriesJson = JsonSerializer.Serialize(model.SeriesKeys);
        string paletteJson = JsonSerializer.Serialize(ChartPalette);
        string labelKeyJson = JsonSerializer.Serialize(model.LabelKey);
        string chartTypeJson = JsonSerializer.Serialize(chartType);

        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\" /><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        html.AppendLine("<style>");
        html.AppendLine(BoardTheme.BuildWebViewCssVariables());
        html.AppendLine("body{margin:0;font-family:'Segoe UI',sans-serif;color:var(--color-text);background:transparent;}");
        html.AppendLine("#wrap{display:flex;flex-direction:column;gap:8px;height:100%;}");
        html.AppendLine("canvas{width:100%;height:auto;background:transparent;border-radius:8px;}");
        html.AppendLine("#legend{display:flex;flex-wrap:wrap;gap:8px 12px;font-size:11px;}");
        html.AppendLine(".item{display:flex;align-items:center;gap:6px;}");
        html.AppendLine(".swatch{width:10px;height:10px;border-radius:999px;display:inline-block;}");
        html.AppendLine("</style></head><body>");
        html.AppendLine($"<div id=\"wrap\" style=\"height:{height}px\"><canvas id=\"chart\" width=\"640\" height=\"{Math.Max(140, height - (showLegend ? 28 : 0))}\"></canvas><div id=\"legend\"></div></div>");
        html.AppendLine("<script>");
        html.AppendLine($"const rows={rowsJson}; const seriesKeys={seriesJson}; const palette={paletteJson}; const labelKey={labelKeyJson}; const chartType={chartTypeJson}; const stacked={(stacked ? "true" : "false")}; const showLegend={(showLegend ? "true" : "false")}; const showGrid={(showGrid ? "true" : "false")};");
        html.AppendLine("""
    const canvas=document.getElementById('chart');
    const ctx=canvas.getContext('2d');
    const legend=document.getElementById('legend');
    const W=canvas.width,H=canvas.height;
    const pad={l:44,r:16,t:12,b:28};
    function num(v){ const n=Number(v); return Number.isFinite(n)?n:null; }
    function label(v){ return v==null ? '—' : String(v); }
    function buildSeries() { return seriesKeys.map((key, i) => ({ key, color: palette[i % palette.length], values: rows.map(r => num(r[key])) })); }
    function maxValue(series){ let max=0; series.forEach(s => s.values.forEach(v => { if(v!=null) max=Math.max(max,v); })); return max || 1; }
    const css=getComputedStyle(document.documentElement);
    const textColor=css.getPropertyValue('--color-text').trim() || '#e5eefc';
    const textMutedColor=css.getPropertyValue('--color-text-muted').trim() || '#94a9c6';
    const borderColor=css.getPropertyValue('--color-border').trim() || 'rgba(148,163,184,0.3)';
    function drawAxes(max){ ctx.strokeStyle=borderColor; ctx.lineWidth=1; ctx.beginPath(); ctx.moveTo(pad.l,pad.t); ctx.lineTo(pad.l,H-pad.b); ctx.lineTo(W-pad.r,H-pad.b); ctx.stroke(); if(showGrid){ ctx.strokeStyle=borderColor; ctx.fillStyle=textMutedColor; ctx.font='10px Segoe UI'; for(let i=0;i<4;i++){ const value=max*(1-(i/3)); const y=pad.t+((H-pad.t-pad.b)*(i/3)); ctx.beginPath(); ctx.moveTo(pad.l,y); ctx.lineTo(W-pad.r,y); ctx.stroke(); ctx.fillText(String(Math.round(value*100)/100),4,y+3);} } }
    function drawLegend(series){ if(!showLegend){ legend.innerHTML=''; return; } legend.innerHTML=series.map(s => `<div class="item"><span class="swatch" style="background:${s.color}"></span><span>${s.key}</span></div>`).join(''); }
    function drawBar(series){ const max=maxValue(series); drawAxes(max); const n=rows.length||1; const innerW=W-pad.l-pad.r; const innerH=H-pad.t-pad.b; const groupW=innerW/n; const labels=rows.map(r=>label(r[labelKey])); for(let i=0;i<n;i++){ const baseX=pad.l + i*groupW; let stackY=0; series.forEach((s, si) => { const raw=s.values[i]; if(raw==null) return; const barW=stacked ? groupW*0.62 : Math.max(12,(groupW*0.72)/series.length); const x=stacked ? baseX + groupW*0.19 : baseX + groupW*0.14 + (si*barW); const h=(raw/max)*innerH; const y=H-pad.b-h-stackY; ctx.fillStyle=s.color; ctx.fillRect(x,y,barW,h); if(stacked) stackY += h; }); ctx.save(); ctx.translate(baseX + groupW/2, H-pad.b+14); ctx.rotate(-0.35); ctx.fillStyle=textMutedColor; ctx.font='10px Segoe UI'; ctx.textAlign='right'; ctx.fillText(labels[i],0,0); ctx.restore(); } drawLegend(series); }
    function drawLine(series, fill){ const max=maxValue(series); drawAxes(max); const n=rows.length||1; const innerW=W-pad.l-pad.r; const innerH=H-pad.t-pad.b; const step=n===1 ? 0 : innerW/(n-1); const labels=rows.map(r=>label(r[labelKey])); labels.forEach((text,i)=>{ const x=pad.l + (step*i); ctx.save(); ctx.translate(x,H-pad.b+14); ctx.rotate(-0.35); ctx.fillStyle=textMutedColor; ctx.font='10px Segoe UI'; ctx.textAlign='right'; ctx.fillText(text,0,0); ctx.restore();}); series.forEach(s=>{ ctx.beginPath(); let started=false; s.values.forEach((v,i)=>{ if(v==null) return; const x=pad.l + step*i; const y=H-pad.b-((v/max)*innerH); if(!started){ ctx.moveTo(x,y); started=true; } else { ctx.lineTo(x,y); } }); if(fill && started){ ctx.lineTo(pad.l + step*(n-1), H-pad.b); ctx.lineTo(pad.l, H-pad.b); ctx.closePath(); ctx.fillStyle=s.color + '55'; ctx.fill(); ctx.beginPath(); started=false; s.values.forEach((v,i)=>{ if(v==null) return; const x=pad.l + step*i; const y=H-pad.b-((v/max)*innerH); if(!started){ ctx.moveTo(x,y); started=true; } else { ctx.lineTo(x,y); } }); } ctx.strokeStyle=s.color; ctx.lineWidth=2; ctx.stroke();}); drawLegend(series); }
    function drawScatter(series){ const s=series[0]; const points=rows.map(r=>({x:num(r[labelKey]), y:num(r[s.key])})).filter(p=>p.x!=null && p.y!=null); const maxX=Math.max(...points.map(p=>p.x),1); const maxY=Math.max(...points.map(p=>p.y),1); ctx.strokeStyle='rgba(148,163,184,0.38)'; ctx.beginPath(); ctx.moveTo(pad.l,pad.t); ctx.lineTo(pad.l,H-pad.b); ctx.lineTo(W-pad.r,H-pad.b); ctx.stroke(); if(showGrid){ ctx.strokeStyle='rgba(148,163,184,0.18)'; for(let i=0;i<4;i++){ const x=pad.l+((W-pad.l-pad.r)*(i/3)); const y=pad.t+((H-pad.t-pad.b)*(i/3)); ctx.beginPath(); ctx.moveTo(x,pad.t); ctx.lineTo(x,H-pad.b); ctx.stroke(); ctx.beginPath(); ctx.moveTo(pad.l,y); ctx.lineTo(W-pad.r,y); ctx.stroke(); } } points.forEach(p=>{ const x=pad.l+((p.x/maxX)*(W-pad.l-pad.r)); const y=H-pad.b-((p.y/maxY)*(H-pad.t-pad.b)); ctx.fillStyle=s.color; ctx.beginPath(); ctx.arc(x,y,4,0,Math.PI*2); ctx.fill();}); drawLegend(series); }
    function drawPie(series, doughnut){ const key=series[0].key; const vals=rows.map(r=>({label:label(r[labelKey]), value:Math.max(0,num(r[key])||0)})).filter(v=>v.value>0); const total=vals.reduce((s,v)=>s+v.value,0) || 1; const cx=W/2, cy=(H/2)-6, radius=Math.min(W,H)*0.28; const inner=doughnut ? radius*0.58 : 0; let start=-Math.PI/2; vals.forEach((item,i)=>{ const angle=(item.value/total)*Math.PI*2; ctx.beginPath(); ctx.moveTo(cx,cy); ctx.arc(cx,cy,radius,start,start+angle); ctx.closePath(); ctx.fillStyle=palette[i%palette.length]; ctx.fill(); if(inner>0){ ctx.globalCompositeOperation='destination-out'; ctx.beginPath(); ctx.arc(cx,cy,inner,0,Math.PI*2); ctx.fill(); ctx.globalCompositeOperation='source-over'; } start += angle; }); if(showLegend){ legend.innerHTML=vals.map((item,i)=> `<div class="item"><span class="swatch" style="background:${palette[i%palette.length]}"></span><span>${item.label}</span></div>`).join(''); } }
    const series=buildSeries();
    if(chartType==='pie' || chartType==='doughnut'){ drawPie(series, chartType==='doughnut'); }
    else if(chartType==='line'){ drawLine(series, false); }
    else if(chartType==='area'){ drawLine(series, true); }
    else if(chartType==='scatter'){ drawScatter(series); }
    else { drawBar(series); }
    """);
        html.AppendLine("</script></body></html>");
        return html.ToString();
    }

    private static UIElement BuildNarrative(JsonElement data)
    {
        return new TextBlock { Text = RenderScalar(data), TextWrapping = TextWrapping.WrapWholeWords };
    }

    private static UIElement BuildBadge(JsonElement data, string rawRenderDefJson)
    {
        string text = RenderScalar(data);
        return new Border
        {
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(8),
            Background = BoardTheme.CreateStatusBrush("running", 0x22),
            Child = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold }
        };
    }

    private static UIElement BuildAlert(JsonElement data)
    {
        string title = "Alert";
        string body = RenderScalar(data);
        if (data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("title", out JsonElement titleElement)) title = RenderScalar(titleElement);
            if (data.TryGetProperty("body", out JsonElement bodyElement)) body = RenderScalar(bodyElement);
        }
        return new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(10),
            Background = BoardTheme.CreateStatusBrush("failed", 0x22),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = title, FontWeight = FontWeights.SemiBold },
                    new TextBlock { Text = body, TextWrapping = TextWrapping.WrapWholeWords }
                }
            }
        };
    }

    private static UIElement BuildMarkdown(JsonElement data)
    {
        string text = data.ValueKind switch
        {
            JsonValueKind.String => data.GetString() ?? string.Empty,
            JsonValueKind.Object when data.TryGetProperty("text", out JsonElement textElement) => RenderScalar(textElement),
            _ => data.ValueKind == JsonValueKind.Undefined || data.ValueKind == JsonValueKind.Null ? string.Empty : data.GetRawText()
        };
        var markdown = new BoardMarkdown();
        markdown.Render(text);
        return markdown;
    }

    private static UIElement BuildActions(JsonElement data, string rawRenderDefJson, Func<object?, SaveRequest, Task>? onSave)
    {
        using JsonDocument renderDef = JsonDocument.Parse(rawRenderDefJson);
        JsonElement root = renderDef.RootElement;
        JsonElement renderData = root.TryGetProperty("data", out JsonElement dataElement) ? dataElement : default;
        JsonElement buttons = renderData.ValueKind == JsonValueKind.Object && renderData.TryGetProperty("buttons", out JsonElement buttonElement)
            ? buttonElement
            : data;

        if (buttons.ValueKind != JsonValueKind.Array || buttons.GetArrayLength() == 0)
        {
            return BuildMutedText("No actions");
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (JsonElement button in buttons.EnumerateArray())
        {
            string label = button.TryGetProperty("label", out JsonElement labelElement)
                ? RenderScalar(labelElement)
                : button.TryGetProperty("id", out JsonElement idElement)
                    ? RenderScalar(idElement)
                    : "Action";
            string buttonId = button.TryGetProperty("id", out JsonElement buttonIdElement)
                ? RenderScalar(buttonIdElement)
                : label;
            var actionButton = new Button { Content = label, IsEnabled = onSave is not null };
            actionButton.Click += async (_, _) =>
            {
                if (onSave is not null)
                {
                    string elemId = root.TryGetProperty("id", out JsonElement elemIdElement) ? RenderScalar(elemIdElement) : string.Empty;
                    await onSave(null, new SaveRequest("actions", null, buttonId, elemId));
                }
            };
            row.Children.Add(actionButton);
        }
        return row;
    }

    private static UIElement BuildText(JsonElement data, string rawRenderDefJson)
    {
        return new TextBlock { Text = RenderScalar(data), TextWrapping = TextWrapping.WrapWholeWords };
    }

    private static UIElement BuildSimpleEditor(JsonElement data, string rawRenderDefJson, Func<object?, SaveRequest, Task>? onSave)
    {
        SingleFieldConfig? config = GetSingleFieldConfig(rawRenderDefJson, data);
        if (config is null)
        {
            return BuildMutedText("No query field configured");
        }

        var row = new StackPanel { Spacing = 6 };
        var editor = new TextBox
        {
            Text = FormatFieldValue(config.Field, config.CurrentValue),
            PlaceholderText = config.Field.Placeholder ?? config.Field.Title ?? config.FieldKey,
            InputScope = BuildInputScope(config.Field)
        };
        row.Children.Add(editor);
        var saveButton = new Button { Content = config.Field.ActionLabel ?? "Search", IsEnabled = onSave is not null };
        saveButton.Click += async (_, _) =>
        {
            if (onSave is not null)
            {
                object payload = BuildEditorSaveValue(config.WriteTo, config.FieldKey, ConvertFieldText(config.Field, editor.Text));
                await onSave(payload, new SaveRequest("searchbox", config.WriteTo));
            }
        };
        row.Children.Add(saveButton);
        return row;
    }

    private static UIElement BuildSimpleSelection(JsonElement data, string rawRenderDefJson, Func<object?, SaveRequest, Task>? onSave)
    {
        SingleFieldConfig? config = GetSingleFieldConfig(rawRenderDefJson, data);
        if (config is null)
        {
            return BuildMutedText("No selection configured");
        }

        var panel = new StackPanel { Spacing = 6 };
        var combo = new ComboBox();
        if (!config.Field.IsRequired)
        {
            combo.Items.Add(new ComboBoxItem { Content = "All", Tag = string.Empty });
        }

        foreach (ChoiceOption option in config.Field.Options)
        {
            combo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
        }

        string currentValue = FormatFieldValue(config.Field, config.CurrentValue);
        foreach (object item in combo.Items)
        {
            if (item is ComboBoxItem comboBoxItem
                && string.Equals(comboBoxItem.Tag?.ToString() ?? string.Empty, currentValue, StringComparison.Ordinal))
            {
                combo.SelectedItem = comboBoxItem;
                break;
            }
        }

        bool initialized = false;
        combo.Loaded += (_, _) => initialized = true;
        combo.SelectionChanged += async (_, _) =>
        {
            if (!initialized || onSave is null)
            {
                return;
            }

            string selectedValue = (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
            object payload = BuildEditorSaveValue(config.WriteTo, config.FieldKey, selectedValue);
            await onSave(payload, new SaveRequest("selection", config.WriteTo));
        };
        panel.Children.Add(combo);
        return panel;
    }

    private static UIElement BuildObjectForm(JsonElement data, string rawRenderDefJson, Func<object?, SaveRequest, Task>? onSave)
    {
        IReadOnlyList<FieldConfig> fieldConfigs = GetFormFieldConfigs(rawRenderDefJson, data);
        if (fieldConfigs.Count == 0)
        {
            return BuildText(data, "{}");
        }

        var stack = new StackPanel { Spacing = 6 };
        Dictionary<string, object?> baseValues = GetFormBaseValues(rawRenderDefJson, data);
        var getters = new Dictionary<string, Func<object?>>(StringComparer.Ordinal);
        var setters = new Dictionary<string, Action<object?>>(StringComparer.Ordinal);

        var actionsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var discardButton = new Button { Content = fieldConfigs.First().DiscardLabel ?? "Discard", Visibility = Visibility.Collapsed };
        var saveButton = new Button { Content = fieldConfigs.First().SaveLabel ?? "Save", IsEnabled = onSave is not null, Visibility = Visibility.Collapsed };

        void RefreshDirtyState()
        {
            bool dirty = getters.Any(pair => !ValuesEqual(pair.Value(), baseValues.TryGetValue(pair.Key, out object? baseValue) ? baseValue : null));
            discardButton.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
            saveButton.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (FieldConfig field in fieldConfigs)
        {
            object? baseValue = baseValues.TryGetValue(field.Key, out object? resolvedBaseValue) ? resolvedBaseValue : null;
            if (string.Equals(field.Type, "boolean", StringComparison.OrdinalIgnoreCase))
            {
                var checkBox = new CheckBox
                {
                    Content = field.Title ?? field.Key,
                    IsChecked = CoerceBoolean(baseValue)
                };
                checkBox.Checked += (_, _) => RefreshDirtyState();
                checkBox.Unchecked += (_, _) => RefreshDirtyState();
                getters[field.Key] = () => checkBox.IsChecked == true;
                setters[field.Key] = value => checkBox.IsChecked = CoerceBoolean(value);
                stack.Children.Add(checkBox);
                continue;
            }

            stack.Children.Add(new TextBlock
            {
                Text = field.Title ?? field.Key,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.WrapWholeWords,
                Opacity = 0.72
            });

            if (field.Options.Count > 0)
            {
                var combo = new ComboBox();
                foreach (ChoiceOption option in field.Options)
                {
                    combo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
                }

                string currentValue = FormatFieldValue(field, baseValue);
                foreach (object item in combo.Items)
                {
                    if (item is ComboBoxItem comboItem
                        && string.Equals(comboItem.Tag?.ToString() ?? string.Empty, currentValue, StringComparison.Ordinal))
                    {
                        combo.SelectedItem = comboItem;
                        break;
                    }
                }

                combo.SelectionChanged += (_, _) => RefreshDirtyState();
                getters[field.Key] = () => ConvertFieldText(field, (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty);
                setters[field.Key] = value => SetComboSelection(combo, FormatFieldValue(field, value));
                stack.Children.Add(combo);
                continue;
            }

            var editor = new TextBox
            {
                Text = FormatFieldValue(field, baseValue),
                PlaceholderText = field.Placeholder,
                InputScope = BuildInputScope(field),
                TextWrapping = TextWrapping.WrapWholeWords
            };
            editor.TextChanged += (_, _) => RefreshDirtyState();
            getters[field.Key] = () => ConvertFieldText(field, editor.Text);
            setters[field.Key] = value => editor.Text = FormatFieldValue(field, value);
            stack.Children.Add(editor);
        }

        discardButton.Click += (_, _) =>
        {
            foreach ((string key, Action<object?> setter) in setters)
            {
                setter(baseValues.TryGetValue(key, out object? value) ? value : null);
            }

            RefreshDirtyState();
        };

        string? writeTo = ResolveWriteTo(rawRenderDefJson);
        saveButton.Click += async (_, _) =>
        {
            if (onSave is not null)
            {
                var payload = getters.ToDictionary(pair => pair.Key, pair => pair.Value(), StringComparer.Ordinal);
                await onSave(payload, new SaveRequest("form", writeTo));
            }
        };

        actionsRow.Children.Add(discardButton);
        actionsRow.Children.Add(saveButton);
        stack.Children.Add(actionsRow);
        RefreshDirtyState();
        return stack;
    }

    private static UIElement BuildNotes(JsonElement data, string rawRenderDefJson, Func<object?, SaveRequest, Task>? onSave)
    {
        string baseContent = data.ValueKind == JsonValueKind.String ? data.GetString() ?? string.Empty : string.Empty;
        var stack = new StackPanel { Spacing = 6 };
        var editor = new TextBox
        {
            Text = baseContent,
            AcceptsReturn = true,
            MinHeight = 120,
            PlaceholderText = "Write markdown...",
            TextWrapping = TextWrapping.Wrap
        };
        stack.Children.Add(editor);
        var actionsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var discardButton = new Button { Content = "Discard", Visibility = Visibility.Collapsed };
        var saveButton = new Button { Content = "Save", IsEnabled = onSave is not null, Visibility = Visibility.Collapsed };

        void RefreshDirtyState()
        {
            bool dirty = !string.Equals(editor.Text ?? string.Empty, baseContent, StringComparison.Ordinal);
            discardButton.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
            saveButton.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        }

        editor.TextChanged += (_, _) => RefreshDirtyState();
        discardButton.Click += (_, _) =>
        {
            editor.Text = baseContent;
            RefreshDirtyState();
        };
        saveButton.Click += async (_, _) =>
        {
            if (onSave is not null)
            {
                await onSave(editor.Text, new SaveRequest("notes", ResolveWriteTo(rawRenderDefJson)));
            }
        };
        actionsRow.Children.Add(discardButton);
        actionsRow.Children.Add(saveButton);
        stack.Children.Add(actionsRow);
        RefreshDirtyState();
        return stack;
    }

    private static UIElement BuildEditableTable(JsonElement data, string rawRenderDefJson, Func<object?, SaveRequest, Task>? onSave)
    {
        TableEditorConfig config = GetTableEditorConfig(rawRenderDefJson, data);
        var baseRows = MergeRows(data);
        var currentRows = MergeRows(data);
        var stack = new StackPanel { Spacing = 6 };

        if (config.Columns.Count == 0 && !config.CanAdd)
        {
            return BuildMutedText(config.Placeholder ?? "No data");
        }

        var tableScrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 140,
            MaxHeight = 320
        };
        var tableGrid = new Grid();
        tableScrollViewer.Content = tableGrid;

        var actionsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var addButton = new Button { Content = "+ Add row", Visibility = config.CanAdd ? Visibility.Visible : Visibility.Collapsed };
        var discardButton = new Button { Content = "Discard", Visibility = Visibility.Collapsed };
        var saveButton = new Button { Content = "Save", IsEnabled = onSave is not null, Visibility = Visibility.Collapsed };

        void RefreshDirtyState()
        {
            bool dirty = !ValuesEqual(currentRows, baseRows);
            discardButton.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
            saveButton.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        }

        void UpdateRows(List<Dictionary<string, object?>> nextRows)
        {
            currentRows = CloneRows(nextRows);
            RenderEditableTableGrid(tableGrid, currentRows, config, UpdateRows);
            RefreshDirtyState();
        }

        addButton.Click += (_, _) =>
        {
            var nextRows = CloneRows(currentRows);
            var nextRow = config.Columns.ToDictionary(column => column, _ => (object?)string.Empty, StringComparer.Ordinal);
            nextRows.Add(nextRow);
            UpdateRows(nextRows);
        };

        discardButton.Click += (_, _) =>
        {
            currentRows = CloneRows(baseRows);
            RenderEditableTableGrid(tableGrid, currentRows, config, UpdateRows);
            RefreshDirtyState();
        };

        saveButton.Click += async (_, _) =>
        {
            if (onSave is null)
            {
                return;
            }

            await onSave(CloneRows(currentRows), new SaveRequest("editable-table", ResolveWriteTo(rawRenderDefJson)));
        };

        RenderEditableTableGrid(tableGrid, currentRows, config, UpdateRows);
        actionsRow.Children.Add(addButton);
        actionsRow.Children.Add(discardButton);
        actionsRow.Children.Add(saveButton);
        stack.Children.Add(tableScrollViewer);
        stack.Children.Add(actionsRow);
        RefreshDirtyState();
        return stack;
    }

    private static UIElement BuildTodo(JsonElement data, string rawRenderDefJson, Func<object?, SaveRequest, Task>? onSave)
    {
        if (data.ValueKind != JsonValueKind.Array)
        {
            return BuildMutedText("No todo items");
        }

        var stack = new StackPanel { Spacing = 6 };
        var items = new List<Dictionary<string, object?>>();
        foreach (JsonElement item in data.EnumerateArray())
        {
            bool done = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("done", out JsonElement doneElement) && doneElement.ValueKind == JsonValueKind.True;
            string text = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out JsonElement textElement) ? RenderScalar(textElement) : RenderScalar(item);
            var todo = new Dictionary<string, object?> { ["text"] = text, ["done"] = done };
            items.Add(todo);
            var checkBox = new CheckBox { Content = text, IsChecked = done, IsEnabled = onSave is not null };
            int itemIndex = items.Count - 1;
            checkBox.Checked += async (_, _) => await SaveTodoAsync(onSave, items, itemIndex, true, rawRenderDefJson);
            checkBox.Unchecked += async (_, _) => await SaveTodoAsync(onSave, items, itemIndex, false, rawRenderDefJson);
            stack.Children.Add(checkBox);
        }

        var composer = new TextBox { PlaceholderText = "Add todo item" };
        var addButton = new Button { Content = "Add", IsEnabled = onSave is not null };
        addButton.Click += async (_, _) =>
        {
            string nextText = composer.Text.Trim();
            if (onSave is null || string.IsNullOrWhiteSpace(nextText))
            {
                return;
            }

            var nextItems = items.Select(item => new Dictionary<string, object?>(item, StringComparer.Ordinal)).ToList();
            nextItems.Add(new Dictionary<string, object?> { ["text"] = nextText, ["done"] = false });
            await onSave(nextItems, new SaveRequest("todo", ResolveWriteTo(rawRenderDefJson)));
            composer.Text = string.Empty;
        };
        stack.Children.Add(composer);
        stack.Children.Add(addButton);
        return stack;
    }

    private static async Task SaveTodoAsync(Func<object?, SaveRequest, Task>? onSave, List<Dictionary<string, object?>> items, int itemIndex, bool done, string rawRenderDefJson)
    {
        if (onSave is null || itemIndex < 0 || itemIndex >= items.Count)
        {
            return;
        }

        var nextItems = items.Select(item => new Dictionary<string, object?>(item, StringComparer.Ordinal)).ToList();
        nextItems[itemIndex]["done"] = done;
        await onSave(nextItems, new SaveRequest("todo", ResolveWriteTo(rawRenderDefJson)));
    }

    private static SingleFieldConfig? GetSingleFieldConfig(string rawRenderDefJson, JsonElement data)
    {
        using JsonDocument renderDef = JsonDocument.Parse(rawRenderDefJson);
        JsonElement root = renderDef.RootElement;
        JsonElement renderData = root.TryGetProperty("data", out JsonElement dataElement) ? dataElement : default;
        string? writeTo = renderData.ValueKind == JsonValueKind.Object && renderData.TryGetProperty("writeTo", out JsonElement writeToElement)
            ? RenderScalar(writeToElement)
            : null;
        JsonElement fields = renderData.ValueKind == JsonValueKind.Object && renderData.TryGetProperty("fields", out JsonElement fieldsElement) ? fieldsElement : default;
        JsonElement properties = fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty("properties", out JsonElement propertiesElement) ? propertiesElement : default;
        if (properties.ValueKind != JsonValueKind.Object || properties.EnumerateObject().Count() != 1)
        {
            return null;
        }

        JsonProperty prop = properties.EnumerateObject().First();
        string fieldKey = prop.Name;
        object? currentValue = ResolveSingleFieldCurrentValue(writeTo, fieldKey, rawRenderDefJson, data);
        return new SingleFieldConfig(writeTo, fieldKey, currentValue, BuildFieldConfig(fieldKey, prop.Value, renderData, data));
    }

    private static object? ResolveSingleFieldCurrentValue(string? writeTo, string fieldKey, string rawRenderDefJson, JsonElement data)
    {
        if (TryGetResolvedWriteValue(rawRenderDefJson, out JsonElement resolvedWriteValue))
        {
            if (string.Equals(writeTo, "card_data", StringComparison.Ordinal)
                && resolvedWriteValue.ValueKind == JsonValueKind.Object
                && resolvedWriteValue.TryGetProperty(fieldKey, out JsonElement fieldValue))
            {
                return ConvertJsonElement(fieldValue);
            }

            return ConvertJsonElement(resolvedWriteValue);
        }

        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty(fieldKey, out JsonElement valueElement))
        {
            return ConvertJsonElement(valueElement);
        }

        return ConvertJsonElement(data);
    }

    private static IReadOnlyList<FieldConfig> GetFormFieldConfigs(string rawRenderDefJson, JsonElement data)
    {
        using JsonDocument renderDef = JsonDocument.Parse(rawRenderDefJson);
        JsonElement root = renderDef.RootElement;
        JsonElement renderData = root.TryGetProperty("data", out JsonElement dataElement) ? dataElement : default;
        JsonElement fields = renderData.ValueKind == JsonValueKind.Object && renderData.TryGetProperty("fields", out JsonElement fieldsElement) ? fieldsElement : default;
        JsonElement properties = fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty("properties", out JsonElement propertiesElement) ? propertiesElement : default;
        if (properties.ValueKind == JsonValueKind.Object)
        {
            return properties.EnumerateObject().Select(prop => BuildFieldConfig(prop.Name, prop.Value, renderData, data)).ToArray();
        }

        if (data.ValueKind == JsonValueKind.Object)
        {
            return data.EnumerateObject().Select(prop => new FieldConfig(prop.Name, prop.Name, null, null, null, false, Array.Empty<ChoiceOption>(), null, null, null)).ToArray();
        }

        return Array.Empty<FieldConfig>();
    }

    private static Dictionary<string, object?> GetFormBaseValues(string rawRenderDefJson, JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Object)
        {
            return data.EnumerateObject().ToDictionary(property => property.Name, property => ConvertJsonElement(property.Value), StringComparer.Ordinal);
        }

        if (TryGetResolvedWriteValue(rawRenderDefJson, out JsonElement resolvedWriteValue)
            && resolvedWriteValue.ValueKind == JsonValueKind.Object)
        {
            return resolvedWriteValue.EnumerateObject().ToDictionary(property => property.Name, property => ConvertJsonElement(property.Value), StringComparer.Ordinal);
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    private static FieldConfig BuildFieldConfig(string key, JsonElement property, JsonElement renderData, JsonElement data)
    {
        string? title = property.TryGetProperty("title", out JsonElement titleElement) ? RenderScalar(titleElement) : key;
        string? type = property.TryGetProperty("type", out JsonElement typeElement) ? RenderScalar(typeElement) : null;
        string? format = property.TryGetProperty("format", out JsonElement formatElement) ? RenderScalar(formatElement) : null;
        string? placeholder = property.TryGetProperty("placeholder", out JsonElement placeholderElement) ? RenderScalar(placeholderElement) : null;
        bool isRequired = renderData.ValueKind == JsonValueKind.Object
            && renderData.TryGetProperty("fields", out JsonElement fieldsElement)
            && fieldsElement.ValueKind == JsonValueKind.Object
            && fieldsElement.TryGetProperty("required", out JsonElement requiredElement)
            && requiredElement.ValueKind == JsonValueKind.Array
            && requiredElement.EnumerateArray().Any(item => string.Equals(RenderScalar(item), key, StringComparison.Ordinal));

        var options = new List<ChoiceOption>();
        if (property.TryGetProperty("enum", out JsonElement enumElement) && enumElement.ValueKind == JsonValueKind.Array)
        {
            options.AddRange(enumElement.EnumerateArray().Select(option =>
            {
                string value = RenderScalar(option);
                return new ChoiceOption(value, value);
            }));
        }
        else if (data.ValueKind == JsonValueKind.Array)
        {
            options.AddRange(BuildChoiceOptions(data));
        }
        else if (data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty(key, out JsonElement dataOptions) && dataOptions.ValueKind == JsonValueKind.Array)
            {
                options.AddRange(BuildChoiceOptions(dataOptions));
            }
            else if (data.TryGetProperty("options", out JsonElement optionsElement) && optionsElement.ValueKind == JsonValueKind.Array)
            {
                options.AddRange(BuildChoiceOptions(optionsElement));
            }
        }

        string? actionLabel = renderData.ValueKind == JsonValueKind.Object && renderData.TryGetProperty("actionLabel", out JsonElement actionLabelElement)
            ? RenderScalar(actionLabelElement)
            : null;
        string? discardLabel = renderData.ValueKind == JsonValueKind.Object && renderData.TryGetProperty("discardLabel", out JsonElement discardLabelElement)
            ? RenderScalar(discardLabelElement)
            : null;
        string? saveLabel = renderData.ValueKind == JsonValueKind.Object && renderData.TryGetProperty("saveLabel", out JsonElement saveLabelElement)
            ? RenderScalar(saveLabelElement)
            : null;

        return new FieldConfig(key, title, type, format, placeholder, isRequired, options, actionLabel, discardLabel, saveLabel);
    }

    private static IReadOnlyList<ChoiceOption> BuildChoiceOptions(JsonElement optionsElement)
    {
        var options = new List<ChoiceOption>();
        foreach (JsonElement option in optionsElement.EnumerateArray())
        {
            if (option.ValueKind == JsonValueKind.Object)
            {
                string value = option.TryGetProperty("value", out JsonElement valueElement)
                    ? RenderScalar(valueElement)
                    : option.TryGetProperty("id", out JsonElement idElement)
                        ? RenderScalar(idElement)
                        : option.TryGetProperty("label", out JsonElement labelElement)
                            ? RenderScalar(labelElement)
                            : string.Empty;
                string label = option.TryGetProperty("label", out JsonElement optionLabelElement)
                    ? RenderScalar(optionLabelElement)
                    : option.TryGetProperty("title", out JsonElement titleElement)
                        ? RenderScalar(titleElement)
                        : value;
                options.Add(new ChoiceOption(value, label));
                continue;
            }

            string scalar = RenderScalar(option);
            options.Add(new ChoiceOption(scalar, scalar));
        }

        return options;
    }

    private static bool TryGetResolvedWriteValue(string rawRenderDefJson, out JsonElement resolvedWriteValue)
    {
        using JsonDocument renderDef = JsonDocument.Parse(rawRenderDefJson);
        JsonElement root = renderDef.RootElement;
        if (root.TryGetProperty("resolvedWriteValue", out JsonElement element))
        {
            resolvedWriteValue = CloneElement(element);
            return true;
        }

        resolvedWriteValue = default;
        return false;
    }

    private static TableEditorConfig GetTableEditorConfig(string rawRenderDefJson, JsonElement data)
    {
        using JsonDocument renderDef = JsonDocument.Parse(rawRenderDefJson);
        JsonElement root = renderDef.RootElement;
        JsonElement renderData = root.TryGetProperty("data", out JsonElement dataElement) ? dataElement : default;

        var columns = new List<string>();
        if (renderData.ValueKind == JsonValueKind.Object
            && renderData.TryGetProperty("columns", out JsonElement columnsElement)
            && columnsElement.ValueKind == JsonValueKind.Array)
        {
            columns.AddRange(columnsElement.EnumerateArray().Select(RenderScalar).Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        foreach (string column in GetObjectColumns(MergeRows(data)))
        {
            if (!columns.Contains(column, StringComparer.Ordinal))
            {
                columns.Add(column);
            }
        }

        var columnTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (renderData.ValueKind == JsonValueKind.Object
            && renderData.TryGetProperty("schema", out JsonElement schemaElement)
            && schemaElement.ValueKind == JsonValueKind.Object
            && schemaElement.TryGetProperty("properties", out JsonElement propertiesElement)
            && propertiesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in propertiesElement.EnumerateObject())
            {
                if (property.Value.TryGetProperty("type", out JsonElement typeElement) && typeElement.ValueKind == JsonValueKind.String)
                {
                    columnTypes[property.Name] = RenderScalar(typeElement);
                }
            }
        }

        bool canAdd = !(renderData.ValueKind == JsonValueKind.Object
            && renderData.TryGetProperty("addRow", out JsonElement addRowElement)
            && addRowElement.ValueKind == JsonValueKind.False);
        bool canDelete = !(renderData.ValueKind == JsonValueKind.Object
            && renderData.TryGetProperty("deleteRow", out JsonElement deleteRowElement)
            && deleteRowElement.ValueKind == JsonValueKind.False);
        string? placeholder = renderData.ValueKind == JsonValueKind.Object
            && renderData.TryGetProperty("placeholder", out JsonElement placeholderElement)
            ? RenderScalar(placeholderElement)
            : null;

        return new TableEditorConfig(columns, columnTypes, canAdd, canDelete, placeholder);
    }

    private static List<Dictionary<string, object?>> MergeRows(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Array)
        {
            return new List<Dictionary<string, object?>>();
        }

        var rows = new List<Dictionary<string, object?>>();
        foreach (JsonElement item in data.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                rows.Add(item.EnumerateObject().ToDictionary(property => property.Name, property => ConvertJsonElement(property.Value), StringComparer.Ordinal));
            }
            else
            {
                rows.Add(new Dictionary<string, object?> { ["value"] = ConvertJsonElement(item) });
            }
        }

        return rows;
    }

    private static List<Dictionary<string, object?>> CloneRows(IEnumerable<Dictionary<string, object?>> rows)
    {
        return rows.Select(row => row.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)).ToList();
    }

    private static IReadOnlyList<string> GetObjectColumns(IEnumerable<Dictionary<string, object?>> rows)
    {
        var columns = new List<string>();
        foreach (Dictionary<string, object?> row in rows)
        {
            foreach (string key in row.Keys)
            {
                if (!columns.Contains(key, StringComparer.Ordinal))
                {
                    columns.Add(key);
                }
            }
        }

        return columns;
    }

    private static void RenderEditableTableGrid(
        Grid tableGrid,
        IReadOnlyList<Dictionary<string, object?>> rows,
        TableEditorConfig config,
        Action<List<Dictionary<string, object?>>> updateRows)
    {
        tableGrid.Children.Clear();
        tableGrid.RowDefinitions.Clear();
        tableGrid.ColumnDefinitions.Clear();

        int deleteColumnOffset = config.CanDelete ? 1 : 0;
        foreach (string _ in config.Columns)
        {
            tableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        if (config.CanDelete)
        {
            tableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int columnIndex = 0; columnIndex < config.Columns.Count; columnIndex += 1)
        {
            tableGrid.Children.Add(BuildTableCell(config.Columns[columnIndex], 0, columnIndex, true));
        }

        if (rows.Count == 0)
        {
            tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var empty = BuildTableCell(config.Placeholder ?? "No rows", 1, 0, false);
            Grid.SetColumnSpan(empty, System.Math.Max(1, config.Columns.Count + deleteColumnOffset));
            tableGrid.Children.Add(empty);
            return;
        }

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex += 1)
        {
            tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Dictionary<string, object?> row = rows[rowIndex];
            for (int columnIndex = 0; columnIndex < config.Columns.Count; columnIndex += 1)
            {
                string column = config.Columns[columnIndex];
                string? declaredType = config.ColumnTypes.TryGetValue(column, out string? typeValue) ? typeValue : null;
                bool isNumber = string.Equals(declaredType, "number", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(declaredType, "integer", StringComparison.OrdinalIgnoreCase)
                    || row.TryGetValue(column, out object? currentValue) && currentValue is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;
                var editor = new TextBox
                {
                    Text = row.TryGetValue(column, out object? value) && value is not null ? value.ToString() ?? string.Empty : string.Empty,
                    InputScope = isNumber ? BuildNumberInputScope() : BuildTextInputScope(),
                    MinWidth = 110,
                    Margin = new Thickness(0, 0, 4, 4)
                };
                int capturedRowIndex = rowIndex;
                editor.TextChanged += (_, _) =>
                {
                    var nextRows = CloneRows(rows);
                    nextRows[capturedRowIndex][column] = isNumber ? ConvertNumericCellValue(declaredType, editor.Text) : editor.Text;
                    updateRows(nextRows);
                };
                Grid.SetRow(editor, rowIndex + 1);
                Grid.SetColumn(editor, columnIndex);
                tableGrid.Children.Add(editor);
            }

            if (config.CanDelete)
            {
                var removeButton = new Button { Content = "×", Margin = new Thickness(0, 0, 0, 4) };
                int capturedRowIndex = rowIndex;
                removeButton.Click += (_, _) =>
                {
                    var nextRows = CloneRows(rows);
                    nextRows.RemoveAt(capturedRowIndex);
                    updateRows(nextRows);
                };
                Grid.SetRow(removeButton, rowIndex + 1);
                Grid.SetColumn(removeButton, config.Columns.Count);
                tableGrid.Children.Add(removeButton);
            }
        }
    }

    private static Border BuildTableCell(string text, int row, int column, bool isHeader)
    {
        var border = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x22, 0x77, 0x8C, 0xA6)),
            Padding = new Thickness(4, 2, 4, 4),
            Child = new TextBlock
            {
                Text = text,
                FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
                Opacity = isHeader ? 0.82 : 0.6,
                TextWrapping = TextWrapping.WrapWholeWords
            }
        };
        Grid.SetRow(border, row);
        Grid.SetColumn(border, column);
        return border;
    }

    private static InputScope BuildNumberInputScope()
    {
        var scope = new InputScope();
        scope.Names.Add(new InputScopeName { NameValue = InputScopeNameValue.Number });
        return scope;
    }

    private static InputScope BuildTextInputScope()
    {
        var scope = new InputScope();
        scope.Names.Add(new InputScopeName { NameValue = InputScopeNameValue.Default });
        return scope;
    }

    private static object ConvertNumericCellValue(string? declaredType, string? rawValue)
    {
        string value = rawValue ?? string.Empty;
        if (string.Equals(declaredType, "integer", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(value, out long parsedInteger) ? parsedInteger : 0L;
        }

        return double.TryParse(value, out double parsedNumber) ? parsedNumber : 0d;
    }

    private static object? ConvertFieldText(FieldConfig field, string? rawValue)
    {
        string value = rawValue ?? string.Empty;
        if (string.Equals(field.Type, "number", StringComparison.OrdinalIgnoreCase))
        {
            return double.TryParse(value, out double parsedNumber) ? parsedNumber : 0d;
        }

        if (string.Equals(field.Type, "integer", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(value, out long parsedInteger) ? parsedInteger : 0L;
        }

        return value;
    }

    private static string FormatFieldValue(FieldConfig field, object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        string text = value.ToString() ?? string.Empty;
        if (string.Equals(field.Format, "date", StringComparison.OrdinalIgnoreCase) && text.Length >= 10)
        {
            return text[..10];
        }

        return text;
    }

    private static InputScope BuildInputScope(FieldConfig field)
    {
        var scope = new InputScope();
        InputScopeNameValue scopeName = InputScopeNameValue.Default;
        if (string.Equals(field.Type, "number", StringComparison.OrdinalIgnoreCase)
            || string.Equals(field.Type, "integer", StringComparison.OrdinalIgnoreCase))
        {
            scopeName = InputScopeNameValue.Number;
        }

        scope.Names.Add(new InputScopeName { NameValue = scopeName });
        return scope;
    }

    private static void SetComboSelection(ComboBox combo, string value)
    {
        foreach (object item in combo.Items)
        {
            if (item is ComboBoxItem comboItem
                && string.Equals(comboItem.Tag?.ToString() ?? string.Empty, value, StringComparison.Ordinal))
            {
                combo.SelectedItem = comboItem;
                return;
            }
        }

        combo.SelectedItem = null;
    }

    private static bool CoerceBoolean(object? value)
    {
        return value switch
        {
            bool booleanValue => booleanValue,
            string stringValue when bool.TryParse(stringValue, out bool parsedBoolean) => parsedBoolean,
            _ => false,
        };
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        return JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    }

    private static JsonElement CloneElement(JsonElement element)
    {
        return JsonDocument.Parse(element.GetRawText()).RootElement.Clone();
    }

    private static object BuildEditorSaveValue(string? writeTo, string? fieldKey, object? nextValue)
    {
        if (string.Equals(writeTo, "card_data", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(fieldKey))
        {
            return new Dictionary<string, object?> { [fieldKey] = nextValue };
        }

        return nextValue ?? string.Empty;
    }

    private static string? ResolveWriteTo(string rawRenderDefJson)
    {
        using JsonDocument renderDef = JsonDocument.Parse(rawRenderDefJson);
        JsonElement root = renderDef.RootElement;
        JsonElement renderData = root.TryGetProperty("data", out JsonElement dataElement) ? dataElement : default;
        return renderData.ValueKind == JsonValueKind.Object && renderData.TryGetProperty("writeTo", out JsonElement writeToElement)
            ? RenderScalar(writeToElement)
            : null;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(property => property.Name, property => ConvertJsonElement(property.Value), StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out long intValue) => intValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };
    }

    private static TextBlock BuildMutedText(string text)
    {
        return new TextBlock { Text = text, Opacity = 0.6, TextWrapping = TextWrapping.WrapWholeWords };
    }

    private static string RenderScalar(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            JsonValueKind.Undefined => string.Empty,
            _ => value.GetRawText()
        };
    }
}
