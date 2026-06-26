using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Hooks;

// =====================================================================================
//  UseCardState — Reactor port of useCardState.js
//
//  Surfaces a single card's resolved definition/data/runtime slice plus the bound
//  data-object dependencies (requires/provides) and the stable action callbacks the
//  card surfaces wire to (refresh / patch / dispatch / upload, and the preflight +
//  discovery MCP authoring actions). The data slice is read in-process from BoardStore
//  (which already resolves requires/provides exactly like the web buildBoardCardState);
//  actions are bound to EmbeddedBoardClient. Components stay presentational.
// =====================================================================================

/// <summary>Stable card action callbacks (port of <c>useCardState</c>'s memoized <c>cardActions</c> object).</summary>
public sealed record CardActions(
    Func<Task> Refresh,
    Func<object, Task> Patch,
    Func<string, object?, Task> DispatchAction,
    Func<NativeAttachmentFile, Task> UploadFileForChat,
    Func<Task<JsonNode?>> DiscoverSourceKinds,
    Func<JsonNode?, Task<JsonNode?>> ValidateCandidateCardDefinition,
    Func<int, IReadOnlyDictionary<string, string>?, Task<JsonNode?>> RunSingleSourceInLiveCard,
    Func<JsonNode?, int, IReadOnlyDictionary<string, string>?, JsonNode?, Task<JsonNode?>> RunSingleSourceInCandidateCard,
    Func<JsonNode?, int, IReadOnlyDictionary<string, string>?, JsonNode?, Task<JsonNode?>> ProbeSingleSourceInCandidateCard,
    Func<JsonNode?, IReadOnlyDictionary<string, string>?, Task<JsonNode?>> RunOneCycleWithCandidateCard);

/// <summary>The full card state object returned by <see cref="HookComponent{TProps}.UseCardState"/>, or null.</summary>
public sealed record CardState(
    string? BoardSseClientId,
    BoardCard? CardContent,
    bool CanRefresh,
    IReadOnlyDictionary<string, string> CardData,
    BoardCard? CardRuntime,
    IReadOnlyDictionary<string, string> RequiresDataObjects,
    IReadOnlyDictionary<string, string> ProvidesDataObjects,
    CardActions CardActions);

public abstract partial class HookComponent<TProps>
{
    private static readonly IReadOnlyDictionary<string, string> EmptyDataObjects =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Port of <c>useCardState</c>: the resolved card slice plus stable action callbacks, or null.</summary>
    protected CardState? UseCardState(string boardId, string cardId)
    {
        BoardStore store = UseBoardStoreSubscription(includeUiState: false);
        EmbeddedBoardClient client = App.Current.BoardClient;
        BoardInfoState boardInfo = store.GetBoardInfo();
        string? boardSseClientId = string.IsNullOrEmpty(boardInfo.ClientId) ? null : boardInfo.ClientId;
        BoardCardState? cardState = string.IsNullOrEmpty(cardId) ? null : store.GetCardState(cardId);

        BoardCard? cardContent = cardState?.CardContent;
        bool canRefresh = cardState?.CanRefresh ?? false;
        IReadOnlyDictionary<string, string> requiresDataObjects = cardState?.RequiresDataObjects ?? EmptyDataObjects;
        string cardContentSignature = cardContent?.RawDefinitionJson ?? string.Empty;
        string requiresSignature = SignatureOfDataObjects(requiresDataObjects);

        CardActions cardActions = UseMemo<CardActions>(
            () => BuildCardActions(client, cardId, canRefresh, cardContent, requiresDataObjects),
            boardId,
            cardId,
            canRefresh,
            cardContentSignature,
            requiresSignature);

        if (cardState is null || string.IsNullOrEmpty(cardId))
        {
            return null;
        }

        return new CardState(
            BoardSseClientId: boardSseClientId,
            CardContent: cardContent,
            CanRefresh: canRefresh,
            CardData: cardState.CardData,
            CardRuntime: cardState.CardRuntime,
            RequiresDataObjects: requiresDataObjects,
            ProvidesDataObjects: cardState.ProvidesDataObjects,
            CardActions: cardActions);
    }

    private static CardActions BuildCardActions(
        EmbeddedBoardClient client,
        string cardId,
        bool canRefresh,
        BoardCard? cardContent,
        IReadOnlyDictionary<string, string> requiresDataObjects)
    {
        JsonNode? CurrentCardContent() =>
            string.IsNullOrWhiteSpace(cardContent?.RawDefinitionJson) ? null : SafeParseNode(cardContent!.RawDefinitionJson);

        return new CardActions(
            Refresh: () => canRefresh ? client.RefreshCardAsync(cardId) : Task.CompletedTask,
            Patch: patch => client.PatchCardAsync(cardId, patch),
            DispatchAction: (type, payload) => client.DispatchActionAsync(cardId, type, payload),
            UploadFileForChat: file => client.AddChatAttachmentAsync(cardId, string.Empty, file),
            DiscoverSourceKinds: async () =>
                UnwrapMcpToolPayload(await client.CallBoardMcpAsync("discover.source-kinds").ConfigureAwait(false)),
            ValidateCandidateCardDefinition: async candidate =>
                UnwrapMcpToolPayload(await client.CallBoardMcpAsync(
                    "preflight.validate-candidate-card-definition",
                    new { candidate_card_content = candidate ?? CurrentCardContent() }).ConfigureAwait(false)),
            RunSingleSourceInLiveCard: async (sourceIndex, mockRequires) =>
                UnwrapMcpToolPayload(await client.CallBoardMcpAsync(
                    "preflight.run-single-source-in-live-card",
                    new
                    {
                        card_id = cardId,
                        source_idx = sourceIndex,
                        mock_requires = ToJsonObject(mockRequires ?? requiresDataObjects),
                    }).ConfigureAwait(false)),
            RunSingleSourceInCandidateCard: (candidate, sourceIndex, mockRequires, mockProjections) =>
                RunCandidateSourceAsync(
                    client,
                    "preflight.run-single-source-in-candidate-card",
                    candidate ?? CurrentCardContent(),
                    sourceIndex,
                    mockRequires ?? requiresDataObjects,
                    mockProjections),
            ProbeSingleSourceInCandidateCard: (candidate, sourceIndex, mockRequires, mockProjections) =>
                RunCandidateSourceAsync(
                    client,
                    "preflight.probe-single-source-in-candidate-card",
                    candidate ?? CurrentCardContent(),
                    sourceIndex,
                    mockRequires ?? requiresDataObjects,
                    mockProjections),
            RunOneCycleWithCandidateCard: async (candidate, mockRequires) =>
                UnwrapMcpToolPayload(await client.CallBoardMcpAsync(
                    "preflight.run-one-cycle-with-candidate-card",
                    new
                    {
                        candidate_card_content = candidate ?? CurrentCardContent(),
                        mock_requires = ToJsonObject(mockRequires ?? requiresDataObjects),
                    }).ConfigureAwait(false)));
    }

    private static async Task<JsonNode?> RunCandidateSourceAsync(
        EmbeddedBoardClient client,
        string tool,
        JsonNode? candidate,
        int sourceIndex,
        IReadOnlyDictionary<string, string> mockRequires,
        JsonNode? mockProjections)
    {
        var args = new JsonObject
        {
            ["candidate_card_content"] = candidate?.DeepClone(),
            ["source_idx"] = sourceIndex,
            ["mock_requires"] = ToJsonObject(mockRequires),
        };

        if (mockProjections is not null)
        {
            args["mock_projections"] = mockProjections.DeepClone();
        }

        return UnwrapMcpToolPayload(await client.CallBoardMcpAsync(tool, args).ConfigureAwait(false));
    }

    /// <summary>
    /// Port of <c>unwrapMcpToolPayload</c>: throw on a <c>status:"fail"</c> envelope, return the inner
    /// <c>data</c> on a <c>status:"success"</c> envelope, otherwise return the payload as-is.
    /// </summary>
    internal static JsonNode? UnwrapMcpToolPayload(string json)
    {
        JsonNode? root = SafeParseNode(json);

        if (root is JsonObject obj
            && obj.TryGetPropertyValue("status", out JsonNode? statusNode)
            && statusNode is JsonValue statusValue
            && statusValue.TryGetValue(out string? status))
        {
            if (string.Equals(status, "fail", StringComparison.Ordinal))
            {
                string? error = obj.TryGetPropertyValue("error", out JsonNode? errorNode)
                    && errorNode is JsonValue errorValue
                    && errorValue.TryGetValue(out string? errorText)
                        ? errorText
                        : null;
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(error) ? "MCP tool request failed" : error!.Trim());
            }

            if (string.Equals(status, "success", StringComparison.Ordinal) && obj.ContainsKey("data"))
            {
                return obj["data"]?.DeepClone();
            }
        }

        return root;
    }

    private static JsonObject ToJsonObject(IReadOnlyDictionary<string, string> values)
    {
        var obj = new JsonObject();
        foreach (KeyValuePair<string, string> entry in values)
        {
            obj[entry.Key] = SafeParseNode(entry.Value) ?? JsonValue.Create(entry.Value);
        }

        return obj;
    }

    private static JsonNode? SafeParseNode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string SignatureOfDataObjects(IReadOnlyDictionary<string, string> data) =>
        data.Count == 0
            ? string.Empty
            : string.Join(
                "\u0001",
                data.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => $"{entry.Key}\u0002{entry.Value}"));
}
