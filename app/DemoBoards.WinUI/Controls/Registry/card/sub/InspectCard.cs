using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI;
using DemoBoards_WinUI.Assets;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards_WinUI.Hooks;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Controls.Registry;

/// <summary>Props for <see cref="InspectCard"/> (port of <c>InspectCard.jsx</c>'s prop set).</summary>
public sealed record InspectCardProps(string BoardId, string CardId, string Title, Action OnClose);

/// <summary>
/// Inspect card modal (port of <c>card/sub/InspectCard.jsx</c>) — a <see cref="GlobalModal"/> with a live
/// read-only card preview (rendered with <c>chrome="inspect"</c>) plus a delete affordance on the left, and
/// a sidebar on the right combining the <see cref="CardPreflight"/> panel with the trial-run output. It owns
/// the trial-run state machine: running the full card preflight, any single source preflight, or inspecting a
/// dependency token's live data object, each surfaced through <see cref="InspectCardOutput"/>. Deletion routes
/// through <see cref="EmbeddedBoardClient.RemoveRuntimeCardAsync"/> behind a <see cref="ChallengeConfirmModal"/>.
/// DOM-only concerns (the inline trash SVG, the theme-data-attribute read off the shell, className/style hooks
/// and the JSON <c>&lt;pre&gt;</c> styling) are dropped — the JSON sections render as monospace text blocks.
/// </summary>
public sealed class InspectCard : HookComponent<InspectCardProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        EmbeddedBoardClient client = UseEmbeddedClient();
        CardState? cardState = UseCardState(Props.BoardId, Props.CardId);

        (FlightResult? flightResult, Action<FlightResult?> setFlightResult) = UseState<FlightResult?>(null);
        (bool deletePending, Action<bool> setDeletePending) = UseState(false);
        (bool deleteConfirmOpen, Action<bool> setDeleteConfirmOpen) = UseState(false);
        var mountedRef = UseRef(true);

        UseEffect(() => () => { mountedRef.Current = false; });

        UseEffect(
            () =>
            {
                setFlightResult(null);
                setDeletePending(false);
                setDeleteConfirmOpen(false);
                return () => { };
            },
            Props.BoardId,
            Props.CardId);

        FlightResult? activeFlight = flightResult?.Status == "running" ? flightResult : null;
        bool flightDisabled = activeFlight is not null;
        string activeTokenKey = flightResult is { Kind: "token", Token: { Length: > 0 } }
            ? $"{flightResult.TokenKind}:{flightResult.Token}"
            : string.Empty;
        IReadOnlyDictionary<int, bool>? loadingBySource = activeFlight is { Kind: "source", SourceIndex: >= 0 }
            ? new Dictionary<int, bool> { [activeFlight.SourceIndex] = true }
            : null;

        async void RunFlight(FlightResult baseResult, Func<Task<JsonNode?>> action)
        {
            setFlightResult(baseResult with { Status = "running" });
            try
            {
                JsonNode? payload = await action();
                if (!mountedRef.Current)
                {
                    return;
                }

                JsonNode? data = payload is JsonObject or JsonArray ? payload : new JsonObject();
                setFlightResult(baseResult with { Status = "success", Data = data });
            }
            catch (Exception error)
            {
                if (!mountedRef.Current)
                {
                    return;
                }

                setFlightResult(baseResult with { Status = "error", Error = error.Message });
            }
        }

        Action<int, string> handleRunFlight = (sourceIndex, bindTo) =>
        {
            if (flightDisabled || sourceIndex < 0 || cardState is null)
            {
                return;
            }

            string label = bindTo.Length > 0 ? bindTo : $"source {sourceIndex}";
            RunFlight(
                new FlightResult($"Source flight: {label}", "source", SourceIndex: sourceIndex),
                () => cardState.CardActions.RunSingleSourceInLiveCard(sourceIndex, cardState.RequiresDataObjects));
        };

        Action handleRunCardFlight = () =>
        {
            if (flightDisabled || cardState?.CardContent is null)
            {
                return;
            }

            RunFlight(
                new FlightResult($"Card flight: {Props.CardId}", "card"),
                () => cardState.CardActions.RunOneCycleWithCandidateCard(null, cardState.RequiresDataObjects));
        };

        Action<string, string> handleInspectToken = (token, kind) =>
        {
            if (token.Length == 0 || kind.Length == 0 || cardState is null)
            {
                return;
            }

            IReadOnlyDictionary<string, string> source = kind == "require"
                ? cardState.RequiresDataObjects
                : cardState.ProvidesDataObjects;
            bool has = source.TryGetValue(token, out string? raw);
            setFlightResult(new FlightResult(
                $"{(kind == "require" ? "Requires" : "Provides")}: {token}",
                "token",
                Data: has ? SafeParse(raw) : null,
                Token: token,
                TokenKind: kind,
                Missing: !has));
        };

        async void HandleDeleteCard()
        {
            if (string.IsNullOrEmpty(Props.BoardId) || string.IsNullOrEmpty(Props.CardId) || deletePending)
            {
                return;
            }

            setDeletePending(true);
            try
            {
                await client.RemoveRuntimeCardAsync(Props.CardId);
                if (!mountedRef.Current)
                {
                    return;
                }

                setDeleteConfirmOpen(false);
                Props.OnClose();
            }
            catch (Exception error)
            {
                if (!mountedRef.Current)
                {
                    return;
                }

                setFlightResult(new FlightResult($"Delete card: {Props.CardId}", "card", Status: "error", Error: error.Message));
                setDeleteConfirmOpen(false);
            }
            finally
            {
                if (mountedRef.Current)
                {
                    setDeletePending(false);
                }
            }
        }

        if (cardState?.CardContent is not BoardCard content)
        {
            return Empty();
        }

        IReadOnlyDictionary<string, object?> cardContentMap = BoardData.AsMap(RegistryJson.Parse(content.RawDefinitionJson));

        Element preview = VStack(8,
            Component<CardRenderer, CardRendererProps>(new CardRendererProps(Props.BoardId, Props.CardId, Chrome: "inspect")),
            Button(HStack(6,
                    Component<SvgIcon, SvgIconProps>(new SvgIconProps(HostIconSources.InspectDeleteCard, 14)),
                    TextBlock(deletePending ? "Deleting\u2026" : "Delete card")),
                () => setDeleteConfirmOpen(true))
                .SubtleButton()
                .AutomationName("Delete this card")
                .Set(b => b.IsEnabled = !deletePending));

        Element sidebar = VStack(12,
            Component<CardPreflight, CardPreflightProps>(new CardPreflightProps(
                Props.CardId,
                cardContentMap,
                loadingBySource,
                activeFlight?.Kind == "card",
                flightDisabled,
                handleRunCardFlight,
                handleRunFlight,
                handleInspectToken,
                activeTokenKey)),
            InspectCardOutput(flightResult, theme));

        Element body = VStack(0,
            HStack(16, preview.Flex(grow: 1), sidebar.Flex(grow: 1)),
            deleteConfirmOpen
                ? Component<ChallengeConfirmModal, ChallengeConfirmModalProps>(new ChallengeConfirmModalProps(
                    $"This will remove card {Props.CardId} from the board runtime.",
                    () => HandleDeleteCard(),
                    () =>
                    {
                        if (deletePending)
                        {
                            return;
                        }

                        setDeleteConfirmOpen(false);
                    }))
                : Empty());

        return Component<GlobalModal, GlobalModalProps>(new GlobalModalProps($"Inspect: {Props.Title}", Props.OnClose, body));
    }

    // ----- trial-run output (port of InspectCardOutput + Source/CardFlightContent + FlightLoadingContent) -----

    /// <summary>Renders the latest trial-run / token-inspection result (port of <c>InspectCardOutput</c>).</summary>
    public static Element InspectCardOutput(FlightResult? flightResult, AppTheme theme)
    {
        if (flightResult is null)
        {
            return VStack(4,
                CardPreflight.SectionTitle("Trial Run Output", theme),
                TextBlock("Run the full card preflight or any source-level preflight from the panel above to inspect the live result here.")
                    .FontSize(11).Opacity(0.7).Foreground(theme.TextPrimary)
                    .Set(t => t.TextWrapping = TextWrapping.WrapWholeWords));
        }

        if (flightResult.Kind == "token")
        {
            return VStack(8,
                HStack(8,
                    VStack(2,
                        CardPreflight.SectionTitle("Selected Token", theme),
                        TextBlock(flightResult.Title).FontSize(13).Bold().Foreground(theme.TextPrimary)).Flex(grow: 1),
                    StatusChip(flightResult.Missing ? "missing" : "available", flightResult.Missing ? "failed" : "completed", theme)),
                VStack(6,
                    CardPreflight.WrapChips(new[] { CardPreflight.Chip(flightResult.Token, theme), CardPreflight.Chip(flightResult.TokenKind, theme) }),
                    JsonSection("Current Data", flightResult.Data, theme)));
        }

        string statusLabel = flightResult.Status switch
        {
            "running" => "running",
            "error" => "failed",
            _ => "completed",
        };
        string statusTone = flightResult.Status switch
        {
            "error" => "failed",
            "success" => "completed",
            _ => "running",
        };

        return VStack(8,
            HStack(8,
                VStack(2,
                    CardPreflight.SectionTitle("Latest Trial Run", theme),
                    TextBlock(flightResult.Title).FontSize(13).Bold().Foreground(theme.TextPrimary)).Flex(grow: 1),
                StatusChip(statusLabel, statusTone, theme)),
            flightResult.Kind == "source"
                ? SourceFlightContent(flightResult, theme)
                : CardFlightContent(flightResult, theme));
    }

    private static Element SourceFlightContent(FlightResult flightResult, AppTheme theme)
    {
        if (flightResult.Status == "running")
        {
            return FlightLoadingContent("source", flightResult.Title, theme);
        }

        if (flightResult.Error is not null)
        {
            return TextBlock(flightResult.Error).FontSize(11).Foreground(theme.TextPrimary)
                .Set(t => t.TextWrapping = TextWrapping.WrapWholeWords);
        }

        SourceFlightData data = NormalizeSourceFlightData(flightResult.Data);
        var chips = new List<Element>();
        if (data.BindTo.Length > 0)
        {
            chips.Add(CardPreflight.Chip(data.BindTo, theme));
        }

        chips.Add(StatusChip(data.Ok ? "ok" : "failed", data.Ok ? "completed" : "failed", theme));
        if (data.Issues.Count > 0)
        {
            chips.Add(StatusChip(Pluralize(data.Issues.Count, "issue"), "failed", theme));
        }

        var sections = new List<Element> { CardPreflight.WrapChips(chips.ToArray()) };
        if (data.Issues.Count > 0)
        {
            sections.Add(IssuesList(data.Issues, theme));
        }

        if (data.Result is not null)
        {
            sections.Add(JsonSection("Result", data.Result, theme));
        }

        return VStack(6, sections.ToArray());
    }

    private static Element CardFlightContent(FlightResult flightResult, AppTheme theme)
    {
        if (flightResult.Status == "running")
        {
            return FlightLoadingContent("card", flightResult.Title, theme);
        }

        if (flightResult.Error is not null)
        {
            return TextBlock(flightResult.Error).FontSize(11).Foreground(theme.TextPrimary)
                .Set(t => t.TextWrapping = TextWrapping.WrapWholeWords);
        }

        CardFlightData data = NormalizeCardFlightData(flightResult.Data);
        IReadOnlyList<string> providesKeys = data.ProvidesOutputs.Select(pair => pair.Key).ToList();
        int elementCount = data.RenderedView["elements"] is JsonArray elements ? elements.Count : 0;

        var chips = new List<Element>();
        if (data.CardId.Length > 0)
        {
            chips.Add(CardPreflight.Chip(data.CardId, theme));
        }

        chips.Add(StatusChip(data.Ok ? "ok" : "failed", data.Ok ? "completed" : "failed", theme));
        if (data.Issues.Count > 0)
        {
            chips.Add(StatusChip(Pluralize(data.Issues.Count, "issue"), "failed", theme));
        }

        if (providesKeys.Count > 0)
        {
            chips.Add(CardPreflight.Chip(Pluralize(providesKeys.Count, "provide"), theme));
        }

        if (elementCount > 0)
        {
            chips.Add(CardPreflight.Chip(Pluralize(elementCount, "view element"), theme));
        }

        var sections = new List<Element> { CardPreflight.WrapChips(chips.ToArray()) };
        if (data.Issues.Count > 0)
        {
            sections.Add(VStack(4, CardPreflight.SectionTitle("Issues", theme), IssuesList(data.Issues, theme)));
        }

        if (providesKeys.Count > 0)
        {
            sections.Add(JsonSection("Provides Outputs", data.ProvidesOutputs, theme));
        }

        if (elementCount > 0)
        {
            sections.Add(JsonSection("Rendered View", data.RenderedView, theme));
        }

        return VStack(6, sections.ToArray());
    }

    private static Element FlightLoadingContent(string kind, string title, AppTheme theme)
    {
        string label = kind == "card" ? "card preflight" : "source preflight";
        return VStack(8,
            HStack(8,
                ProgressRing(),
                VStack(2,
                    CardPreflight.SectionTitle($"Running {label}", theme),
                    TextBlock(title).FontSize(13).Foreground(theme.TextPrimary))),
            TextBlock("This can take 20-30 seconds depending on the fetch path. Results stay in this pane when they arrive.")
                .FontSize(11).Opacity(0.7).Foreground(theme.TextPrimary)
                .Set(t => t.TextWrapping = TextWrapping.WrapWholeWords),
            CardPreflight.WrapChips(new[]
            {
                StatusChip("dispatching request", "running", theme),
                CardPreflight.Chip("waiting for remote fetch", theme),
                CardPreflight.Chip("materialising result", theme),
            }));
    }

    private static Element IssuesList(IReadOnlyList<string> issues, AppTheme theme) =>
        VStack(2, issues.Select(issue => (Element)TextBlock($"\u2022 {issue}").FontSize(11).Foreground(theme.TextPrimary)
            .Set(t => t.TextWrapping = TextWrapping.WrapWholeWords)).ToArray());

    private static Element JsonSection(string title, JsonNode? value, AppTheme theme) =>
        VStack(4,
            CardPreflight.SectionTitle(title, theme),
            Border(TextBlock(Pretty(value)).FontSize(11).Foreground(theme.TextPrimary)
                    .Set(t => t.FontFamily = new FontFamily("Consolas")))
                .Padding(8).Background(theme.Layer).CornerRadius(6));

    private static Element StatusChip(string label, string toneStatus, AppTheme theme) =>
        Border(TextBlock(label).FontSize(11).Foreground(BoardTheme.CreateStatusBrush(toneStatus, 0xFF)))
            .Padding(4).Background(theme.LayerAlt).CornerRadius(6);

    // ----- pure helpers (faithful ports of the module-level functions) -----

    /// <summary>The trial-run state (source / card / token), mirroring the web's <c>flightResult</c> object.</summary>
    public sealed record FlightResult(
        string Title,
        string Kind,
        string Status = "",
        JsonNode? Data = null,
        string? Error = null,
        int SourceIndex = -1,
        string Token = "",
        string TokenKind = "",
        bool Missing = false);

    /// <summary>Normalised single-source preflight payload (port of <c>normalizeSourceFlightData</c>).</summary>
    public readonly record struct SourceFlightData(string BindTo, bool Ok, JsonNode? Result, IReadOnlyList<string> Issues);

    /// <summary>Normalised card preflight payload (port of <c>normalizeCardFlightData</c>).</summary>
    public readonly record struct CardFlightData(string CardId, bool Ok, IReadOnlyList<string> Issues, JsonObject ProvidesOutputs, JsonObject RenderedView);

    public static SourceFlightData NormalizeSourceFlightData(JsonNode? data)
    {
        if (data is not JsonObject obj)
        {
            return new SourceFlightData(string.Empty, false, null, Array.Empty<string>());
        }

        return new SourceFlightData(
            GetString(obj, "bindTo"),
            !(obj.TryGetPropertyValue("ok", out JsonNode? ok) && ok is JsonValue okv && okv.TryGetValue(out bool b) && b == false),
            obj.ContainsKey("result") ? obj["result"] : null,
            GetIssues(obj));
    }

    public static CardFlightData NormalizeCardFlightData(JsonNode? data)
    {
        if (data is not JsonObject obj)
        {
            return new CardFlightData(string.Empty, false, Array.Empty<string>(), new JsonObject(), new JsonObject { ["elements"] = new JsonArray() });
        }

        return new CardFlightData(
            GetString(obj, "cardId"),
            !(obj.TryGetPropertyValue("ok", out JsonNode? ok) && ok is JsonValue okv && okv.TryGetValue(out bool b) && b == false),
            GetIssues(obj),
            obj["provides_outputs"] is JsonObject provides ? provides : new JsonObject(),
            obj["rendered_view"] is JsonObject rendered ? rendered : new JsonObject { ["elements"] = new JsonArray() });
    }

    public static string Pluralize(int count, string singular, string? plural = null) =>
        $"{count} {(count == 1 ? singular : plural ?? $"{singular}s")}";

    private static string GetString(JsonObject obj, string key) =>
        obj[key] is JsonValue value && value.TryGetValue(out string? text) ? text : string.Empty;

    private static IReadOnlyList<string> GetIssues(JsonObject obj) =>
        obj["issues"] is JsonArray issues ? issues.Select(Stringify).ToList() : Array.Empty<string>();

    private static string Stringify(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) ? text : node?.ToJsonString() ?? string.Empty;

    private static JsonNode? SafeParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(raw);
        }
        catch (JsonException)
        {
            return JsonValue.Create(raw);
        }
    }

    private static string Pretty(JsonNode? value) =>
        value?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
}
