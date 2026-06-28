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
    string BoardId = "",
    int TransportMode = 0,
    Func<string, System.Threading.Tasks.Task<object?>>? LoadBoard = null,
    Func<string, IReadOnlyDictionary<string, object?>, System.Threading.Tasks.Task<object?>>? OnSave = null);

public sealed class EditPageDetails : Component<EditPageDetailsProps>
{
    /// <summary>
    /// Mirrors frontend's toPageDetailsDraft function.
    /// Extracts board metadata into form field values.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> ExtractPageDetailsDraft(object? board)
    {
        var draft = new Dictionary<string, object?>
        {
            ["pageTitle"] = "",
            ["pageSubtitle"] = "",
            ["refreshAllIntervalMinutes"] = "60",
            ["uiTemplate"] = "default"
        };

        if (board is IReadOnlyDictionary<string, object?> boardDict)
        {
            // Extract from metadata
            if (boardDict.TryGetValue("metadata", out var metaObj) && metaObj is IReadOnlyDictionary<string, object?> metadata)
            {
                if (metadata.TryGetValue("pageTitle", out var pt) && pt is string pageTitle && !string.IsNullOrWhiteSpace(pageTitle))
                {
                    draft["pageTitle"] = pageTitle.Trim();
                }
                if (metadata.TryGetValue("pageSubtitle", out var ps))
                {
                    draft["pageSubtitle"] = ps?.ToString() ?? "";
                }
                
                // Convert refreshAllIntervalSeconds to refreshAllIntervalMinutes
                if (metadata.TryGetValue("refreshAllIntervalSeconds", out var refSec) &&
                    int.TryParse(refSec?.ToString(), out int seconds) && seconds > 0)
                {
                    int minutes = Math.Max(1, (int)Math.Round(seconds / 60.0));
                    draft["refreshAllIntervalMinutes"] = minutes.ToString();
                }
            }

            // Fall back to board label for pageTitle if metadata didn't have it
            if (string.IsNullOrEmpty(draft["pageTitle"]?.ToString()) && 
                boardDict.TryGetValue("label", out var label) && label is string boardLabel)
            {
                draft["pageTitle"] = boardLabel.Trim();
            }

            // Extract uiTemplate
            if (boardDict.TryGetValue("uiTemplate", out var ut))
            {
                draft["uiTemplate"] = ut?.ToString() ?? "default";
            }
        }

        return draft;
    }

    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        var (draft, setDraft) = UseState<IReadOnlyDictionary<string, object?>>(
            ExtractPageDetailsDraft(null));

        var (loading, setLoading) = UseState(false);
        var (saving, setSaving) = UseState(false);
        var (errorMessage, setErrorMessage) = UseState("");
        var (successMessage, setSuccessMessage) = UseState("");

        // Mirrors frontend's useEffect for loading board data
        UseEffect(() =>
        {
            if (Props.TransportMode != EditPageDetailsConstants.BOARD_TRANSPORT_MODE_SERVER_URL || string.IsNullOrEmpty(Props.BoardId) || Props.LoadBoard == null)
            {
                setDraft(ExtractPageDetailsDraft(null));
                setLoading(false);
                setErrorMessage("");
                setSuccessMessage("");
                return null;
            }

            setLoading(true);
            setErrorMessage("");
            setSuccessMessage("");

            // Fire async load
            var boardId = Props.BoardId;
            var loadFunc = Props.LoadBoard;
            
            #pragma warning disable CS4014
            loadFunc(boardId).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    setDraft(ExtractPageDetailsDraft(task.Result));
                }
                else if (task.IsFaulted && task.Exception != null)
                {
                    setErrorMessage(task.Exception.InnerException?.Message ?? "Failed to load board");
                    setDraft(ExtractPageDetailsDraft(null));
                }
                setLoading(false);
            });
            #pragma warning restore CS4014

            return null;
        }, $"{Props.BoardId}:{Props.TransportMode}:{Props.LoadBoard?.GetHashCode() ?? 0}");

        bool fieldsDisabled = loading || Props.TransportMode != EditPageDetailsConstants.BOARD_TRANSPORT_MODE_SERVER_URL || string.IsNullOrEmpty(Props.BoardId);

        // Memoize field spec
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
                            ["placeholder"] = "Live",
                            ["disabled"] = fieldsDisabled
                        },
                        ["pageSubtitle"] = new Dictionary<string, object?>
                        {
                            ["title"] = "Page Subtitle",
                            ["placeholder"] = "Live operational intelligence for agent workflows",
                            ["disabled"] = fieldsDisabled
                        },
                        ["refreshAllIntervalMinutes"] = new Dictionary<string, object?>
                        {
                            ["title"] = "Refresh Interval (minutes)",
                            ["type"] = "integer",
                            ["minimum"] = 1,
                            ["placeholder"] = "30",
                            ["disabled"] = fieldsDisabled
                        }
                    },
                    ["required"] = new[] { "pageTitle", "pageSubtitle", "refreshAllIntervalMinutes" }
                }
            };
            return spec;
        }, fieldsDisabled);

        var sections = new List<Element>
        {
            TextBlock("Edit Page Details")
                .FontSize(16)
                .Bold()
        };

        if (loading)
        {
            sections.Add(TextBlock("Loading board configuration...")
                .FontSize(12)
                .Opacity(0.6));
        }
        else if (fieldsDisabled)
        {
            sections.Add(TextBlock("Board not available in this mode")
                .FontSize(12)
                .Opacity(0.6)
                .Foreground(theme.StatusWarning));
        }
        else
        {
            sections.Add(
                Component<Form, FormProps>(new FormProps(
                    Spec: fieldSpec,
                    BaseValues: draft,
                    OnSave: values =>
                    {
                        if (Props.OnSave != null)
                        {
                            setSaving(true);
                            setErrorMessage("");
                            setSuccessMessage("");
                            
                            // Prepare values for save
                            var nextValues = new Dictionary<string, object?>
                            {
                                ["pageTitle"] = values.TryGetValue("pageTitle", out var pt) ? pt?.ToString()?.Trim() : "",
                                ["pageSubtitle"] = values.TryGetValue("pageSubtitle", out var ps) ? ps?.ToString()?.Trim() : "",
                                ["refreshAllIntervalMinutes"] = values.TryGetValue("refreshAllIntervalMinutes", out var rm) ? rm?.ToString()?.Trim() : "",
                                ["uiTemplate"] = values.TryGetValue("uiTemplate", out var ut) ? ut?.ToString()?.Trim() : ""
                            };

                            var boardId = Props.BoardId;
                            var saveFunc = Props.OnSave;

                            #pragma warning disable CS4014
                            saveFunc(boardId, nextValues).ContinueWith(task =>
                            {
                                if (task.IsCompletedSuccessfully)
                                {
                                    setDraft(ExtractPageDetailsDraft(task.Result));
                                    setSuccessMessage("Saved.");
                                }
                                else if (task.IsFaulted && task.Exception != null)
                                {
                                    setErrorMessage(task.Exception.InnerException?.Message ?? "Save failed");
                                }
                                setSaving(false);
                            });
                            #pragma warning restore CS4014
                        }
                    },
                    OnCancel: () => setDraft(ExtractPageDetailsDraft(draft)),
                    SubmitLabel: "Save",
                    Submitting: saving,
                    Error: errorMessage
                )));
        }

        if (!string.IsNullOrEmpty(successMessage))
        {
            sections.Add(TextBlock(successMessage)
                .FontSize(12)
                .Foreground(theme.StatusSuccess));
        }

        return VStack(12, sections.ToArray())
            .Set(stack => stack.Padding = new(12));
    }
}
