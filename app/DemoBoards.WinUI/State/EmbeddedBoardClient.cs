using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Generic;
using System.Threading.Tasks;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.Config;

namespace DemoBoards_WinUI.State;

/// <summary>
/// WinUI equivalent of frontend lib/client.js. For embedded desktop mode we do
/// not need browser fetch wrappers or Redux; we need a client facade over the
/// in-process runtime host plus the one external HTTP face (agentface).
/// </summary>
public sealed class EmbeddedBoardClient
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new() { WriteIndented = true };

    private readonly DemoBoardsRuntimeService runtimeService;
    private readonly HttpClient runtimeHttpClient;

    public EmbeddedBoardClient(DemoBoardsRuntimeService runtimeService)
    {
        this.runtimeService = runtimeService;
        runtimeHttpClient = new HttpClient
        {
            BaseAddress = new Uri(runtimeService.GetStatus().AgentfaceEndpoint + "/")
        };
    }

    public Task<BoardSnapshot> InitBoardAsync()
    {
        return runtimeService.RefreshAsync();
    }

    public Task<BoardSnapshot> RefreshBoardAsync()
    {
        return runtimeService.RefreshAsync();
    }

    public Task<BoardSnapshot> AddCardAsync(string cardJson)
    {
        return runtimeService.AddCardAsync(cardJson);
    }

    public async Task<string> PostCardToAgentfaceAsync(string cardJson)
    {
        using var response = await runtimeHttpClient.PostAsync("board/cards", new StringContent(cardJson, Encoding.UTF8, "application/json")).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    public async Task<string> CallBoardMcpAsync(string tool, object? args = null)
    {
        var payload = JsonSerializer.Serialize(new { tool, args = args ?? new { } });
        using var response = await runtimeHttpClient.PostAsync("mcp", new StringContent(payload, Encoding.UTF8, "application/json")).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    public Task RefreshCardAsync(string cardId)
    {
        return PostMcpActionsAsync("retrigger-card", new { card_id = cardId });
    }

    public Task PatchCardAsync(string cardId, object patch)
    {
        return PostMcpControlplaneAsync("manage.patch-card", new { card_id = cardId, patch });
    }

    public Task DispatchActionAsync(string cardId, string type, object? payload = null)
    {
        return PostMcpActionsAsync(type, new { card_id = cardId, payload = payload ?? new { } });
    }

    public Task SubscribeCardChatsAsync(string cardId)
    {
        return PostMcpControlplaneAsync("sse.subscribe-chat", new { client_id = "embedded-v8", card_id = cardId });
    }

    public Task UnsubscribeCardChatsAsync(string cardId)
    {
        return PostMcpControlplaneAsync("sse.unsubscribe-chat", new { client_id = "embedded-v8", card_id = cardId });
    }

    public Task SubscribeWatchpartyAsync(string cardId, string channelName)
    {
        return PostMcpControlplaneAsync("sse.watch-channel", new { client_id = "embedded-v8", card_id = cardId, channel_name = channelName });
    }

    public Task UnsubscribeWatchpartyAsync(string cardId, string channelName)
    {
        return PostMcpControlplaneAsync("sse.unwatch-channel", new { client_id = "embedded-v8", card_id = cardId, channel_name = channelName });
    }

    public Task SendChatAsync(string cardId, string text, string turnId)
    {
        return DispatchActionAsync(cardId, "chat-send", new { text, turnId });
    }

    public Task AddChatAttachmentAsync(string cardId, string turnId, NativeAttachmentFile file)
    {
        return PostMcpControlplaneAsync("manage.add-chat-attachment", new
        {
            card_id = cardId,
            turn_id = turnId,
            file_name = file.Name,
            content_type = file.ContentType,
            base64 = Convert.ToBase64String(file.Bytes)
        });
    }

    public Task AddChatEntryAndAnyAttachmentsAsync(string cardId, string text, string turnId, IReadOnlyList<NativeAttachmentFile> files)
    {
        return PostMcpControlplaneAsync("manage.add-chat-entry-and-any-attachments", new
        {
            card_id = cardId,
            role = "user",
            text,
            turn_id = turnId,
            files = (files ?? Array.Empty<NativeAttachmentFile>())
                .Select(file => new
                {
                    file_name = file.Name,
                    content_type = file.ContentType,
                    base64 = Convert.ToBase64String(file.Bytes)
                })
                .ToArray()
        });
    }

    public string GetCardFileUrl(string cardId, int fileIndex, string? storedName = null)
    {
        string query = string.IsNullOrWhiteSpace(storedName)
            ? string.Empty
            : $"?sn={Uri.EscapeDataString(storedName)}";
        return new Uri(runtimeHttpClient.BaseAddress!, $"cards/{Uri.EscapeDataString(cardId)}/files/{fileIndex}{query}").ToString();
    }

    public async Task<ManagedBoardConfigState?> GetManagedBoardConfigAsync(string boardId)
    {
        string normalizedBoardId = boardId?.Trim() ?? string.Empty;
        if (normalizedBoardId.Length == 0)
        {
            return null;
        }

        Task<string?> boardPayloadTask = PostManageBoardsAsync("get-board", new { boardId = normalizedBoardId });
        Task<string?> layoutPayloadTask = PostManageBoardsAsync("get-layout", new { boardId = normalizedBoardId });
        string? boardPayload = await boardPayloadTask.ConfigureAwait(false);
        string? layoutPayload = await layoutPayloadTask.ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(boardPayload))
        {
            return null;
        }

        using JsonDocument boardDocument = JsonDocument.Parse(boardPayload);
        JsonElement boardRoot = boardDocument.RootElement;
        EnsureSuccessPayload(boardRoot, "get-board failed");
        JsonElement board = TryGetNestedProperty(boardRoot, "data", "board");
        if (board.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        JsonElement layout = default;
        if (!string.IsNullOrWhiteSpace(layoutPayload))
        {
            using JsonDocument layoutDocument = JsonDocument.Parse(layoutPayload);
            JsonElement layoutRoot = layoutDocument.RootElement;
            EnsureSuccessPayload(layoutRoot, "get-layout failed");
            layout = TryGetNestedProperty(layoutRoot, "data", "layout");
        }

        JsonElement ui = board.TryGetProperty("ui", out JsonElement uiElement) && uiElement.ValueKind == JsonValueKind.Object
            ? uiElement
            : default;
        JsonElement metadata = board.TryGetProperty("metadata", out JsonElement metadataElement) && metadataElement.ValueKind == JsonValueKind.Object
            ? metadataElement
            : default;

        return new ManagedBoardConfigState(
            ui.ValueKind == JsonValueKind.Object ? ui.GetRawText() : "{}",
            metadata.ValueKind == JsonValueKind.Object ? metadata.GetRawText() : "{}",
            layout.ValueKind == JsonValueKind.Undefined || layout.ValueKind == JsonValueKind.Null ? "null" : layout.GetRawText(),
            board.GetRawText());
    }

    public async Task<ManagedBoardConfigState> SaveManagedBoardConfigAsync(string boardId, string rawUiJson, string rawMetadataJson, string rawLayoutJson, string? currentRawBoardJson = null)
    {
        string normalizedBoardId = boardId?.Trim() ?? string.Empty;
        if (normalizedBoardId.Length == 0)
        {
            throw new InvalidOperationException("Board id is required.");
        }

        JsonObject board = ParseBoardRecord(normalizedBoardId, currentRawBoardJson, rawUiJson, rawMetadataJson);
        await PostManageBoardsAsync("save-board-record", new
        {
            boardId = normalizedBoardId,
            record = board
        }).ConfigureAwait(false);

        JsonNode? layout = JsonNode.Parse(string.IsNullOrWhiteSpace(rawLayoutJson) ? "null" : rawLayoutJson);
        if (layout is JsonObject layoutObject)
        {
            await PostManageBoardsAsync("save-layout", new
            {
                boardId = normalizedBoardId,
                layout = layoutObject
            }).ConfigureAwait(false);
        }

        ManagedBoardConfigState? saved = await GetManagedBoardConfigAsync(normalizedBoardId).ConfigureAwait(false);
        return saved ?? throw new InvalidOperationException("Saved managed board config could not be reloaded.");
    }

    public async Task<IReadOnlyList<ManagedBoardListEntry>> ListManagedBoardsAsync()
    {
        string? payload = await PostManageBoardsAsync("list-boards", new { }).ConfigureAwait(false);
        using JsonDocument document = ParseRequiredJson(payload, "list-boards returned no payload.");
        JsonElement root = document.RootElement;
        EnsureSuccessPayload(root, "list-boards failed");
        JsonElement boards = TryGetNestedProperty(root, "data", "boards");
        if (boards.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ManagedBoardListEntry>();
        }

        var entries = new List<ManagedBoardListEntry>();
        foreach (JsonElement board in boards.EnumerateArray())
        {
            ManagedBoardListEntry? entry = ParseManagedBoardEntry(board);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    public async Task<ManagedBoardListEntry> AddManagedBoardAsync(ManagedBoardCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateManagedBoardCreateRequest(request);

        string? payload = await PostManageBoardsAsync("add-board", new
        {
            boardId = request.BoardId,
            record = new
            {
                label = request.Label,
                ai = request.Ai,
                aiWorkspaceTemplate = request.AiWorkspaceTemplate,
                uiTemplate = request.UiTemplate,
                refsTemplate = request.RefsTemplate,
                metadata = new
                {
                    pageTitle = request.PageTitle,
                    pageSubtitle = request.PageSubtitle,
                }
            }
        }).ConfigureAwait(false);

        using JsonDocument document = ParseRequiredJson(payload, "add-board returned no payload.");
        JsonElement root = document.RootElement;
        EnsureSuccessPayload(root, "add-board failed");
        JsonElement board = TryGetNestedProperty(root, "data", "board");
        return ParseManagedBoardEntry(board) ?? throw new InvalidOperationException("Created board payload was invalid.");
    }

    public async Task<WinUiHostTemplateCatalog> DescribeHostConfigAsync()
    {
        string? payload = await PostManageBoardsAsync("describe-host-config", BuildHostConfigArgs()).ConfigureAwait(false);
        using JsonDocument document = ParseRequiredJson(payload, "describe-host-config returned no payload.");
        JsonElement root = document.RootElement;
        EnsureSuccessPayload(root, "describe-host-config failed");
        JsonElement data = TryGetNestedProperty(root, "data");

        return new WinUiHostTemplateCatalog(
            ParseStringArray(TryGetNestedProperty(data, "assistantNames")),
            ParseStringArray(TryGetNestedProperty(data, "aiWorkspaceTemplateNames")),
            ParseStringArray(TryGetNestedProperty(data, "uiTemplateNames")),
            ParseStringArray(TryGetNestedProperty(data, "refsTemplateNames")),
            GetStringValue(TryGetNestedProperty(data, "hostConfigPath")),
            GetStringValue(TryGetNestedProperty(data, "templatesConfigPath")),
            GetStringValue(TryGetNestedProperty(data, "setupSingleAiWorkspaceScriptPath")),
            GetStringValue(TryGetNestedProperty(data, "sampleTemplateCatalogDir")),
            GetStringValue(TryGetNestedProperty(data, "runtimeBoardsIndexRef")),
            GetStringValue(TryGetNestedProperty(data, "runtimeBoardsLayoutRef")),
            GetStringValue(TryGetNestedProperty(data, "rawHostSummaryJson")));
    }

    public async Task<string> ResolveEffectiveBoardConfigAsync(string boardId, string rawBoardJson)
    {
        string normalizedBoardId = NormalizeRequired(boardId, "Board id is required.");
        JsonNode? record = JsonNode.Parse(NormalizeJsonPayload(rawBoardJson, "Board record is required."));
        string? payload = await PostManageBoardsAsync("resolve-board-config", new Dictionary<string, object?>(BuildHostConfigArgs())
        {
            ["boardId"] = normalizedBoardId,
            ["record"] = record,
        }).ConfigureAwait(false);
        using JsonDocument document = ParseRequiredJson(payload, "resolve-board-config returned no payload.");
        JsonElement root = document.RootElement;
        EnsureSuccessPayload(root, "resolve-board-config failed");
        JsonElement data = TryGetNestedProperty(root, "data");
        JsonElement resolved = TryGetNestedProperty(data, "resolvedBoardConfig");
        return resolved.ValueKind == JsonValueKind.Undefined ? "{}" : JsonSerializer.Serialize(resolved, PrettyJsonOptions);
    }

    public async Task SetupBoardWorkspaceAsync(string boardId)
    {
        string normalizedBoardId = NormalizeRequired(boardId, "Board id is required.");
        string? payload = await PostManageBoardsAsync("setup-board-workspace", new Dictionary<string, object?>(BuildHostConfigArgs())
        {
            ["boardId"] = normalizedBoardId,
        }).ConfigureAwait(false);
        using JsonDocument document = ParseRequiredJson(payload, "setup-board-workspace returned no payload.");
        EnsureSuccessPayload(document.RootElement, "setup-board-workspace failed");
    }

    public async Task<IReadOnlyList<SampleTemplateEntry>> ListSampleTemplatesAsync()
    {
        string payload = await CallBoardMcpAsync("explore.list-sample-templates").ConfigureAwait(false);
        return ParseMcpDataArray(payload, ParseSampleTemplateEntry);
    }

    public async Task<SampleTemplateEnvelope> GetSampleTemplateAsync(string key)
    {
        string payload = await CallBoardMcpAsync("explore.get-sample-template", new { key }).ConfigureAwait(false);
        using JsonDocument document = ParseRequiredJson(ExtractMcpJsonPayload(payload), "Template payload was empty.");
        JsonElement root = document.RootElement;
        string templateKey = root.TryGetProperty("key", out JsonElement keyElement) && keyElement.ValueKind == JsonValueKind.String
            ? keyElement.GetString() ?? string.Empty
            : string.Empty;
        string label = root.TryGetProperty("label", out JsonElement labelElement) && labelElement.ValueKind == JsonValueKind.String
            ? labelElement.GetString() ?? string.Empty
            : string.Empty;
        string description = root.TryGetProperty("description", out JsonElement descriptionElement) && descriptionElement.ValueKind == JsonValueKind.String
            ? descriptionElement.GetString() ?? string.Empty
            : string.Empty;
        string rawPayloadJson = root.TryGetProperty("payload", out JsonElement payloadElement)
            ? payloadElement.GetRawText()
            : "[]";
        return new SampleTemplateEnvelope(templateKey, label, description, rawPayloadJson);
    }

    public async Task<BoardImportPreview> PreviewImportBoardAsync(string boardId, string payloadJson, string mode)
    {
        string normalizedBoardId = NormalizeRequired(boardId, "Board id is required.");
        JsonNode? payloadNode = JsonNode.Parse(NormalizeJsonPayload(payloadJson, "Import payload is required."));
        string? payload = await PostManageBoardsAsync("preview-import-board", new
        {
            boardId = normalizedBoardId,
            payload = payloadNode,
            mode = string.IsNullOrWhiteSpace(mode) ? "replace" : mode
        }).ConfigureAwait(false);
        using JsonDocument document = ParseRequiredJson(payload, "preview-import-board returned no payload.");
        JsonElement root = document.RootElement;
        EnsureSuccessPayload(root, "preview-import-board failed");
        JsonElement preview = TryGetNestedProperty(root, "data", "preview");
        return ParseBoardImportPreview(preview);
    }

    public async Task ApplyImportBoardAsync(string boardId, string payloadJson, string mode, bool applyBoardMetadata)
    {
        string normalizedBoardId = NormalizeRequired(boardId, "Board id is required.");
        JsonNode? payloadNode = JsonNode.Parse(NormalizeJsonPayload(payloadJson, "Import payload is required."));
        string? payload = await PostManageBoardsAsync("apply-import-board", new
        {
            boardId = normalizedBoardId,
            payload = payloadNode,
            mode = string.IsNullOrWhiteSpace(mode) ? "replace" : mode,
            applyBoardMetadata
        }).ConfigureAwait(false);
        using JsonDocument document = ParseRequiredJson(payload, "apply-import-board returned no payload.");
        EnsureSuccessPayload(document.RootElement, "apply-import-board failed");
    }

    public async Task<string> ExportBoardAsync(string boardId)
    {
        string normalizedBoardId = NormalizeRequired(boardId, "Board id is required.");
        string? payload = await PostManageBoardsAsync("export-board", new { boardId = normalizedBoardId }).ConfigureAwait(false);
        using JsonDocument document = ParseRequiredJson(payload, "export-board returned no payload.");
        JsonElement root = document.RootElement;
        EnsureSuccessPayload(root, "export-board failed");
        JsonElement exportPayload = TryGetNestedProperty(root, "data", "payload");
        return JsonSerializer.Serialize(exportPayload, PrettyJsonOptions);
    }

    public async Task RefreshManagedBoardAsync(string boardId)
    {
        string normalizedBoardId = NormalizeRequired(boardId, "Board id is required.");
        string? payload = await PostManageBoardsAsync("refresh-board", new { boardId = normalizedBoardId }).ConfigureAwait(false);
        using JsonDocument document = ParseRequiredJson(payload, "refresh-board returned no payload.");
        EnsureSuccessPayload(document.RootElement, "refresh-board failed");
    }

    public Task SaveLayoutAsync(string boardId, BoardCanvasLayoutState layoutState)
    {
        string normalizedBoardId = boardId?.Trim() ?? string.Empty;
        if (normalizedBoardId.Length == 0)
        {
            return Task.CompletedTask;
        }

        return PostManageBoardsAsync("save-layout", new
        {
            boardId = normalizedBoardId,
            layout = new
            {
                canvas = new
                {
                    cardIds = layoutState.CardIds,
                    positions = layoutState.Positions.ToDictionary(
                        entry => entry.Key,
                        entry => new { x = entry.Value.X, y = entry.Value.Y },
                        StringComparer.Ordinal),
                    widths = layoutState.Widths,
                    viewport = layoutState.Viewport is null
                        ? null
                        : new { x = layoutState.Viewport.X, y = layoutState.Viewport.Y, zoom = layoutState.Viewport.Zoom }
                }
            }
        });
    }

    private Task PostMcpActionsAsync(string tool, object args)
    {
        return PostJsonAsync("mcp-actions", new { tool, args });
    }

    private Task PostMcpControlplaneAsync(string tool, object args)
    {
        return PostJsonAsync("mcp-controlplane", new { tool, args });
    }

    private async Task PostJsonAsync(string relativePath, object payload)
    {
        string json = JsonSerializer.Serialize(payload);
        using var response = await runtimeHttpClient.PostAsync(relativePath, new StringContent(json, Encoding.UTF8, "application/json")).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        _ = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    private async Task<string?> PostManageBoardsAsync(string subcommand, object args)
    {
        string json = JsonSerializer.Serialize(new { subcommand, args });
        using var response = await runtimeHttpClient.PostAsync("manage-boards", new StringContent(json, Encoding.UTF8, "application/json")).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    private static Dictionary<string, object?> BuildHostConfigArgs()
    {
        WinUiBackendAppConfig backend = App.Current.HostConfig.Backend;
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["hostConfigPath"] = backend.HostConfigPath,
            ["localFsConfigLoaderPath"] = backend.LocalFsConfigLoaderPath,
            ["templatesConfigPath"] = backend.TemplatesConfigPath,
            ["assistantRegistryPath"] = backend.AssistantRegistryPath,
            ["setupSingleAiWorkspaceScriptPath"] = backend.SetupSingleAiWorkspaceScriptPath,
        };
    }

    private static IReadOnlyList<string> ParseStringArray(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
    }

    private static string GetStringValue(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static JsonObject ParseBoardRecord(string boardId, string? currentRawBoardJson, string rawUiJson, string rawMetadataJson)
    {
        JsonObject board = JsonNode.Parse(currentRawBoardJson ?? string.Empty) as JsonObject ?? new JsonObject();
        JsonNode? ui = JsonNode.Parse(string.IsNullOrWhiteSpace(rawUiJson) ? "{}" : rawUiJson);
        JsonNode? metadata = JsonNode.Parse(string.IsNullOrWhiteSpace(rawMetadataJson) ? "{}" : rawMetadataJson);
        if (ui is not JsonObject uiObject)
        {
            throw new InvalidOperationException("ui must be a JSON object.");
        }

        if (metadata is not JsonObject metadataObject)
        {
            throw new InvalidOperationException("metadata must be a JSON object.");
        }

        board["id"] = boardId;
        board["ui"] = uiObject;
        board["metadata"] = metadataObject;
        return board;
    }

    private static void EnsureSuccessPayload(JsonElement root, string fallbackMessage)
    {
        string status = root.TryGetProperty("status", out JsonElement statusElement) && statusElement.ValueKind == JsonValueKind.String
            ? statusElement.GetString() ?? string.Empty
            : string.Empty;
        if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string message = root.TryGetProperty("error", out JsonElement errorElement) && errorElement.ValueKind == JsonValueKind.String
            ? errorElement.GetString() ?? fallbackMessage
            : fallbackMessage;
        throw new InvalidOperationException(message);
    }

    private static JsonElement TryGetNestedProperty(JsonElement element, string parentName, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(parentName, out JsonElement parent)
            && parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(propertyName, out JsonElement property))
        {
            return property;
        }

        return default;
    }

    private static JsonElement TryGetNestedProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out JsonElement property))
        {
            return property;
        }

        return default;
    }

    private static JsonDocument ParseRequiredJson(string? rawJson, string fallbackMessage)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            throw new InvalidOperationException(fallbackMessage);
        }

        return JsonDocument.Parse(rawJson);
    }

    private static string NormalizeRequired(string? value, string message)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException(message);
        }

        return normalized;
    }

    private static string NormalizeJsonPayload(string? value, string message)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException(message);
        }

        return normalized;
    }

    private static void ValidateManagedBoardCreateRequest(ManagedBoardCreateRequest request)
    {
        _ = NormalizeRequired(request.BoardId, "Board id is required.");
        _ = NormalizeRequired(request.Label, "Label is required.");
        _ = NormalizeRequired(request.PageTitle, "Page title is required.");
        _ = NormalizeRequired(request.PageSubtitle, "Page subtitle is required.");
        _ = NormalizeRequired(request.Ai, "AI is required.");
        _ = NormalizeRequired(request.AiWorkspaceTemplate, "AI workspace template is required.");
        _ = NormalizeRequired(request.UiTemplate, "UI template is required.");
        _ = NormalizeRequired(request.RefsTemplate, "Refs template is required.");
    }

    private static ManagedBoardListEntry? ParseManagedBoardEntry(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string id = element.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        string label = element.TryGetProperty("label", out JsonElement labelElement) && labelElement.ValueKind == JsonValueKind.String
            ? labelElement.GetString() ?? string.Empty
            : string.Empty;
        return new ManagedBoardListEntry(id, string.IsNullOrWhiteSpace(label) ? id : label);
    }

    private static SampleTemplateEntry? ParseSampleTemplateEntry(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string key = element.TryGetProperty("key", out JsonElement keyElement) && keyElement.ValueKind == JsonValueKind.String
            ? keyElement.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        string label = element.TryGetProperty("label", out JsonElement labelElement) && labelElement.ValueKind == JsonValueKind.String
            ? labelElement.GetString() ?? string.Empty
            : string.Empty;
        string description = element.TryGetProperty("description", out JsonElement descriptionElement) && descriptionElement.ValueKind == JsonValueKind.String
            ? descriptionElement.GetString() ?? string.Empty
            : string.Empty;
        return new SampleTemplateEntry(key, string.IsNullOrWhiteSpace(label) ? key : label, description);
    }

    private static BoardImportPreview ParseBoardImportPreview(JsonElement preview)
    {
        return new BoardImportPreview(
            ParseStringArray(preview, "replaceIds"),
            ParseStringArray(preview, "addIds"),
            ParseInvalidCardArray(preview, "invalidCards"));
    }

    private static IReadOnlyList<string> ParseStringArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out JsonElement array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static IReadOnlyList<InvalidBoardImportCard> ParseInvalidCardArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out JsonElement array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<InvalidBoardImportCard>();
        }

        var result = new List<InvalidBoardImportCard>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string id = item.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.String
                ? idElement.GetString() ?? string.Empty
                : string.Empty;
            string title = item.TryGetProperty("title", out JsonElement titleElement) && titleElement.ValueKind == JsonValueKind.String
                ? titleElement.GetString() ?? string.Empty
                : string.Empty;
            IReadOnlyList<string> issues = ParseStringArray(item, "issues");
            result.Add(new InvalidBoardImportCard(id, title, issues));
        }

        return result;
    }

    private static IReadOnlyList<T> ParseMcpDataArray<T>(string rawPayload, Func<JsonElement, T?> parser) where T : class
    {
        string rawJson = ExtractMcpJsonPayload(rawPayload);
        using JsonDocument document = ParseRequiredJson(rawJson, "MCP payload was empty.");
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<T>();
        }

        var result = new List<T>();
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            T? parsed = parser(item);
            if (parsed is not null)
            {
                result.Add(parsed);
            }
        }

        return result;
    }

    private static string ExtractMcpJsonPayload(string payload)
    {
        using JsonDocument document = ParseRequiredJson(payload, "MCP payload was empty.");
        JsonElement root = document.RootElement;
        JsonElement content = TryGetNestedProperty(root, "result", "content");
        if (content.ValueKind != JsonValueKind.Array || content.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("MCP payload content was empty.");
        }

        JsonElement first = content[0];
        if (first.ValueKind == JsonValueKind.Object
            && first.TryGetProperty("type", out JsonElement typeElement)
            && typeElement.ValueKind == JsonValueKind.String
            && string.Equals(typeElement.GetString(), "text", StringComparison.OrdinalIgnoreCase)
            && first.TryGetProperty("text", out JsonElement textElement)
            && textElement.ValueKind == JsonValueKind.String)
        {
            return textElement.GetString() ?? string.Empty;
        }

        throw new InvalidOperationException("MCP payload did not contain text content.");
    }

}

public sealed record ManagedBoardListEntry(string Id, string Label);

public sealed record ManagedBoardCreateRequest(
    string BoardId,
    string Label,
    string PageTitle,
    string PageSubtitle,
    string Ai,
    string AiWorkspaceTemplate,
    string UiTemplate,
    string RefsTemplate,
    string TemplateKey);

public sealed record SampleTemplateEntry(string Key, string Label, string Description);

public sealed record SampleTemplateEnvelope(string Key, string Label, string Description, string RawPayloadJson);

public sealed record InvalidBoardImportCard(string Id, string Title, IReadOnlyList<string> Issues);

public sealed record BoardImportPreview(
    IReadOnlyList<string> ReplaceIds,
    IReadOnlyList<string> AddIds,
    IReadOnlyList<InvalidBoardImportCard> InvalidCards);
