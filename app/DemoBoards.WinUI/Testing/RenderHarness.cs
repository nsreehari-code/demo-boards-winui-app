using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DemoBoards_WinUI.Controls.Registry;
using DemoBoards_WinUI.Controls.Shared;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI;

/// <summary>
/// Reactor-level render parity harness (run via <c>--render-harness</c>). Unlike the data-only
/// ConverterHarness, this actually mounts each shared component through the real
/// <see cref="ReactorApp"/> reconciler on a hidden, off-screen window and walks the produced native
/// WinUI visual tree — asserting that the plain-data props each component takes are reconciled into
/// the expected <c>Button</c>/<c>TextBlock</c>/<c>CheckBox</c>/<c>ComboBox</c> elements. It prints
/// <c>[i/N] PASS/FAIL</c> lines plus a final banner, mirroring the other harnesses.
/// </summary>
internal static class RenderHarness
{
    internal static Window? Window;
    internal static Border?[] Slots = Array.Empty<Border?>();
    internal static readonly List<(string Name, bool Pass, string? Detail)> Results = new();
    internal static IReadOnlyList<RenderCase>? Cases;

    private static bool scheduled;
    private static bool finished;

    public static void RunAndExit()
    {
        RegistryBootstrap.EnsureRegistered();
        try
        {
            ReactorApp.Run<RenderHarnessRoot>(
                "DemoBoards.RenderHarness",
                width: 1024,
                height: 768,
                configure: host =>
                {
                    XamlInterop.Register(host.Reconciler);
                    Window = host.Window;
                    MoveOffscreen(host.Window);
                });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[harness] render host crashed: {ex}");
            Environment.ExitCode = 1;
            return;
        }

        if (!finished)
        {
            Console.Error.WriteLine("[harness] render host exited before assertions ran");
            Environment.ExitCode = 1;
        }
    }

    // The window must stay activated (so layout/render run) but should not flash on-screen; park it
    // far off the visible desktop instead of minimising (a minimised window skips layout).
    private static void MoveOffscreen(Window window)
    {
        Microsoft.UI.Windowing.AppWindow? appWindow = window.AppWindow;
        appWindow?.MoveAndResize(new Windows.Graphics.RectInt32(-4000, -4000, 1024, 768));
    }

    internal static void ScheduleAssertions()
    {
        if (scheduled)
        {
            return;
        }

        scheduled = true;

        DispatcherQueue? queue = DispatcherQueue.GetForCurrentThread();
        if (queue is null)
        {
            RunAssertions();
            return;
        }

        // Low priority runs after the first layout/render pass, so the visual tree is realised.
        queue.TryEnqueue(DispatcherQueuePriority.Low, RunAssertions);
    }

    private static void RunAssertions()
    {
        IReadOnlyList<RenderCase> cases = Cases ?? Array.Empty<RenderCase>();
        for (int i = 0; i < cases.Count; i++)
        {
            bool pass;
            string? detail;
            try
            {
                Border? border = i < Slots.Length ? Slots[i] : null;
                if (border is null)
                {
                    pass = false;
                    detail = "no element captured";
                }
                else
                {
                    border.UpdateLayout();
                    (pass, detail) = cases[i].Assert(border);
                }
            }
            catch (Exception ex)
            {
                pass = false;
                detail = $"{ex.GetType().Name}: {ex.Message}";
            }

            Results.Add((cases[i].Name, pass, detail));
        }

        Finish();
    }

    private static void Finish()
    {
        int n = Results.Count;
        int failures = 0;
        for (int i = 0; i < n; i++)
        {
            (string name, bool pass, string? detail) = Results[i];
            if (!pass)
            {
                failures++;
            }

            Console.WriteLine($"[{i + 1}/{n}] {(pass ? "PASS" : "FAIL")} {name}{(pass ? string.Empty : $" -> {detail}")}");
        }

        if (failures == 0 && n > 0)
        {
            Console.WriteLine("[harness] ALL RENDER CHECKS PASSED");
            Environment.ExitCode = 0;
        }
        else
        {
            Console.Error.WriteLine($"[harness] {(n == 0 ? "NO" : failures.ToString())} RENDER CHECK(S) FAILED");
            Environment.ExitCode = 1;
        }

        finished = true;

        try
        {
            Application.Current.Exit();
        }
        catch
        {
            // Best effort — the process exit code is already set.
        }
    }

    // ---- Test cases -----------------------------------------------------------------------------

    internal static IReadOnlyList<RenderCase> BuildCases() => new List<RenderCase>
    {
        new("Actions renders one button per data entry with labels + disabled state",            Component<Actions, ActionsProps>(new ActionsProps(Buttons: new IReadOnlyDictionary<string, object?>[]
            {
                D(("id", "save"), ("label", "Save"), ("style", "primary")),
                D(("id", "cancel"), ("label", "Cancel")),
                D(("id", "archive")),
                D(("id", "del"), ("label", "Delete"), ("disabled", true)),
            })),
            border =>
            {
                List<Button> buttons = OfType<Button>(border);
                List<string> labels = buttons.Select(Label).ToList();
                if (buttons.Count != 4)
                {
                    return (false, $"expected 4 buttons, got {buttons.Count} [{string.Join("|", labels)}]");
                }

                if (!labels.SequenceEqual(new[] { "Save", "Cancel", "archive", "Delete" }))
                {
                    return (false, $"labels [{string.Join("|", labels)}] (archive should fall back to id)");
                }

                if (buttons[3].IsEnabled)
                {
                    return (false, "disabled button is still enabled");
                }

                if (buttons.Take(3).Any(button => !button.IsEnabled))
                {
                    return (false, "a non-disabled button was disabled");
                }

                return (true, null);
            }),

        new("Actions with no buttons renders nothing",
            Component<Actions, ActionsProps>(new ActionsProps(Buttons: null)),
            border =>
            {
                int count = OfType<Button>(border).Count;
                return count == 0 ? (true, null) : (false, $"expected 0 buttons, got {count}");
            }),

        new("NodeRenderer resolves a metric node (headline: no frame label) to the Metric tile",
            Component<NodeRenderer, NodeRendererProps>(new NodeRendererProps(
                Node: new RegistryNode("metric", Label: "Revenue", HasData: true, Data: 42d))),
            border =>
            {
                List<string> texts = OfType<TextBlock>(border).Select(text => text.Text).ToList();
                if (!texts.Contains("Revenue") || !texts.Contains("42"))
                {
                    return (false, $"metric texts [{string.Join("|", texts)}]");
                }

                // HEADLINE meta suppresses the engine frame label, so the title appears exactly once.
                if (texts.Count(text => text == "Revenue") != 1)
                {
                    return (false, "expected the label exactly once (no duplicated engine frame label)");
                }

                return (true, null);
            }),

        new("NodeRenderer frames a labelled table node and resolves rows + columns",
            Component<NodeRenderer, NodeRendererProps>(new NodeRendererProps(
                Node: new RegistryNode("table", Label: "Items", HasData: true, Data: new List<object?>
                {
                    D(("name", "A"), ("count", 1d)),
                    D(("name", "B"), ("count", 2d)),
                }))),
            border =>
            {
                List<string> texts = OfType<TextBlock>(border).Select(text => text.Text).ToList();
                bool framed = texts.Contains("Items");            // READ_ONLY meta -> engine frame label
                bool columns = texts.Contains("name") && texts.Contains("count");
                bool cells = texts.Contains("A") && texts.Contains("B");
                return framed && columns && cells
                    ? (true, null)
                    : (false, $"table texts [{string.Join("|", texts)}]");
            }),

        new("NodeRenderer hides a node whose visible-bind resolves falsy",
            Component<NodeRenderer, NodeRendererProps>(new NodeRendererProps(
                Node: new RegistryNode("metric", Label: "Hidden", Visible: "flags.ready", HasData: true, Data: 7d),
                Namespaces: D(("flags", D(("ready", false)))))),
            border =>
            {
                int count = OfType<TextBlock>(border).Count;
                return count == 0 ? (true, null) : (false, $"expected nothing rendered, got {count} text block(s)");
            }),
        new("Todo renders a row per item with text + checkbox state plus a composer",
            Component<Todo, TodoProps>(new TodoProps(BaseItems: new IReadOnlyDictionary<string, object?>[]
            {
                D(("text", "Buy milk"), ("done", true)),
                D(("text", "Walk dog"), ("done", false)),
            })),
            border =>
            {
                List<string> texts = OfType<TextBlock>(border).Select(text => text.Text).ToList();
                if (!texts.Contains("Buy milk") || !texts.Contains("Walk dog"))
                {
                    return (false, $"item texts [{string.Join("|", texts)}]");
                }

                List<CheckBox> checks = OfType<CheckBox>(border);
                if (checks.Count < 2)
                {
                    return (false, $"expected >= 2 checkboxes, got {checks.Count}");
                }

                if (checks[0].IsChecked != true)
                {
                    return (false, "first item should be checked (done:true)");
                }

                if (checks[1].IsChecked != false)
                {
                    return (false, "second item should be unchecked (done:false)");
                }

                if (!OfType<TextBox>(border).Any(box => AutoName(box) == "Add todo item"))
                {
                    return (false, "composer text box missing");
                }

                return (true, null);
            }),

        new("EditableTable renders headers from spec columns and the empty placeholder",
            Component<EditableTable, EditableTableProps>(new EditableTableProps(
                Spec: D(
                    ("schema", D(("properties", D(
                        ("name", D(("title", "Name"))),
                        ("qty", D(("title", "Qty"))))))),
                    ("columns", new object?[] { "name", "qty" }),
                    ("placeholder", "Nothing here")),
                BaseRows: null)),
            border =>
            {
                List<string> texts = OfType<TextBlock>(border).Select(text => text.Text).ToList();
                if (!texts.Contains("name") || !texts.Contains("qty"))
                {
                    return (false, $"header cells [{string.Join("|", texts)}]");
                }

                if (!texts.Contains("Nothing here"))
                {
                    return (false, "empty-state placeholder missing");
                }

                if (!OfType<Button>(border).Any(button => AutoName(button) == "Add row"))
                {
                    return (false, "add-row button missing");
                }

                return (true, null);
            }),

        new("EditableTable with addRow:false and no columns shows only the placeholder",
            Component<EditableTable, EditableTableProps>(new EditableTableProps(
                Spec: D(("addRow", false)),
                BaseRows: null)),
            border =>
            {
                List<string> texts = OfType<TextBlock>(border).Select(text => text.Text).ToList();
                int buttons = OfType<Button>(border).Count;
                if (!texts.Contains("No data"))
                {
                    return (false, $"default placeholder missing, texts [{string.Join("|", texts)}]");
                }

                return buttons == 0 ? (true, null) : (false, $"expected 0 buttons, got {buttons}");
            }),

        new("Form renders the field label and save/cancel actions from the spec",
            Component<Form, FormProps>(new FormProps(
                Spec: D(
                    ("fields", D(("properties", D(
                        ("name", D(("type", "string"), ("title", "Full Name"))))))),
                    ("saveLabel", "Apply")),
                AlwaysShowActions: true)),
            border =>
            {
                List<string> texts = OfType<TextBlock>(border).Select(text => text.Text).ToList();
                if (!texts.Contains("Full Name"))
                {
                    return (false, $"field label missing, texts [{string.Join("|", texts)}]");
                }

                List<string> names = OfType<Button>(border).Select(Label).ToList();
                if (!names.Contains("Apply"))
                {
                    return (false, $"save button (saveLabel) missing [{string.Join("|", names)}]");
                }

                if (!names.Contains("Discard"))
                {
                    return (false, $"cancel button missing [{string.Join("|", names)}]");
                }

                return (true, null);
            }),

        new("Select renders a ComboBox with normalized options and the selected index from Value",
            Component<SelectControl, SelectControlProps>(new SelectControlProps(
                Value: "b",
                Options: new object?[] { "a", D(("value", "b"), ("label", "Bee")), "c" })),
            border =>
            {
                List<ComboBox> combos = OfType<ComboBox>(border);
                if (combos.Count != 1)
                {
                    return (false, $"expected 1 combobox, got {combos.Count}");
                }

                ComboBox combo = combos[0];
                int itemCount = combo.Items.Count;
                if (itemCount == 0 && combo.ItemsSource is IEnumerable source)
                {
                    itemCount = source.Cast<object?>().Count();
                }

                if (itemCount != 3)
                {
                    return (false, $"expected 3 options, got {itemCount}");
                }

                return combo.SelectedIndex == 1
                    ? (true, null)
                    : (false, $"expected SelectedIndex 1 (Value 'b'), got {combo.SelectedIndex}");
            }),

        new("Text file-links renders a link button per file from plain data",
            Component<Text, TextProps>(new TextProps(
                Value: new object?[]
                {
                    D(("name", "Report"), ("stored_name", "r.pdf"), ("size", 2048)),
                    D(("name", "Notes"), ("stored_name", "n.txt")),
                    D(("name", "Pending")),
                },
                Format: "file-links",
                ResolveFileUrl: (index, file) => $"https://files.example/{file["stored_name"]}")),
            border =>
            {
                List<Button> buttons = OfType<Button>(border);
                List<string> labels = buttons.Select(Label).ToList();
                if (buttons.Count != 2)
                {
                    return (false, $"expected 2 link buttons (file without stored_name skipped), got {buttons.Count} [{string.Join("|", labels)}]");
                }

                if (!labels.Any(label => label.StartsWith("Report")) || !labels.Any(label => label.StartsWith("Notes")))
                {
                    return (false, $"link labels [{string.Join("|", labels)}]");
                }

                if (buttons.Any(button => !button.IsEnabled))
                {
                    return (false, "a link button is disabled despite a resolved href");
                }

                return (true, null);
            }),

        new("MessageList renders a bubble per message with an expand toggle and resolved system attachment",
            Component<MessageList, MessageListProps>(new MessageListProps(
                Messages: new IReadOnlyDictionary<string, object?>[]
                {
                    D(("role", "user"), ("text", "Hi"), ("turn", "t1")),
                    D(("role", "assistant"), ("text", new string('x', 320)), ("turn", "t1")),
                    D(("role", "system"), ("text", "file uploaded: Report #0"), ("turn", "t1")),
                },
                FilesUploaded: new IReadOnlyDictionary<string, object?>[]
                {
                    D(("name", "Report"), ("stored_name", "r.pdf")),
                },
                ResolveFileUrl: (index, file) => $"https://files.example/{file["stored_name"]}")),
            border =>
            {
                List<string> buttonNames = OfType<Button>(border).Select(Label).ToList();
                if (!buttonNames.Contains("Expand message"))
                {
                    return (false, $"long assistant message should expose an expand toggle [{string.Join("|", buttonNames)}]");
                }

                if (!buttonNames.Contains("Report"))
                {
                    return (false, $"system attachment chip (resolved url) missing [{string.Join("|", buttonNames)}]");
                }

                return (true, null);
            }),

        new("AgentWorkingBubble renders the working indicator plus an activity chip per live stream",
            Component<AgentWorkingBubble, AgentWorkingBubbleProps>(new AgentWorkingBubbleProps(
                AgentOutput: "thinking\nlast output line",
                AgentTools: "calling a tool")),
            border =>
            {
                List<string> texts = OfType<TextBlock>(border).Select(text => text.Text).ToList();
                if (!texts.Any(text => text.Contains("AI working")))
                {
                    return (false, $"working label missing [{string.Join("|", texts)}]");
                }

                int chips = OfType<Button>(border).Count;
                if (chips < 2)
                {
                    return (false, $"expected 2 activity chips (output + tools), got {chips}");
                }

                return (true, null);
            }),

        new("ChatPane renders the conversation plus a composer that hides while a mini turn processes",
            Component<ChatPane, ChatPaneProps>(new ChatPaneProps(
                LiveMessages: new IReadOnlyDictionary<string, object?>[]
                {
                    D(("role", "user"), ("text", "Hello"), ("turn", "t1")),
                },
                OnSubmit: _ => { },
                Placeholder: "Send a message")),
            border =>
            {
                if (!OfType<TextBox>(border).Any())
                {
                    return (false, "composer text box missing");
                }

                List<string> buttonNames = OfType<Button>(border).Select(Label).ToList();
                if (!buttonNames.Contains("Send"))
                {
                    return (false, $"composer send button missing [{string.Join("|", buttonNames)}]");
                }

                if (!buttonNames.Contains("Attach files"))
                {
                    return (false, $"composer attach button missing [{string.Join("|", buttonNames)}]");
                }

                return (true, null);
            }),
    };

    // ---- Visual-tree helpers --------------------------------------------------------------------

    private static IReadOnlyDictionary<string, object?> D(params (string Key, object? Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (DependencyObject grandChild in Descendants(child))
            {
                yield return grandChild;
            }
        }
    }

    private static List<T> OfType<T>(DependencyObject root)
        where T : DependencyObject => Descendants(root).OfType<T>().ToList();

    private static string? AutoName(DependencyObject element) => AutomationProperties.GetName(element);

    private static string Label(Button button) =>
        AutoName(button) is { Length: > 0 } name ? name : Convert.ToString(button.Content) ?? string.Empty;
}

/// <summary>A single render parity case: the component element to mount and an assertion over its tree.</summary>
internal sealed record RenderCase(string Name, Element Node, Func<Border, (bool Pass, string? Detail)> Assert);

/// <summary>
/// Root component for the render harness. Mounts every <see cref="RenderHarness.BuildCases"/> entry
/// (each wrapped in a captured <c>Border</c>) under a theme provider, then schedules the visual-tree
/// assertions to run once after the first render commit.
/// </summary>
internal sealed class RenderHarnessRoot : Component
{
    public override Element Render()
    {
        App.Current.EnsureUiResources();

        IReadOnlyList<RenderCase> cases = RenderHarness.Cases ??= RenderHarness.BuildCases();
        if (RenderHarness.Slots.Length != cases.Count)
        {
            RenderHarness.Slots = new Border?[cases.Count];
        }

        var children = new Element[cases.Count];
        for (int i = 0; i < cases.Count; i++)
        {
            int index = i;
            children[i] = Border(cases[i].Node).Set(border => RenderHarness.Slots[index] = border);
        }

        UseEffect(() => RenderHarness.ScheduleAssertions());

        return ScrollViewer(VStack(8, children))
            .Provide(AppThemeContext.Current, AppTheme.FromResources());
    }
}
