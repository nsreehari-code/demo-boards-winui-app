using System.Collections.Generic;

namespace DemoBoards.RuntimeHost;

public sealed record BoardCardField(string Key, string Value);

public sealed record BoardChatMessage(
    string Role,
    string Text,
    string Turn,
    bool Processing);

public sealed record BoardWatchpartyToolPayload(
    string Tool,
    string Action,
    string CardId,
    string TurnId,
    int? FileIndex);

public sealed record BoardWatchpartyState(
    string AgentOutput,
    string AgentTools,
    IReadOnlyList<BoardWatchpartyToolPayload> AgentToolPayloads);

public sealed record BoardRenderElement(
    string Kind,
    string Label,
    string ClassName,
    string Visible,
    string RawJson);

public sealed record BoardSourceDefinition(
    string BindTo,
    IReadOnlyList<BoardCardField> DetailFields);

public sealed record BoardCard(
    string Id,
    string Title,
    string Status,
    IReadOnlyDictionary<string, string> MetaValues,
    IReadOnlyList<BoardCardField> Fields,
    IReadOnlyList<BoardCardField> ComputedValues,
    IReadOnlyList<string> Requires,
    IReadOnlyList<string> Provides,
    IReadOnlyList<string> ViewKinds,
    IReadOnlyList<BoardRenderElement> ViewElements,
    IReadOnlyList<BoardSourceDefinition> SourceDefinitions,
    IReadOnlyList<BoardChatMessage> ChatMessages,
    bool ChatReceiving,
    bool ChatProcessing,
    string RawDefinitionJson,
    string RawRuntimeJson,
    string SchemaVersion);
