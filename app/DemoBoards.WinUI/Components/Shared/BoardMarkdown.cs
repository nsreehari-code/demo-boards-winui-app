using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>BoardMarkdown.jsx</c> — renders markdown text. <c>Text</c> may be a string or an object
/// exposing a <c>text</c> field. Citation links (<c>[n](https://…)</c>) and trailing whitespace are
/// stripped before rendering through the Reactor <c>Markdown</c> factory.
/// </summary>
public sealed record BoardMarkdownProps(object? Text = null);

public sealed class BoardMarkdown : Component<BoardMarkdownProps>
{
    private static readonly Regex CitationLink = new(@"\s*\[(\d+)\]\((https?://[^)]+)\)", RegexOptions.Compiled);

    public override Element Render()
    {
        _ = UseContext(AppThemeContext.Current);

        string source = Props.Text switch
        {
            string text => text,
            IReadOnlyDictionary<string, object?> map when map.TryGetValue("text", out object? value) => value?.ToString() ?? string.Empty,
            _ => Props.Text?.ToString() ?? string.Empty,
        };

        string normalized = Normalize(source);
        return string.IsNullOrWhiteSpace(normalized) ? Empty() : Markdown(normalized);
    }

    private static string Normalize(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        string stripped = CitationLink.Replace(input, string.Empty);
        IEnumerable<string> lines = stripped.Replace("\r\n", "\n").Split('\n').Select(line => line.TrimEnd());
        return string.Join("\n", lines).Trim();
    }
}
