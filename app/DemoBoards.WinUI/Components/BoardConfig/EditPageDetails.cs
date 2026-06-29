using System;
using System.Collections.Generic;
using DemoBoards_WinUI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI.Controls.Shared;

namespace DemoBoards_WinUI.Controls.Registry.BoardConfig;

/// <summary>
/// Transport mode constant matching frontend BOARD_TRANSPORT_MODE_SERVER_URL.
/// Indicates board is loaded from server URL (not other transports like file).
/// </summary>
public static class EditPageDetailsConstants
{
    public const int BOARD_TRANSPORT_MODE_SERVER_URL = 1;
}

/// <summary>
/// Mirrors <c>EditPageDetails.jsx</c> — form for editing board page title, subtitle, and refresh interval.
/// Component manages all state internally: draft, loading, saving, errorMessage, successMessage.
/// Props match frontend exactly: boardId, transportMode, loadBoard, onSave (input-only callbacks).
/// </summary>
public sealed record EditPageDetailsProps(
    string PageTitle = "",
    string PageSubtitle = "",
    string RefreshIntervalMinutes = "60",
    string CurrentUiTemplate = "default",
    IReadOnlyList<string>? UiTemplateOptions = null,
    bool Saving = false,
    string ErrorMessage = "",
    string SuccessMessage = "",
    Action<IReadOnlyDictionary<string, object?>>? OnSave = null);

public sealed class EditPageDetails : Component<EditPageDetailsProps>
{
    public static IReadOnlyDictionary<string, object?> CreateDraft(
        string? pageTitle,
        string? pageSubtitle,
        string? refreshIntervalMinutes,
        string? currentUiTemplate)
    {
        return new Dictionary<string, object?>
        {
            ["pageTitle"] = pageTitle?.Trim() ?? string.Empty,
            ["pageSubtitle"] = pageSubtitle?.Trim() ?? string.Empty,
            ["refreshAllIntervalMinutes"] = string.IsNullOrWhiteSpace(refreshIntervalMinutes) ? "60" : refreshIntervalMinutes.Trim(),
            ["uiTemplate"] = string.IsNullOrWhiteSpace(currentUiTemplate) ? "default" : currentUiTemplate
        };
    }

    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        var (draft, setDraft) = UseState<IReadOnlyDictionary<string, object?>>(
            CreateDraft(Props.PageTitle, Props.PageSubtitle, Props.RefreshIntervalMinutes, Props.CurrentUiTemplate));

        UseEffect(() =>
        {
            setDraft(CreateDraft(Props.PageTitle, Props.PageSubtitle, Props.RefreshIntervalMinutes, Props.CurrentUiTemplate));
            return static () => { };
        }, $"{Props.PageTitle}:{Props.PageSubtitle}:{Props.RefreshIntervalMinutes}:{Props.CurrentUiTemplate}");

        var fieldSpec = UseMemo(() =>
        {
            var spec = new Dictionary<string, object?>
            {
                ["fields"] = new Dictionary<string, object?>
                {
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["pageTitle"] = new Dictionary<string, object?>
                        {
                            ["title"] = "Page Title",
                            ["placeholder"] = "Live"
                        },
                        ["pageSubtitle"] = new Dictionary<string, object?>
                        {
                            ["title"] = "Page Subtitle",
                            ["placeholder"] = "Live operational intelligence for agent workflows"
                        },
                        ["refreshAllIntervalMinutes"] = new Dictionary<string, object?>
                        {
                            ["title"] = "Refresh Interval (minutes)",
                            ["type"] = "integer",
                            ["minimum"] = 1,
                            ["placeholder"] = "30"
                        },
                        ["uiTemplate"] = new Dictionary<string, object?>
                        {
                            ["title"] = "UI Template",
                            ["placeholder"] = "default",
                            ["options"] = BuildOptions(Props.UiTemplateOptions)
                        }
                    },
                    ["required"] = new[] { "pageTitle", "pageSubtitle", "refreshAllIntervalMinutes", "uiTemplate" }
                }
            };
            return spec;
        }, $"{Props.UiTemplateOptions?.Count ?? 0}");

        var sections = new List<Element>
        {
            TextBlock("Page Details")
                .FontSize(16)
                .Bold(),
            TextBlock("Edit the page title, subtitle, refresh cadence, and UI template without raw JSON editing.")
                .FontSize(12)
                .Opacity(0.68)
        };

        sections.Add(
            Component<Form, FormProps>(new FormProps(
                Spec: fieldSpec,
                BaseValues: draft,
                OnSave: values =>
                {
                    var nextValues = new Dictionary<string, object?>
                    {
                        ["pageTitle"] = values.TryGetValue("pageTitle", out var pt) ? pt?.ToString()?.Trim() : string.Empty,
                        ["pageSubtitle"] = values.TryGetValue("pageSubtitle", out var ps) ? ps?.ToString()?.Trim() : string.Empty,
                        ["refreshAllIntervalMinutes"] = values.TryGetValue("refreshAllIntervalMinutes", out var rm) ? rm?.ToString()?.Trim() : string.Empty,
                        ["uiTemplate"] = values.TryGetValue("uiTemplate", out var ut) ? ut?.ToString()?.Trim() : "default"
                    };

                    setDraft(nextValues);
                    Props.OnSave?.Invoke(nextValues);
                },
                SubmitLabel: "Save",
                Submitting: Props.Saving,
                CanSubmit: Props.OnSave is not null,
                Error: string.IsNullOrWhiteSpace(Props.ErrorMessage) ? string.Empty : $"Save failed: {Props.ErrorMessage}"
            )));

        if (Props.UiTemplateOptions is { Count: > 0 })
        {
            sections.Add(TextBlock($"Available UI templates: {string.Join(", ", Props.UiTemplateOptions)}")
                .FontSize(12)
                .Opacity(0.68));
        }

        if (!string.IsNullOrEmpty(Props.SuccessMessage))
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
