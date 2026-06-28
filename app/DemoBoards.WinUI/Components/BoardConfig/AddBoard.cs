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
    Action<IReadOnlyDictionary<string, object?>>? OnSubmit = null,
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

        // Memoize the field spec to avoid rebuilding on every render
        var fieldSpec = UseMemo(() =>
        {
            // Map template options from {key, label} to {value, label} for SelectControl
            var templateSelectOptions = new List<object?>();
            foreach (var opt in templateOptions)
            {
                if (opt is IDictionary<string, object?> optDict)
                {
                    templateSelectOptions.Add(new Dictionary<string, object?>
                    {
                        ["value"] = optDict.TryGetValue("key", out var k) ? k : "",
                        ["label"] = optDict.TryGetValue("label", out var l) ? l : "",
                    });
                }
                else if (opt is System.Collections.Generic.KeyValuePair<string, object?> kvp)
                {
                    // Handle if passed as kvp
                    templateSelectOptions.Add(new Dictionary<string, object?>
                    {
                        ["value"] = kvp.Key,
                        ["label"] = kvp.Value,
                    });
                }
            }

            var spec = new Dictionary<string, object?>
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
                            ["description"] = Props.LoadingTemplates
                                ? "Loading templates…"
                                : "If selected, the template cards will be ingested into the newly created board.",
                            ["options"] = BuildTemplateOptions(templateOptions)
                        }
                    },
                    ["required"] = RequiredFields
                }
            };

            // Validators for board ID format
            var validators = new List<object?>
            {
                new object?[] { "data.boardId = '' or ($match(data.boardId, /^[a-z0-9-]+$/) ? true : false)", "Board ID may only contain lowercase letters, numbers, and hyphens." },
                new object?[] { "data.boardId = '' or $length(data.boardId) >= 3", "Board ID must be at least 3 characters." }
            };

            spec["validators"] = validators;
            return spec;
        }, templateOptions.Count);

        return VStack(12,
            TextBlock("Create New Board")
                .FontSize(18)
                .Bold(),
            
            Component<Form, FormProps>(new FormProps(
                Spec: fieldSpec,
                BaseValues: CreateEmptyBoardForm(),
                OnSave: values => Props.OnSubmit?.Invoke(values),
                OnCancel: Props.OnClose,
                SubmitLabel: "Create Board",
                Submitting: Props.Submitting,
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
