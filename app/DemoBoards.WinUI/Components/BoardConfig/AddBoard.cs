using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI.Controls.Shared;

namespace DemoBoards_WinUI.Controls.Registry.BoardConfig;

/// <summary>
/// Mirrors <c>AddBoard.jsx</c> — form for creating a new board with all required fields.
/// Includes board ID validation and optional template selection.
/// Props match frontend exactly: onClose, onSubmit, templateOptions ([{key, label}]), loadingTemplates, submitting, errorMessage.
/// </summary>
public sealed record AddBoardProps(
    Action? OnClose = null,
    Func<IReadOnlyDictionary<string, object?>, System.Threading.Tasks.Task>? OnSubmit = null,
    IReadOnlyList<object?>? TemplateOptions = null,
    bool LoadingTemplates = false,
    bool Submitting = false,
    string ErrorMessage = "");

public sealed class AddBoard : Component<AddBoardProps>
{
    private static readonly string[] RequiredFields =
    {
        "boardId", "label", "pageTitle", "pageSubtitle", "ai",
        "aiWorkspaceTemplate", "uiTemplate", "refsTemplate"
    };

    public override Element Render()
    {
        var templateOptions = Props.TemplateOptions ?? Array.Empty<object?>();

        var fieldSpec = new Dictionary<string, object?>
        {
            ["fields"] = new Dictionary<string, object?>
            {
                ["properties"] = new Dictionary<string, object?>
                {
                    ["boardId"] = new Dictionary<string, object?>
                    {
                        ["title"] = "Board Id",
                        ["placeholder"] = "live-test-frontend"
                    },
                    ["label"] = new Dictionary<string, object?>
                    {
                        ["title"] = "Label",
                        ["placeholder"] = "Live Test"
                    },
                    ["pageTitle"] = new Dictionary<string, object?>
                    {
                        ["title"] = "Page Title",
                        ["placeholder"] = "Live Test"
                    },
                    ["pageSubtitle"] = new Dictionary<string, object?>
                    {
                        ["title"] = "Page Subtitle",
                        ["placeholder"] = "Live operational intelligence for agent workflows"
                    },
                    ["ai"] = new Dictionary<string, object?>
                    {
                        ["title"] = "AI",
                        ["placeholder"] = "copilot"
                    },
                    ["aiWorkspaceTemplate"] = new Dictionary<string, object?>
                    {
                        ["title"] = "AI Workspace Template",
                        ["placeholder"] = "default"
                    },
                    ["uiTemplate"] = new Dictionary<string, object?>
                    {
                        ["title"] = "UI Template",
                        ["placeholder"] = "default"
                    },
                    ["refsTemplate"] = new Dictionary<string, object?>
                    {
                        ["title"] = "Refs Template",
                        ["placeholder"] = "localfs-default"
                    },
                    ["templateKey"] = new Dictionary<string, object?>
                    {
                        ["title"] = "Card Template (optional)",
                        ["placeholder"] = "No template",
                        ["disabled"] = Props.LoadingTemplates,
                        ["options"] = BuildTemplateOptions(templateOptions),
                        ["description"] = Props.LoadingTemplates
                            ? "Loading templates…"
                            : "If selected, the template cards will be ingested into the newly created board."
                    }
                },
                ["required"] = RequiredFields
            },
            ["validators"] = new List<object?>
            {
                new object?[] { "data.boardId = '' or ($match(data.boardId, /^[a-z0-9-]+$/) ? true : false)", "Board Id may only contain lowercase letters, numbers, and hyphens." },
                new object?[] { "data.boardId = '' or $length(data.boardId) >= 3", "Board Id must be at least 3 characters." }
            }
        };

        async System.Threading.Tasks.Task HandleSaveAsync(IReadOnlyDictionary<string, object?> values)
        {
            if (Props.OnSubmit == null)
            {
                return;
            }

            var normalized = new Dictionary<string, object?>
            {
                ["boardId"] = Normalize(values, "boardId"),
                ["label"] = Normalize(values, "label"),
                ["pageTitle"] = Normalize(values, "pageTitle"),
                ["pageSubtitle"] = Normalize(values, "pageSubtitle"),
                ["ai"] = Normalize(values, "ai"),
                ["aiWorkspaceTemplate"] = Normalize(values, "aiWorkspaceTemplate"),
                ["uiTemplate"] = Normalize(values, "uiTemplate"),
                ["refsTemplate"] = Normalize(values, "refsTemplate"),
                ["templateKey"] = Normalize(values, "templateKey"),
            };

            try
            {
                await Props.OnSubmit(normalized);
            }
            catch
            {
                // Parent surfaces request failures through ErrorMessage.
            }
        }

        return VStack(12,
            Component<Form, FormProps>(new FormProps(
                Spec: fieldSpec,
                BaseValues: CreateEmptyBoardForm(),
                OnSave: values => _ = HandleSaveAsync(values),
                OnCancel: Props.OnClose,
                SubmitLabel: Props.Submitting ? "Adding…" : "Add board",
                Submitting: Props.Submitting,
                AlwaysShowActions: true,
                Error: Props.ErrorMessage
            ))
        )
        .Set(stack => stack.Padding = new(16))
        .Set(stack => stack.Spacing = 16);
    }

    /// <summary>
    /// Creates empty initial form values for AddBoard.
    /// Mirrors frontend's createEmptyAddBoardForm().
    /// </summary>
    public static IReadOnlyDictionary<string, object?> CreateEmptyBoardForm()
    {
        return new Dictionary<string, object?>
        {
            ["boardId"] = "",
            ["label"] = "",
            ["pageTitle"] = "",
            ["pageSubtitle"] = "",
            ["ai"] = "copilot",
            ["aiWorkspaceTemplate"] = "default",
            ["uiTemplate"] = "default",
            ["refsTemplate"] = "localfs-default",
            ["templateKey"] = ""
        };
    }

    private static string Normalize(IReadOnlyDictionary<string, object?> values, string key)
    {
        return values.TryGetValue(key, out object? value)
            ? value?.ToString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static object?[] BuildTemplateOptions(IReadOnlyList<object?> options)
    {
        var result = new List<object?>();
        foreach (var opt in options)
        {
            if (opt is IDictionary<string, object?> optDict &&
                optDict.TryGetValue("key", out var key) &&
                optDict.TryGetValue("label", out var label))
            {
                result.Add(new Dictionary<string, object?>
                {
                    ["value"] = key,
                    ["label"] = label,
                });
            }
        }
        return result.ToArray();
    }
}
