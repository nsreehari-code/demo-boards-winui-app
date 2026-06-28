using System;
using System.Collections.Generic;
using System.Linq;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Controls.Shared;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls.Registry.BoardConfig;

public sealed record ThemeVisualSettingsProps(
    string ThemePackId = "",
    IReadOnlyList<string>? ThemePackOptions = null,
    bool Saving = false,
    string ErrorMessage = "",
    string SuccessMessage = "",
    Action<string>? OnSave = null);

public sealed class ThemeVisualSettings : Component<ThemeVisualSettingsProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        string initialThemePackId = string.IsNullOrWhiteSpace(Props.ThemePackId)
            ? BoardTheme.DefaultThemePackId
            : Props.ThemePackId.Trim();

        var (draft, setDraft) = UseState<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>
        {
            ["themePackId"] = initialThemePackId
        });

        UseEffect(() =>
        {
            setDraft(new Dictionary<string, object?>
            {
                ["themePackId"] = initialThemePackId
            });
            return static () => { };
        }, initialThemePackId);

        var fieldSpec = UseMemo(() => new Dictionary<string, object?>
        {
            ["fields"] = new Dictionary<string, object?>
            {
                ["properties"] = new Dictionary<string, object?>
                {
                    ["themePackId"] = new Dictionary<string, object?>
                    {
                        ["title"] = "Theme Pack",
                        ["placeholder"] = BoardTheme.DefaultThemePackId,
                        ["options"] = BuildOptions(Props.ThemePackOptions)
                    }
                },
                ["required"] = new[] { "themePackId" }
            }
        }, Props.ThemePackOptions?.Count ?? 0);

        var sections = new List<Element>
        {
            TextBlock("Theme")
                .FontSize(16)
                .Bold(),
            TextBlock("Persist board theme through the visuals layout blob.")
                .FontSize(12)
                .Opacity(0.68),
            Component<Form, FormProps>(new FormProps(
                Spec: fieldSpec,
                BaseValues: draft,
                OnSave: values =>
                {
                    string nextThemePackId = values.TryGetValue("themePackId", out var value)
                        ? BoardTheme.NormalizeThemePackId(value?.ToString())
                        : BoardTheme.DefaultThemePackId;

                    setDraft(new Dictionary<string, object?>
                    {
                        ["themePackId"] = nextThemePackId
                    });
                    Props.OnSave?.Invoke(nextThemePackId);
                },
                SubmitLabel: "Save theme",
                Submitting: Props.Saving,
                CanSubmit: Props.OnSave is not null,
                Error: string.IsNullOrWhiteSpace(Props.ErrorMessage) ? string.Empty : $"Save failed: {Props.ErrorMessage}"
            ))
        };

        if (Props.ThemePackOptions is { Count: > 0 })
        {
            sections.Add(TextBlock($"Available theme packs: {string.Join(", ", Props.ThemePackOptions)}")
                .FontSize(12)
                .Opacity(0.68));
        }

        if (!string.IsNullOrWhiteSpace(Props.SuccessMessage))
        {
            sections.Add(TextBlock(Props.SuccessMessage)
                .FontSize(12)
                .Foreground(theme.StatusSuccess));
        }

        return VStack(12, sections.ToArray())
            .Set(stack => stack.Padding = new(12));
    }

    private static object?[] BuildOptions(IReadOnlyList<string>? options)
    {
        if (options is not { Count: > 0 })
        {
            return Array.Empty<object?>();
        }

        return options
            .Select(option => (object?)new Dictionary<string, object?>
            {
                ["value"] = option,
                ["label"] = option,
            })
            .ToArray();
    }
}