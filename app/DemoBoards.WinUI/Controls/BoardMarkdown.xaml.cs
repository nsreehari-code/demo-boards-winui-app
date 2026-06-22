using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Microsoft.UI.Xaml.Controls;

namespace DemoBoards_WinUI.Controls;

public sealed partial class BoardMarkdown : UserControl
{
    private static readonly Regex FootnoteLinkPattern = new("\\s*\\[(\\d+)\\]\\((https?:\\/\\/[^)]+)\\)", RegexOptions.Compiled);
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public BoardMarkdown()
    {
        InitializeComponent();
    }

    public void Render(string? text, string? className = null, string? style = null)
    {
        string normalized = (text ?? string.Empty).Trim();
        normalized = FootnoteLinkPattern.Replace(normalized, string.Empty);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            MarkdownWebView.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            EmptyStateText.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            return;
        }

        EmptyStateText.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        MarkdownWebView.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        string htmlBody = Markdig.Markdown.ToHtml(normalized, MarkdownPipeline);
        MarkdownWebView.NavigateToString(BuildDocument(htmlBody, className, style));
    }

    private static string BuildDocument(string htmlBody, string? className, string? style)
    {
        string encodedClassName = WebUtility.HtmlEncode(className ?? string.Empty);
        string normalizedStyle = NormalizeStyle(style);
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html><head><meta charset=\"utf-8\" />");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        builder.AppendLine("<style>");
        builder.AppendLine(BoardTheme.BuildWebViewCssVariables());
        builder.AppendLine("body { font-family: 'Segoe UI', sans-serif; font-size: 13px; line-height: 1.45; color: var(--color-text); background: transparent; margin: 0; padding: 0; overflow-wrap: anywhere; }");
        builder.AppendLine("a { color: var(--color-accent-strong); text-decoration: none; }");
        builder.AppendLine("a:hover { text-decoration: underline; }");
        builder.AppendLine("img { max-width: 100%; border-radius: 8px; border: 1px solid color-mix(in srgb, var(--color-border) 90%, transparent); margin: 8px 0; }");
        builder.AppendLine("pre, code { font-family: Consolas, 'Courier New', monospace; }");
        builder.AppendLine("pre { background: color-mix(in srgb, var(--color-surface-strong) 94%, transparent); padding: 10px; border-radius: 8px; overflow-x: auto; }");
        builder.AppendLine("table { width: 100%; border-collapse: collapse; margin: 8px 0; }");
        builder.AppendLine("th, td { border: 1px solid color-mix(in srgb, var(--color-border) 100%, transparent); padding: 6px 8px; text-align: left; vertical-align: top; }");
        builder.AppendLine("thead { background: color-mix(in srgb, var(--color-surface-strong) 82%, transparent); }");
        builder.AppendLine("blockquote { margin: 8px 0; padding-left: 12px; border-left: 3px solid color-mix(in srgb, var(--color-accent-strong) 70%, transparent); opacity: 0.88; }");
        builder.AppendLine("ul, ol { padding-left: 20px; }");
        builder.AppendLine("hr { border: 0; border-top: 1px solid color-mix(in srgb, var(--color-border) 100%, transparent); margin: 12px 0; }");
        builder.AppendLine("</style></head><body>");
        builder.Append("<div class=\"");
        builder.Append(encodedClassName);
        builder.Append("\" style=\"");
        builder.Append(WebUtility.HtmlEncode(normalizedStyle));
        builder.AppendLine("\">");
        builder.AppendLine(htmlBody);
        builder.AppendLine("</div>");
        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    private static string NormalizeStyle(string? style)
    {
        if (string.IsNullOrWhiteSpace(style))
        {
            return string.Empty;
        }

        var declarations = style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var normalized = new List<string>();
        foreach (string declaration in declarations)
        {
            string[] parts = declaration.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            string property = parts[0].ToLowerInvariant();
            string value = parts[1];
            normalized.Add(property switch
            {
                "color" => $"color: {value}",
                "background" or "background-color" => $"background-color: {value}",
                "font-size" => $"font-size: {NormalizeCssLength(value)}",
                "padding" => $"padding: {NormalizeCssLength(value)}",
                "margin" => $"margin: {NormalizeCssLength(value)}",
                _ => string.Empty
            });
        }

        return string.Join("; ", normalized.Where(item => !string.IsNullOrWhiteSpace(item)));
    }

    private static string NormalizeCssLength(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
        {
            return $"{number.ToString(CultureInfo.InvariantCulture)}px";
        }

        return value;
    }
}
