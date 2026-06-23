using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.Config;
using DemoBoards_WinUI.State;
using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls;

public sealed class ReactorSmokeRunnerComponent : Component
{
    private const string T3UChatCardId = "card-portfolio-t3u-9105";
    private const string RunTestsPlaceholder = "MB1, T3u, T8F";
    private const string ProbeEnvelope = "__probe__echo__probe__";

    private static readonly SmokeCaseSpec[] SmokeCases =
    {
        new("MB1", "Ensure board registration", SmokeCaseMode.Run),
        new("MB2", "Resolver honors paneRules and cardRendererRules", SmokeCaseMode.Run),
        new("T0", "Seed portfolio and wait for completion", SmokeCaseMode.Run),
        new("T1", "Discovery and preflight coverage", SmokeCaseMode.Run),
        new("TQ", "Queue drain wakeup", SmokeCaseMode.Skip, "Frontend smoke runner leaves queue internals out of scope here."),
        new("TT", "Task-executor queue drain", SmokeCaseMode.Skip, "Frontend smoke runner leaves task-executor queue internals out of scope here."),
        new("T2", "Portfolio compute end-to-end", SmokeCaseMode.Run),
        new("T3", "Probe chat lifecycle", SmokeCaseMode.Run),
        new("T3u", "Probe chat lifecycle via real ChatPane UI", SmokeCaseMode.Run),
        new("T4", "Probe chat with attachment", SmokeCaseMode.Run),
        new("TS", "Chat SSE bouquet with attachment", SmokeCaseMode.Skip, "Frontend smoke runner validates reduced state instead of raw SSE chronology here."),
        new("T8", "Hosted assistant chat via copilot probe", SmokeCaseMode.Run),
        new("T9", "Hosted assistant chat via foundry probe", SmokeCaseMode.Skip, "Requires a configured Azure AI Foundry endpoint."),
        new("T8F", "Hosted assistant attachment chat via copilot probe", SmokeCaseMode.Run),
        new("T9F", "Hosted assistant attachment chat via foundry probe", SmokeCaseMode.Skip, "Requires a configured Azure AI Foundry endpoint."),
        new("TR", "Card refresh lifecycle over SSE", SmokeCaseMode.Run),
    };

    private static readonly SmokeCaseSpec[] RunnableSmokeCases = SmokeCases
        .Where(entry => entry.Mode == SmokeCaseMode.Run)
        .ToArray();

    public override Element Render()
    {
        var processRef = UseRef<Process?>(null);
        var cancelRequestedRef = UseRef(false);

        var (suiteStatus, setSuiteStatus) = UseState("idle");
        var (statusText, setStatusText) = UseState("Run the frontend SmokeRunner case catalog against the embedded WinUI runtime.");
        var (suiteError, setSuiteError) = UseState(string.Empty);
        var (runTestsText, setRunTestsText) = UseState(string.Empty);
        var (outputText, setOutputText) = UseState(string.Empty);
        var (startedAtTicks, setStartedAtTicks) = UseState(0L);
        var (finishedAtTicks, setFinishedAtTicks) = UseState(0L);
        var (caseStates, setCaseStates) = UseState<IReadOnlyList<SmokeCaseState>>(CreateInitialCaseStates(SmokeCases));

        SmokeCaseSelection selection = ResolveSelectedSmokeCases(runTestsText);
        HashSet<string> selectedRunnableCaseIds = ResolveSelectedRunnableCaseIds(selection);
        string activeBoardId = App.Current.BoardStore.State.BoardId;
        string durationText = BuildDurationText(startedAtTicks, finishedAtTicks);

        var sections = new List<Element>();
        sections.Add(TextBlock("Smoke Runner").FontSize(20).Bold());
        sections.Add(TextBlock(statusText).Opacity(0.74).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords));
        Element toolbar = HStack(8,
            Button(suiteStatus == "running" ? "Running..." : "Run", () =>
            {
                if (suiteStatus == "running")
                {
                    return;
                }

                _ = RunSmokeAsync(
                    selection,
                    activeBoardId,
                    processRef,
                    cancelRequestedRef,
                    setSuiteStatus,
                    setStatusText,
                    setSuiteError,
                    setOutputText,
                    setStartedAtTicks,
                    setFinishedAtTicks,
                    setCaseStates);
            }).AutomationName("Run smoke tests").AccentButton(),
            Button("Stop", () =>
            {
                cancelRequestedRef.Current = true;
                TryKillProcess(processRef.Current);
            }).AutomationName("Stop smoke tests").SubtleButton().Set(button => button.IsEnabled = suiteStatus == "running"),
            Button("Copy Report", () => CopyReport(caseStates, outputText, suiteStatus, activeBoardId))
                .AutomationName("Copy smoke report")
                .SubtleButton()
                .Set(button => button.IsEnabled = caseStates.Count > 0 || !string.IsNullOrWhiteSpace(outputText)),
            TextBlock($"{suiteStatus.ToUpperInvariant()} · {durationText}").Opacity(0.82));
        sections.Add(SectionCard(VStack(10, toolbar)));
        sections.Add(SectionCard(VStack(8,
            TextBlock("Run Tests").Bold().Opacity(0.82),
            TextBox(runTestsText, setRunTestsText)
                .AutomationName("Run Tests")
                .PlaceholderText(RunTestsPlaceholder)
                .Set(textBox =>
                {
                    textBox.TextWrapping = TextWrapping.Wrap;
                    textBox.IsEnabled = suiteStatus != "running";
                }),
            HintText("Leave empty to run all frontend smoke cases, or type a comma-separated subset such as MB1, T3u, T8F."),
            VStack(6, RunnableSmokeCases.Select(entry => BuildRunnableCaseToggle(entry, selectedRunnableCaseIds.Contains(entry.Id), suiteStatus == "running", setRunTestsText, selectedRunnableCaseIds)).ToArray()),
            TextBlock(selection.RequestedIds.Count > 0
                    ? $"Selected order: {string.Join(", ", selection.SelectedCases.Select(entry => entry.Id))}"
                    : "Selected order: all frontend smoke cases.")
                .Opacity(0.72)
                .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
            string.IsNullOrWhiteSpace(suiteError)
                ? (Element)TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed)
                : TextBlock(suiteError).Foreground(CreateStatusBrush("failed")).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords))));
        sections.Add(SectionCard(VStack(8,
            TextBlock("Summary").Bold().Opacity(0.82),
            TextBlock($"Board: {activeBoardId}").Opacity(0.74),
            TextBlock(BuildSummaryText(caseStates)).Opacity(0.74).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords))));
        sections.Add(SectionCard(VStack(8,
            TextBlock("Cases").Bold().Opacity(0.82),
            VStack(8, caseStates.Select(BuildCaseRow).ToArray()))));
        sections.Add(SectionCard(VStack(8,
            TextBlock("Output").Bold().Opacity(0.82),
            BuildCodeBlock(outputText, 420))));

        return ScrollViewer(VStack(12, sections.ToArray()))
            .Set(scrollViewer => scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto);
    }

    private static async Task RunSmokeAsync(
        SmokeCaseSelection selection,
        string activeBoardId,
        Ref<Process?> processRef,
        Ref<bool> cancelRequestedRef,
        Action<string> setSuiteStatus,
        Action<string> setStatusText,
        Action<string> setSuiteError,
        Action<string> setOutputText,
        Action<long> setStartedAtTicks,
        Action<long> setFinishedAtTicks,
        Action<IReadOnlyList<SmokeCaseState>> setCaseStates)
    {
        if (selection.UnknownIds.Count > 0)
        {
            setSuiteStatus("failed");
            setStatusText("Smoke case selection failed.");
            setSuiteError($"Unknown smoke test ids: {string.Join(", ", selection.UnknownIds)}");
            return;
        }

        cancelRequestedRef.Current = false;
        setSuiteStatus("running");
        setStatusText($"Running frontend smoke cases against board '{activeBoardId}'...");
        setSuiteError(string.Empty);
        setOutputText(string.Empty);
        setStartedAtTicks(DateTime.UtcNow.Ticks);
        setFinishedAtTicks(0);

        IReadOnlyList<SmokeCaseSpec> selectedCases = selection.SelectedCases;
        IReadOnlyList<SmokeCaseState> nextStates = CreateInitialCaseStates(selectedCases);
        setCaseStates(nextStates);

        List<string> completedCases = new();
        StringBuilder combinedOutput = new();

        try
        {
            IReadOnlyList<string> backendCaseIds = selectedCases
                .Where(entry => entry.Mode == SmokeCaseMode.Run && !string.Equals(entry.Id, "T3u", StringComparison.Ordinal))
                .Select(entry => entry.Id)
                .ToArray();

            IReadOnlyList<string> skippedIds = selectedCases
                .Where(entry => entry.Mode == SmokeCaseMode.Skip)
                .Select(entry => entry.Id)
                .ToArray();

            nextStates = UpdateStatuses(nextStates, skippedIds, "skipped", null);
            nextStates = UpdateStatuses(nextStates, backendCaseIds, "running", "Running via backend HTTP smoke script...");
            setCaseStates(nextStates);

            if (backendCaseIds.Count > 0)
            {
                (int exitCode, string output) = await RunBackendSmokeScriptAsync(activeBoardId, backendCaseIds, processRef, cancelRequestedRef).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(output))
                {
                    combinedOutput.AppendLine(output.Trim());
                }
                if (exitCode != 0)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(output)
                        ? $"Backend smoke runner failed with exit code {exitCode}."
                        : output.Trim());
                }

                completedCases.AddRange(backendCaseIds);
                nextStates = BuildProgressStates(selectedCases, completedCases);
                setCaseStates(nextStates);
            }

            if (selectedCases.Any(entry => string.Equals(entry.Id, "T3u", StringComparison.Ordinal)))
            {
                nextStates = UpdateStatuses(nextStates, new[] { "T3u" }, "running", "Running through the WinUI chat path...");
                setCaseStates(nextStates);

                string t3uOutput = await RunT3uCaseAsync(cancelRequestedRef).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(t3uOutput))
                {
                    if (combinedOutput.Length > 0)
                    {
                        combinedOutput.AppendLine();
                    }
                    combinedOutput.AppendLine(t3uOutput.Trim());
                }

                completedCases.Add("T3u");
                nextStates = BuildProgressStates(selectedCases, completedCases);
                setCaseStates(nextStates);
            }

            setCaseStates(BuildProgressStates(selectedCases, completedCases));
            setSuiteStatus("passed");
            setStatusText("Selected frontend smoke cases passed.");
            setOutputText(combinedOutput.ToString().Trim());
        }
        catch (Exception ex)
        {
            bool cancelled = ex is OperationCanceledException;
            setSuiteStatus(cancelled ? "cancelled" : "failed");
            setStatusText(cancelled ? "Smoke runner cancelled." : "Smoke runner failed.");
            setSuiteError(cancelled ? string.Empty : ex.Message);
            if (!cancelled)
            {
                if (combinedOutput.Length > 0)
                {
                    combinedOutput.AppendLine();
                }
                combinedOutput.AppendLine(ex.Message);
            }

            setCaseStates(BuildFailureStates(selectedCases, completedCases, ex.Message, cancelled));
            setOutputText(combinedOutput.ToString().Trim());
        }
        finally
        {
            processRef.Current = null;
            cancelRequestedRef.Current = false;
            setFinishedAtTicks(DateTime.UtcNow.Ticks);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunBackendSmokeScriptAsync(
        string boardId,
        IReadOnlyList<string> caseIds,
        Ref<Process?> processRef,
        Ref<bool> cancelRequestedRef)
    {
        string repoRoot = App.Current.HostConfig.RepoRoot;
        string scriptPath = Path.Combine(repoRoot, "demo-boards-ns-code", "demo-board", "test", "my-http-test.js");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("Could not locate the backend smoke runner script.", scriptPath);
        }

        Uri runtimeUri = new(App.Current.RuntimeService.GetStatus().AgentfaceEndpoint);
        ProcessStartInfo startInfo = new("node")
        {
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(runtimeUri.Port.ToString());
        startInfo.ArgumentList.Add("--board-id");
        startInfo.ArgumentList.Add(boardId);
        startInfo.ArgumentList.Add("--run-tests");
        startInfo.ArgumentList.Add(string.Join(",", caseIds));

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch the backend smoke runner script.");
        processRef.Current = process;

        string stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        string stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        if (cancelRequestedRef.Current)
        {
            throw new OperationCanceledException("Smoke runner cancelled.");
        }

        string output = string.Join(Environment.NewLine, new[] { stdout.Trim(), stderr.Trim() }.Where(text => !string.IsNullOrWhiteSpace(text)));
        return (process.ExitCode, output);
    }

    private static async Task<string> RunT3uCaseAsync(Ref<bool> cancelRequestedRef)
    {
        string cardJson = BuildPortfolioFixtureCardJson(T3UChatCardId, "Portfolio (T3u)");
        string promptText = "hi testing";
        string probeText = BuildProbeChatText(promptText, "echo");
        string turnId = $"t3u-{Guid.NewGuid():N}";
        StringBuilder log = new();

        log.AppendLine($"[T3u] step 0/8: upserting {T3UChatCardId} for chat");
        JsonNode candidate = JsonNode.Parse(cardJson) ?? throw new InvalidOperationException("Failed to parse the T3u card fixture.");
        _ = await App.Current.BoardClient.CallBoardMcpAsync("manage.upsert-card", new
        {
            card_id = T3UChatCardId,
            candidate_card_content = candidate,
        }).ConfigureAwait(false);

        WinUiBoardServerConstants constants = App.Current.HostConfig.Frontend.BoardServerConstants;
        await App.Current.BoardClient.SubscribeCardChatsAsync(T3UChatCardId).ConfigureAwait(false);
        await App.Current.BoardClient.SubscribeWatchpartyAsync(T3UChatCardId, constants.AgentOutputChannel).ConfigureAwait(false);
        await App.Current.BoardClient.SubscribeWatchpartyAsync(T3UChatCardId, constants.AgentToolsChannel).ConfigureAwait(false);

        try
        {
            log.AppendLine("[T3u] step 1/8: sending chat through WinUI chat path");
            await App.Current.BoardClient.SendChatAsync(T3UChatCardId, probeText, turnId).ConfigureAwait(false);

            log.AppendLine("[T3u] step 2/8: verifying chat processing turns on");
            await WaitUntilAsync(() =>
            {
                EnsureNotCancelled(cancelRequestedRef);
                BoardCard? card = App.Current.BoardStore.GetCardDefinitionAndData(T3UChatCardId);
                return card?.ChatProcessing == true;
            }, TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(250), "T3u chat processing did not turn on.").ConfigureAwait(false);

            log.AppendLine("[T3u] step 3/8: verifying user chat entry is stored");
            await WaitUntilAsync(() =>
            {
                EnsureNotCancelled(cancelRequestedRef);
                BoardCard? card = App.Current.BoardStore.GetCardDefinitionAndData(T3UChatCardId);
                return card is not null && card.ChatMessages.Any(message => message.Role == "user" && string.Equals(message.Text, promptText, StringComparison.Ordinal));
            }, TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(250), "T3u user message was not stored.").ConfigureAwait(false);

            log.AppendLine("[T3u] step 4/8: waiting for probe final reply");
            await WaitUntilAsync(() =>
            {
                EnsureNotCancelled(cancelRequestedRef);
                BoardCard? card = App.Current.BoardStore.GetCardDefinitionAndData(T3UChatCardId);
                return card is not null && card.ChatMessages.Any(message => message.Role == "assistant" && (message.Text ?? string.Empty).Contains("Echo: hi testing", StringComparison.Ordinal));
            }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500), "T3u assistant reply was not observed.").ConfigureAwait(false);

            log.AppendLine("[T3u] step 5/8: verifying chat processing turns off");
            await WaitUntilAsync(() =>
            {
                EnsureNotCancelled(cancelRequestedRef);
                BoardCard? card = App.Current.BoardStore.GetCardDefinitionAndData(T3UChatCardId);
                return card is not null && !card.ChatProcessing;
            }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500), "T3u chat processing did not turn off.").ConfigureAwait(false);

            log.AppendLine("[T3u] step 6/8: verifying final inspected messages");
            BoardCard? finalCard = App.Current.BoardStore.GetCardDefinitionAndData(T3UChatCardId);
            bool hasUser = finalCard?.ChatMessages.Any(message => message.Role == "user" && string.Equals(message.Text, promptText, StringComparison.Ordinal)) == true;
            bool hasAssistant = finalCard?.ChatMessages.Any(message => message.Role == "assistant" && (message.Text ?? string.Empty).Contains("Echo: hi testing", StringComparison.Ordinal)) == true;
            if (!hasUser || !hasAssistant)
            {
                throw new InvalidOperationException("T3u final chat messages were incomplete.");
            }

            log.AppendLine("[T3u] step 7/8: verifying watchparty tools");
            await WaitUntilAsync(() =>
            {
                EnsureNotCancelled(cancelRequestedRef);
                BoardWatchpartyState watchparty = App.Current.BoardStore.GetCardWatchparty(T3UChatCardId);
                return watchparty.AgentToolPayloads.Count > 0 || !string.IsNullOrWhiteSpace(watchparty.AgentTools);
            }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500), "T3u watchparty tools were not observed.").ConfigureAwait(false);

            log.AppendLine("[T3u] step 8/8: verifying card reaches completed state");
            await WaitUntilAsync(() =>
            {
                EnsureNotCancelled(cancelRequestedRef);
                BoardCard? card = App.Current.BoardStore.GetCardDefinitionAndData(T3UChatCardId);
                return card is not null && string.Equals(card.Status, "completed", StringComparison.OrdinalIgnoreCase);
            }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500), "T3u card did not reach completed state.").ConfigureAwait(false);

            log.AppendLine("[T3u] passed");
            return log.ToString().Trim();
        }
        finally
        {
            try
            {
                await App.Current.BoardClient.UnsubscribeWatchpartyAsync(T3UChatCardId, constants.AgentToolsChannel).ConfigureAwait(false);
                await App.Current.BoardClient.UnsubscribeWatchpartyAsync(T3UChatCardId, constants.AgentOutputChannel).ConfigureAwait(false);
                await App.Current.BoardClient.UnsubscribeCardChatsAsync(T3UChatCardId).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> probe, TimeSpan timeout, TimeSpan interval, string failureMessage)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow <= deadline)
        {
            if (probe())
            {
                return;
            }

            await Task.Delay(interval).ConfigureAwait(false);
        }

        throw new InvalidOperationException(failureMessage);
    }

    private static void EnsureNotCancelled(Ref<bool> cancelRequestedRef)
    {
        if (cancelRequestedRef.Current)
        {
            throw new OperationCanceledException("Smoke runner cancelled.");
        }
    }

    private static Element BuildRunnableCaseToggle(
        SmokeCaseSpec entry,
        bool isChecked,
        bool disabled,
        Action<string> setRunTestsText,
        IReadOnlySet<string> selectedRunnableCaseIds)
    {
        return Button($"{(isChecked ? "[x]" : "[ ]")} {entry.Id} - {entry.Title}", () =>
            {
                IEnumerable<string> next = isChecked
                    ? selectedRunnableCaseIds.Where(id => !string.Equals(id, entry.Id, StringComparison.Ordinal))
                    : selectedRunnableCaseIds.Append(entry.Id);
                setRunTestsText(FormatSelectedRunnableCaseIds(next));
            })
            .AutomationName($"Toggle smoke case {entry.Id}")
            .SubtleButton()
            .Set(button => button.IsEnabled = !disabled);
    }

    private static IReadOnlyList<SmokeCaseState> CreateInitialCaseStates(IReadOnlyList<SmokeCaseSpec> entries)
    {
        return entries.Select(entry => new SmokeCaseState(
            entry.Id,
            entry.Title,
            entry.Mode == SmokeCaseMode.Skip ? "skipped" : "pending",
            entry.Mode == SmokeCaseMode.Skip ? entry.Reason : string.Empty)).ToArray();
    }

    private static SmokeCaseSelection ResolveSelectedSmokeCases(string value)
    {
        IReadOnlyList<string> requestedIds = ParseRequestedSmokeCaseIds(value);
        Dictionary<string, SmokeCaseSpec> knownCases = SmokeCases.ToDictionary(entry => NormalizeCaseId(entry.Id), entry => entry, StringComparer.Ordinal);
        IReadOnlyList<string> unknownIds = requestedIds.Where(caseId => !knownCases.ContainsKey(NormalizeCaseId(caseId))).ToArray();
        if (requestedIds.Count == 0)
        {
            return new SmokeCaseSelection(Array.Empty<string>(), Array.Empty<string>(), SmokeCases);
        }

        HashSet<string> requestedSet = requestedIds.Select(NormalizeCaseId).ToHashSet(StringComparer.Ordinal);
        SmokeCaseSpec[] selected = SmokeCases.Where(entry => requestedSet.Contains(NormalizeCaseId(entry.Id))).ToArray();
        return new SmokeCaseSelection(requestedIds, unknownIds, selected);
    }

    private static HashSet<string> ResolveSelectedRunnableCaseIds(SmokeCaseSelection selection)
    {
        if (selection.RequestedIds.Count == 0)
        {
            return RunnableSmokeCases.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        }

        return selection.SelectedCases
            .Where(entry => entry.Mode == SmokeCaseMode.Run)
            .Select(entry => entry.Id)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> ParseRequestedSmokeCaseIds(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',').Select(entry => entry.Trim()).Where(entry => !string.IsNullOrWhiteSpace(entry)).ToArray();
    }

    private static IReadOnlyList<SmokeCaseState> UpdateStatuses(IReadOnlyList<SmokeCaseState> source, IReadOnlyList<string> ids, string status, string? detail)
    {
        HashSet<string> targets = ids.ToHashSet(StringComparer.Ordinal);
        return source.Select(entry => targets.Contains(entry.Id)
            ? entry with { Status = status, Detail = detail ?? entry.Detail }
            : entry).ToArray();
    }

    private static IReadOnlyList<SmokeCaseState> BuildProgressStates(IReadOnlyList<SmokeCaseSpec> selectedCases, IReadOnlyList<string> completedCases)
    {
        HashSet<string> passedIds = completedCases.ToHashSet(StringComparer.Ordinal);
        return selectedCases.Select(entry =>
        {
            if (entry.Mode == SmokeCaseMode.Skip)
            {
                return new SmokeCaseState(entry.Id, entry.Title, "skipped", entry.Reason);
            }

            if (passedIds.Contains(entry.Id))
            {
                return new SmokeCaseState(entry.Id, entry.Title, "passed", string.Empty);
            }

            return new SmokeCaseState(entry.Id, entry.Title, "pending", string.Empty);
        }).ToArray();
    }

    private static IReadOnlyList<SmokeCaseState> BuildFailureStates(IReadOnlyList<SmokeCaseSpec> selectedCases, IReadOnlyList<string> completedCases, string detail, bool cancelled)
    {
        HashSet<string> passedIds = completedCases.ToHashSet(StringComparer.Ordinal);
        return selectedCases.Select(entry =>
        {
            if (entry.Mode == SmokeCaseMode.Skip)
            {
                return new SmokeCaseState(entry.Id, entry.Title, "skipped", entry.Reason);
            }

            if (passedIds.Contains(entry.Id))
            {
                return new SmokeCaseState(entry.Id, entry.Title, "passed", string.Empty);
            }

            return new SmokeCaseState(entry.Id, entry.Title, cancelled ? "cancelled" : "failed", detail);
        }).ToArray();
    }

    private static string NormalizeCaseId(string value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static string FormatSelectedRunnableCaseIds(IEnumerable<string> caseIds)
    {
        HashSet<string> selected = caseIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(NormalizeCaseId)
            .ToHashSet(StringComparer.Ordinal);

        string[] normalized = RunnableSmokeCases
            .Select(entry => entry.Id)
            .Where(selected.Contains)
            .ToArray();

        return normalized.Length == RunnableSmokeCases.Length
            ? string.Empty
            : string.Join(", ", normalized);
    }

    private static string BuildDurationText(long startedAtTicks, long finishedAtTicks)
    {
        if (startedAtTicks <= 0)
        {
            return "0s";
        }

        DateTime startedAt = new(startedAtTicks, DateTimeKind.Utc);
        DateTime finishedAt = finishedAtTicks > 0 ? new DateTime(finishedAtTicks, DateTimeKind.Utc) : DateTime.UtcNow;
        return $"{Math.Max(0, (int)Math.Round((finishedAt - startedAt).TotalSeconds))}s";
    }

    private static string BuildSummaryText(IReadOnlyList<SmokeCaseState> caseStates)
    {
        int passed = caseStates.Count(entry => string.Equals(entry.Status, "passed", StringComparison.Ordinal));
        int failed = caseStates.Count(entry => string.Equals(entry.Status, "failed", StringComparison.Ordinal));
        int skipped = caseStates.Count(entry => string.Equals(entry.Status, "skipped", StringComparison.Ordinal));
        int pending = caseStates.Count(entry => string.Equals(entry.Status, "pending", StringComparison.Ordinal));
        return $"Passed {passed}  Failed {failed}  Skipped {skipped}  Pending {pending}";
    }

    private static void CopyReport(IReadOnlyList<SmokeCaseState> caseStates, string outputText, string suiteStatus, string boardId)
    {
        StringBuilder builder = new();
        builder.AppendLine($"Smoke Runner ({boardId})");
        builder.AppendLine($"Status: {suiteStatus}");
        builder.AppendLine();
        builder.AppendLine("Cases");
        foreach (SmokeCaseState entry in caseStates)
        {
            builder.AppendLine($"{entry.Id}: {entry.Status}{(string.IsNullOrWhiteSpace(entry.Detail) ? string.Empty : $" - {entry.Detail}")}");
        }
        builder.AppendLine();
        builder.AppendLine("Output");
        builder.AppendLine(outputText);

        DataPackage package = new();
        package.SetText(builder.ToString());
        Clipboard.SetContent(package);
    }

    private static void TryKillProcess(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static Element BuildCaseRow(SmokeCaseState state)
    {
        return Border(VStack(4,
                TextBlock($"{state.Id} - {state.Title}").Bold().Opacity(0.9),
                TextBlock(state.Status).Foreground(CreateStatusBrush(state.Status)).Opacity(0.84),
                string.IsNullOrWhiteSpace(state.Detail)
                    ? TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed)
                    : TextBlock(state.Detail).Opacity(0.7).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords)))
            .Padding(10)
            .Background(BoardTheme.ResolveBrush("CardBackgroundFillColorDefaultBrush", Colors.White))
            .WithBorder(BoardTheme.ResolveBrush("BoardBorderStrongBrush", Colors.LightGray), 1)
            .CornerRadius(12);
    }

    private static Element BuildCodeBlock(string value, double minHeight)
    {
        return TextBox(value)
            .AutomationName("Smoke runner output")
            .IsReadOnly(true)
            .AcceptsReturn(true)
            .TextWrapping(TextWrapping.Wrap)
            .Set(textBox =>
            {
                textBox.MinHeight = minHeight;
                textBox.FontFamily = new FontFamily("Consolas");
            });
    }

    private static Element SectionCard(Element content)
    {
        return Border(content)
            .Padding(14)
            .Background(BoardTheme.ResolveBrush("CardBackgroundFillColorDefaultBrush", Colors.White))
            .WithBorder(BoardTheme.ResolveBrush("BoardBorderStrongBrush", Colors.LightGray), 1)
            .CornerRadius(14);
    }

    private static Element HintText(string message)
    {
        return TextBlock(message)
            .Opacity(0.68)
            .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords);
    }

    private static Brush CreateStatusBrush(string status)
    {
        return status switch
        {
            "passed" => new SolidColorBrush(Colors.SeaGreen),
            "failed" => new SolidColorBrush(Colors.IndianRed),
            "cancelled" => new SolidColorBrush(Colors.DarkGoldenrod),
            "running" => new SolidColorBrush(Colors.SteelBlue),
            _ => BoardTheme.ResolveBrush("BoardTextMutedBrush", Colors.DimGray),
        };
    }

    private static string BuildPortfolioFixtureCardJson(string cardId, string title)
    {
        string fixturePath = Path.Combine(App.Current.HostConfig.RepoRoot, "demo-boards-ns-code", "demo-board", "test", "live-cards", "cardT-portfolio.json");
        if (!File.Exists(fixturePath))
        {
            throw new FileNotFoundException("Could not locate the shared card fixture for T3u.", fixturePath);
        }

        JsonNode root = JsonNode.Parse(File.ReadAllText(fixturePath)) ?? throw new InvalidOperationException("Failed to parse the shared T3u card fixture.");
        if (root is not JsonObject jsonObject)
        {
            throw new InvalidOperationException("The shared T3u card fixture was not a JSON object.");
        }

        jsonObject["id"] = cardId;
        JsonObject meta = jsonObject["meta"] as JsonObject ?? new JsonObject();
        meta["title"] = title;
        jsonObject["meta"] = meta;
        return jsonObject.ToJsonString();
    }

    private static string BuildProbeChatText(string promptText, string assistantStem)
    {
        string normalizedAssistant = string.IsNullOrWhiteSpace(assistantStem) ? "echo" : assistantStem.Trim();
        return $"{ProbeEnvelope}{normalizedAssistant}__{promptText}{ProbeEnvelope}";
    }

    private sealed record SmokeCaseSpec(string Id, string Title, SmokeCaseMode Mode, string Reason = "");

    private sealed record SmokeCaseState(string Id, string Title, string Status, string Detail);

    private sealed record SmokeCaseSelection(IReadOnlyList<string> RequestedIds, IReadOnlyList<string> UnknownIds, IReadOnlyList<SmokeCaseSpec> SelectedCases);

    private enum SmokeCaseMode
    {
        Run,
        Skip,
    }
}
