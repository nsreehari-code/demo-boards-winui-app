using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.UI.Reactor.Core;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.Controls;
using DemoBoards_WinUI.Controls.Registry;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards_WinUI.Hooks;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI;

/// <summary>
/// Pure-logic parity harness (run via <c>--hooks-harness</c>) for the hook support code that has no UI
/// surface: the <see cref="ChatMessages"/> helpers ported from <c>chatMessages.js</c> — live/history
/// accumulation, turn-id utilities, the scalar effect signature, and the MCP payload parse — plus the
/// runtime-card / managed-config payload helpers (UnwrapRuntimeCardPayload, ParseObjectOrEmpty/Null).
/// Unlike the RenderHarness it needs no window or board store; it just runs deterministic assertions and
/// prints <c>[i/N] PASS/FAIL</c> lines plus a final banner, mirroring the other harnesses.
/// </summary>
internal static class HooksHarness
{
    public static void RunAndExit()
    {
        var checks = new List<(string Name, Func<bool> Run)>();

        // ---- MakeTurnId / GetMessageTurnId ---------------------------------------
        checks.Add(("MakeTurnId returns a 6-char token", () =>
        {
            string id = ChatMessages.MakeTurnId();
            return id.Length == 6 && id.All(Uri.IsHexDigit);
        }));

        checks.Add(("MakeTurnId is effectively unique per call", () =>
            ChatMessages.MakeTurnId() != ChatMessages.MakeTurnId()));

        checks.Add(("GetMessageTurnId trims and tolerates null", () =>
            ChatMessages.GetMessageTurnId(Map(("turn", "  t1  "))) == "t1"
            && ChatMessages.GetMessageTurnId(null) == string.Empty
            && ChatMessages.GetMessageTurnId(Map(("role", "user"))) == string.Empty));

        // ---- GetFirstTurnId / CountDistinctTurns ---------------------------------
        checks.Add(("GetFirstTurnId returns the first non-empty turn", () =>
            ChatMessages.GetFirstTurnId(new[]
            {
                Map(("turn", "")),
                Map(("turn", "t2")),
                Map(("turn", "t3")),
            }) == "t2"));

        checks.Add(("GetFirstTurnId is empty for an empty list", () =>
            ChatMessages.GetFirstTurnId(Array.Empty<IReadOnlyDictionary<string, object?>>()) == string.Empty));

        checks.Add(("CountDistinctTurns counts non-empty distinct turns", () =>
            ChatMessages.CountDistinctTurns(new[]
            {
                Map(("turn", "t1")),
                Map(("turn", "t1")),
                Map(("turn", "t2")),
                Map(("turn", "")),
            }) == 2));

        // ---- SignatureOf ---------------------------------------------------------
        checks.Add(("SignatureOf is empty for an empty list", () =>
            ChatMessages.SignatureOf(Array.Empty<IReadOnlyDictionary<string, object?>>()) == string.Empty));

        checks.Add(("SignatureOf is stable for identical content", () =>
        {
            var a = new[] { Map(("turn", "t1"), ("role", "user"), ("text", "hi")) };
            var b = new[] { Map(("turn", "t1"), ("role", "user"), ("text", "hi")) };
            return ChatMessages.SignatureOf(a) == ChatMessages.SignatureOf(b);
        }));

        checks.Add(("SignatureOf changes when text changes", () =>
        {
            var a = new[] { Map(("turn", "t1"), ("role", "assistant"), ("text", "one")) };
            var b = new[] { Map(("turn", "t1"), ("role", "assistant"), ("text", "two")) };
            return ChatMessages.SignatureOf(a) != ChatMessages.SignatureOf(b);
        }));

        checks.Add(("SignatureOf changes when processing flips", () =>
        {
            var a = new[] { Map(("turn", "t1"), ("role", "assistant"), ("text", "x"), ("processing", false)) };
            var b = new[] { Map(("turn", "t1"), ("role", "assistant"), ("text", "x"), ("processing", true)) };
            return ChatMessages.SignatureOf(a) != ChatMessages.SignatureOf(b);
        }));

        // ---- MergeLiveMessages ---------------------------------------------------
        checks.Add(("MergeLiveMessages returns prev unchanged for empty incoming", () =>
        {
            IReadOnlyList<LiveChatEntry> prev = ChatMessages.MergeLiveMessages(
                Array.Empty<LiveChatEntry>(),
                new[] { Map(("turn", "t1"), ("role", "user")) });
            IReadOnlyList<LiveChatEntry> next = ChatMessages.MergeLiveMessages(
                prev,
                Array.Empty<IReadOnlyDictionary<string, object?>>());
            return ReferenceEquals(next, prev);
        }));

        checks.Add(("MergeLiveMessages appends new turns in order", () =>
        {
            IReadOnlyList<LiveChatEntry> merged = ChatMessages.MergeLiveMessages(
                Array.Empty<LiveChatEntry>(),
                new[]
                {
                    Map(("turn", "t1"), ("role", "user"), ("text", "a")),
                    Map(("turn", "t1"), ("role", "assistant"), ("text", "b")),
                    Map(("turn", "t2"), ("role", "user"), ("text", "c")),
                });
            return merged.Count == 3
                && merged[0].Key == "t1|user|0"
                && merged[1].Key == "t1|assistant|0"
                && merged[2].Key == "t2|user|0";
        }));

        checks.Add(("MergeLiveMessages disambiguates repeated turn+role by occurrence", () =>
        {
            IReadOnlyList<LiveChatEntry> merged = ChatMessages.MergeLiveMessages(
                Array.Empty<LiveChatEntry>(),
                new[]
                {
                    Map(("turn", "t1"), ("role", "user"), ("text", "first")),
                    Map(("turn", "t1"), ("role", "user"), ("text", "second")),
                });
            return merged.Count == 2 && merged[0].Key == "t1|user|0" && merged[1].Key == "t1|user|1";
        }));

        checks.Add(("MergeLiveMessages updates an existing turn in place (streaming)", () =>
        {
            IReadOnlyList<LiveChatEntry> seed = ChatMessages.MergeLiveMessages(
                Array.Empty<LiveChatEntry>(),
                new[] { Map(("turn", "t1"), ("role", "assistant"), ("text", "partial")) });
            IReadOnlyDictionary<string, object?> updated = Map(("turn", "t1"), ("role", "assistant"), ("text", "complete"));
            IReadOnlyList<LiveChatEntry> merged = ChatMessages.MergeLiveMessages(seed, new[] { updated });
            return merged.Count == 1
                && !ReferenceEquals(merged, seed)
                && ReferenceEquals(merged[0].Msg, updated);
        }));

        checks.Add(("MergeLiveMessages returns prev when the same message refs reappear", () =>
        {
            IReadOnlyDictionary<string, object?> msg = Map(("turn", "t1"), ("role", "user"), ("text", "same"));
            IReadOnlyList<LiveChatEntry> seed = ChatMessages.MergeLiveMessages(
                Array.Empty<LiveChatEntry>(),
                new[] { msg });
            IReadOnlyList<LiveChatEntry> merged = ChatMessages.MergeLiveMessages(seed, new[] { msg });
            return ReferenceEquals(merged, seed);
        }));

        // ---- MergeMessageArrays --------------------------------------------------
        checks.Add(("MergeMessageArrays concatenates existing then incoming, de-duplicated", () =>
        {
            var existing = new[]
            {
                Map(("turn", "t1"), ("role", "user"), ("text", "a")),
                Map(("turn", "t1"), ("role", "assistant"), ("text", "b")),
            };
            var incoming = new[]
            {
                Map(("turn", "t1"), ("role", "assistant"), ("text", "b2")),
                Map(("turn", "t2"), ("role", "user"), ("text", "c")),
            };
            IReadOnlyList<IReadOnlyDictionary<string, object?>> merged = ChatMessages.MergeMessageArrays(existing, incoming);
            return merged.Count == 3
                && ChatMessages.GetMessageTurnId(merged[0]) == "t1"
                && (string?)merged[1]["text"] == "b2"
                && ChatMessages.GetMessageTurnId(merged[2]) == "t2";
        }));

        // ---- ParseChatMessagesPayload -------------------------------------------
        checks.Add(("ParseChatMessagesPayload unwraps a status:success envelope", () =>
        {
            IReadOnlyList<IReadOnlyDictionary<string, object?>> parsed = ChatMessages.ParseChatMessagesPayload(
                "{\"status\":\"success\",\"data\":{\"messages\":[" +
                "{\"role\":\"user\",\"text\":\"hello\",\"turn\":\"t1\"}," +
                "{\"role\":\"assistant\",\"text\":\"hi\",\"turn\":\"t1\",\"processing\":true}]}}");
            return parsed.Count == 2
                && (string?)parsed[0]["role"] == "user"
                && (string?)parsed[0]["text"] == "hello"
                && (string?)parsed[0]["turn"] == "t1"
                && parsed[0]["processing"] is false
                && parsed[1]["processing"] is true;
        }));

        checks.Add(("ParseChatMessagesPayload reads a bare messages object", () =>
        {
            IReadOnlyList<IReadOnlyDictionary<string, object?>> parsed = ChatMessages.ParseChatMessagesPayload(
                "{\"messages\":[{\"role\":\"system\",\"text\":\"note\",\"turn\":\"t9\"}]}");
            return parsed.Count == 1 && (string?)parsed[0]["role"] == "system" && (string?)parsed[0]["turn"] == "t9";
        }));

        checks.Add(("ParseChatMessagesPayload is empty for invalid/empty/missing input", () =>
            ChatMessages.ParseChatMessagesPayload("not json").Count == 0
            && ChatMessages.ParseChatMessagesPayload(string.Empty).Count == 0
            && ChatMessages.ParseChatMessagesPayload(null).Count == 0
            && ChatMessages.ParseChatMessagesPayload("{\"status\":\"success\",\"data\":{}}").Count == 0));

        // ---- UnwrapMcpToolPayload (useCardState) ---------------------------------
        checks.Add(("UnwrapMcpToolPayload returns the inner data for a success envelope", () =>
        {
            JsonNode? data = HookComponent<InfiniteCanvasProps>.UnwrapMcpToolPayload(
                "{\"status\":\"success\",\"data\":{\"value\":42}}");
            return data is JsonObject && data["value"]?.GetValue<int>() == 42;
        }));

        checks.Add(("UnwrapMcpToolPayload throws the trimmed error text for a fail envelope", () =>
        {
            try
            {
                HookComponent<InfiniteCanvasProps>.UnwrapMcpToolPayload("{\"status\":\"fail\",\"error\":\"  boom  \"}");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message == "boom";
            }
        }));

        checks.Add(("UnwrapMcpToolPayload throws a default message when fail carries no error", () =>
        {
            try
            {
                HookComponent<InfiniteCanvasProps>.UnwrapMcpToolPayload("{\"status\":\"fail\"}");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message == "MCP tool request failed";
            }
        }));

        checks.Add(("UnwrapMcpToolPayload returns the payload as-is when there is no envelope", () =>
        {
            JsonNode? data = HookComponent<InfiniteCanvasProps>.UnwrapMcpToolPayload("{\"rows\":[1,2,3]}");
            return data is JsonObject obj && obj["rows"] is JsonArray rows && rows.Count == 3;
        }));

        checks.Add(("UnwrapMcpToolPayload returns the root when a success envelope lacks data", () =>
        {
            JsonNode? data = HookComponent<InfiniteCanvasProps>.UnwrapMcpToolPayload("{\"status\":\"success\"}");
            return data is JsonObject obj && obj["status"]?.GetValue<string>() == "success";
        }));

        checks.Add(("UnwrapMcpToolPayload returns null for invalid JSON", () =>
            HookComponent<InfiniteCanvasProps>.UnwrapMcpToolPayload("not json") is null));

        // ---- UnwrapRuntimeCardPayload (useRuntimeCards) --------------------------
        checks.Add(("UnwrapRuntimeCardPayload returns the data node for a success envelope", () =>
        {
            JsonNode? data = HookComponent<InfiniteCanvasProps>.UnwrapRuntimeCardPayload(
                JsonNode.Parse("{\"status\":\"success\",\"data\":{\"id\":\"c1\"}}"), "upsertRuntimeCard");
            return data is JsonObject obj && obj["id"]?.GetValue<string>() == "c1";
        }));

        checks.Add(("UnwrapRuntimeCardPayload returns null when success carries no data", () =>
            HookComponent<InfiniteCanvasProps>.UnwrapRuntimeCardPayload(
                JsonNode.Parse("{\"status\":\"success\"}"), "upsertRuntimeCard") is null));

        checks.Add(("UnwrapRuntimeCardPayload throws the trimmed error for a fail envelope", () =>
        {
            try
            {
                HookComponent<InfiniteCanvasProps>.UnwrapRuntimeCardPayload(
                    JsonNode.Parse("{\"status\":\"fail\",\"error\":\"  nope  \"}"), "upsertRuntimeCard");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message == "nope";
            }
        }));

        checks.Add(("UnwrapRuntimeCardPayload throws a label-default when fail carries no error", () =>
        {
            try
            {
                HookComponent<InfiniteCanvasProps>.UnwrapRuntimeCardPayload(
                    JsonNode.Parse("{\"status\":\"fail\"}"), "removeRuntimeCard");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message == "removeRuntimeCard failed";
            }
        }));

        checks.Add(("UnwrapRuntimeCardPayload returns the payload when there is no status", () =>
        {
            JsonNode? data = HookComponent<InfiniteCanvasProps>.UnwrapRuntimeCardPayload(
                JsonNode.Parse("{\"rows\":[1,2]}"), "listRuntimeCards");
            return data is JsonObject obj && obj["rows"] is JsonArray rows && rows.Count == 2;
        }));

        // ---- ParseObjectOrEmpty / ParseObjectOrNull (managed config) ------------
        checks.Add(("ParseObjectOrEmpty parses a JSON object", () =>
            HookComponent<InfiniteCanvasProps>.ParseObjectOrEmpty("{\"a\":1}")["a"]?.GetValue<int>() == 1));

        checks.Add(("ParseObjectOrEmpty returns an empty object for null/invalid/non-object", () =>
            HookComponent<InfiniteCanvasProps>.ParseObjectOrEmpty(null).Count == 0
            && HookComponent<InfiniteCanvasProps>.ParseObjectOrEmpty("nope").Count == 0
            && HookComponent<InfiniteCanvasProps>.ParseObjectOrEmpty("[1,2]").Count == 0));

        checks.Add(("ParseObjectOrNull returns null for non-object payloads", () =>
            HookComponent<InfiniteCanvasProps>.ParseObjectOrNull("[1,2]") is null
            && HookComponent<InfiniteCanvasProps>.ParseObjectOrNull("  ") is null
            && HookComponent<InfiniteCanvasProps>.ParseObjectOrNull("{\"a\":1}") is JsonObject));

        // ---- RegistryPath (path.js) ---------------------------------------------
        checks.Add(("PathParts splits dotted + bracket indices", () =>
        {
            IReadOnlyList<string> parts = RegistryPath.PathParts("a.b[0].c");
            return parts.Count == 4 && parts[0] == "a" && parts[1] == "b" && parts[2] == "0" && parts[3] == "c";
        }));

        checks.Add(("PathParts is empty for null/empty", () =>
            RegistryPath.PathParts(null).Count == 0 && RegistryPath.PathParts(string.Empty).Count == 0));

        checks.Add(("DeepGet walks nested objects and array indices", () =>
        {
            var source = Map(("a", Map(("b", new List<object?> { Map(("n", 7)) }))));
            return Equals(RegistryPath.DeepGet(source, "a.b[0].n"), 7)
                && RegistryPath.DeepGet(source, "a.missing") is null;
        }));

        checks.Add(("DeepSet returns a clone without mutating the original", () =>
        {
            var source = Map(("x", 1));
            object? result = RegistryPath.DeepSet(source, "y.z", 2);
            return source.Count == 1
                && result is IReadOnlyDictionary<string, object?> map
                && Equals(map["x"], 1)
                && Equals(RegistryPath.DeepGet(map, "y.z"), 2);
        }));

        // ---- RegistryBind (bind.js) ---------------------------------------------
        checks.Add(("ResolveBind reads a namespaced path", () =>
        {
            var ns = Map(("card", Map(("meta", Map(("title", "Hi"))))));
            return (RegistryBind.ResolveBind(ns, "card.meta.title") as string) == "Hi";
        }));

        checks.Add(("ResolveBind returns the whole namespace for a single segment", () =>
        {
            var ns = Map(("boardId", "b1"));
            return (RegistryBind.ResolveBind(ns, "boardId") as string) == "b1";
        }));

        checks.Add(("ResolveBind is null for missing root / empty bind", () =>
            RegistryBind.ResolveBind(Map(("card", 1)), "nope.x") is null
            && RegistryBind.ResolveBind(Map(("card", 1)), string.Empty) is null));

        // ---- RegistryCoerce (coerce.js) -----------------------------------------
        checks.Add(("DeepEqual is structural and order-sensitive", () =>
            RegistryCoerce.DeepEqual(Map(("a", 1)), Map(("a", 1)))
            && !RegistryCoerce.DeepEqual(Map(("a", 1)), Map(("a", 2)))
            && !RegistryCoerce.DeepEqual(Map(("a", 1), ("b", 2)), Map(("b", 2), ("a", 1)))));

        checks.Add(("CoerceUnknownData passes strings, empties null, pretty-prints objects", () =>
            RegistryCoerce.CoerceUnknownData("hi") == "hi"
            && RegistryCoerce.CoerceUnknownData(null) == string.Empty
            && RegistryCoerce.CoerceUnknownData(Map(("a", 1))) == "{\n  \"a\": 1\n}"));

        checks.Add(("Stringify compact matches JSON.stringify", () =>
            RegistryCoerce.Stringify(new List<object?> { 1, "x", true }, 0) == "[1,\"x\",true]"));

        // ---- RegistryThreshold (threshold.js) -----------------------------------
        checks.Add(("ParseThreshold reads operator + value", () =>
        {
            ThresholdExpr? t = RegistryThreshold.ParseThreshold(">= 5");
            return t is not null && t.Op == ">=" && t.Value == 5
                && RegistryThreshold.ParseThreshold("nope") is null;
        }));

        checks.Add(("EvalThreshold compares per operator", () =>
            RegistryThreshold.EvalThreshold(6, ">=5")
            && !RegistryThreshold.EvalThreshold(4, ">=5")
            && RegistryThreshold.EvalThreshold(5, "==5")
            && RegistryThreshold.EvalThreshold(3, "<5")
            && !RegistryThreshold.EvalThreshold(5, "=5")));

        // ---- ComponentRegistry + NodeResolver (registry.js / NodeRenderer.jsx) ---
        Func<NodeProps, Element> stub = _ => null!;

        checks.Add(("Registry lookup/resolve falls back to text", () =>
        {
            ComponentRegistry.RegisterEntries(new[]
            {
                new RegistryEntry("text", stub),
                new RegistryEntry("metric", stub, Meta: new RegistryMeta(ShowLabel: false)),
            });
            return ComponentRegistry.LookupEntry("metric")?.Kind == "metric"
                && ComponentRegistry.LookupEntry("nope") is null
                && ComponentRegistry.ResolveEntry("metric")?.Kind == "metric"
                && ComponentRegistry.ResolveEntry("nope")?.Kind == "text";
        }));

        checks.Add(("Resolve hides a node when its visible-bind is falsy", () =>
        {
            ComponentRegistry.RegisterEntries(new[] { new RegistryEntry("text", stub) });
            var node = new RegistryNode("text", Visible: "flags.ready");
            return !NodeResolver.Resolve(node, Map(("flags", Map(("ready", false)))) ).Visible
                && NodeResolver.Resolve(node, Map(("flags", Map(("ready", true)))) ).Visible
                && NodeResolver.Resolve(new RegistryNode("text"), null).Visible;
        }));

        checks.Add(("Resolve applies a resolveKind redirect", () =>
        {
            ComponentRegistry.RegisterEntries(new[]
            {
                new RegistryEntry("chartish", stub, ResolveKind: (_, _) => "metric"),
                new RegistryEntry("metric", stub),
            });
            NodeResolution res = NodeResolver.Resolve(new RegistryNode("chartish"), null);
            return res.EffectiveKind == "metric" && res.Entry?.Kind == "metric";
        }));

        checks.Add(("Resolve picks variant via node, then resolveVariant, then default", () =>
        {
            ComponentRegistry.RegisterEntries(new[]
            {
                new RegistryEntry("chart", stub, DefaultVariant: "bar",
                    ResolveVariant: (_, data) => data is string s && s.Length > 0 ? "line" : null),
            });
            return NodeResolver.Resolve(new RegistryNode("chart", HasData: true, Data: ""), null).Variant == "bar"
                && NodeResolver.Resolve(new RegistryNode("chart", HasData: true, Data: "x"), null).Variant == "line"
                && NodeResolver.Resolve(new RegistryNode("chart", Variant: "pie", HasData: true, Data: "x"), null).Variant == "pie";
        }));

        checks.Add(("Resolve coerces data when falling back to the text kind", () =>
        {
            ComponentRegistry.RegisterEntries(new[] { new RegistryEntry("text", stub) });
            NodeResolution res = NodeResolver.Resolve(new RegistryNode("totally-unknown", HasData: true, Data: 42d), null);
            return res.Entry?.Kind == "text" && res.IsFallback && res.Data as string == "42";
        }));

        checks.Add(("Resolve reads currentValue from the writeTo bind", () =>
        {
            ComponentRegistry.RegisterEntries(new[] { new RegistryEntry("text", stub) });
            NodeResolution res = NodeResolver.Resolve(
                new RegistryNode("text", WriteTo: "card.name"),
                Map(("card", Map(("name", "Acme")))));
            return res.CurrentValue as string == "Acme";
        }));

        checks.Add(("JsTruthy: empty array truthy; '' / 0 / null falsy", () =>
            NodeResolver.JsTruthy(new List<object?>())
            && !NodeResolver.JsTruthy("")
            && !NodeResolver.JsTruthy(0d)
            && !NodeResolver.JsTruthy(null)
            && NodeResolver.JsTruthy("x")));

        // ---- CardView entries + lib helpers (cardview/index.js, chart.js, fieldConfig.js) -------
        checks.Add(("CardView entries register with the correct meta presets", () =>
        {
            ComponentRegistry.RegisterEntries(CardViewEntries.All);
            RegistryEntry? table = ComponentRegistry.ResolveEntry("table");
            RegistryEntry? metric = ComponentRegistry.ResolveEntry("metric");
            RegistryEntry? selection = ComponentRegistry.ResolveEntry("selection");
            RegistryEntry? actions = ComponentRegistry.ResolveEntry("actions");
            RegistryEntry? chart = ComponentRegistry.ResolveEntry("chart");
            return table?.Meta is { ShowLabel: true, IsReadonly: true }
                && metric?.Meta is { ShowLabel: false, IsReadonly: true }
                && selection?.Meta is { ShowLabel: true, Controlled: "commit" }
                && actions?.Meta is { ShowLabel: true, IsReadonly: false }
                && chart?.DefaultVariant == "bar" && chart.ResolveVariant != null;
        }));

        checks.Add(("query/searchbox and markdown/markup aliases both resolve", () =>
            ComponentRegistry.LookupEntry("query") != null
            && ComponentRegistry.LookupEntry("searchbox") != null
            && ComponentRegistry.LookupEntry("markdown") != null
            && ComponentRegistry.LookupEntry("markup") != null
            && ComponentRegistry.LookupEntry("editable-table") != null
            && ComponentRegistry.LookupEntry("todo") != null));

        checks.Add(("DetectChartType picks pie / line / bar from the first row", () =>
        {
            IReadOnlyList<object?> pie = new List<object?> { Map(("label", "A"), ("value", 1d)) };
            IReadOnlyList<object?> line = new List<object?> { Map(("x", 1d), ("y", 2d)) };
            IReadOnlyList<object?> bar = new List<object?> { Map(("name", "A"), ("count", 2d)) };
            return RegistryChart.DetectChartType(pie) == "pie"
                && RegistryChart.DetectChartType(line) == "line"
                && RegistryChart.DetectChartType(bar) == "bar"
                && RegistryChart.DetectChartType(null) == "bar";
        }));

        checks.Add(("ResolveChartVariant honours spec.chartType, else detection", () =>
        {
            IReadOnlyList<object?> pie = new List<object?> { Map(("label", "A"), ("value", 1d)) };
            return RegistryChart.ResolveChartVariant(Map(("chartType", "line")), pie) == "line"
                && RegistryChart.ResolveChartVariant(Map(), pie) == "pie";
        }));

        checks.Add(("MergeRows clones object rows and empties non-objects", () =>
        {
            IReadOnlyDictionary<string, object?> src = Map(("k", "v"));
            IReadOnlyList<object?> data = new List<object?> { src, 5d };
            IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = RegistryFieldConfig.MergeRows(data);
            return rows.Count == 2
                && !ReferenceEquals(rows[0], src)
                && rows[0]["k"] as string == "v"
                && rows[1].Count == 0
                && RegistryFieldConfig.MergeRows("nope").Count == 0;
        }));

        checks.Add(("BuildEditorSaveValue wraps card_data, else passes through", () =>
        {
            var wrapped = RegistryFieldConfig.BuildEditorSaveValue("card_data", "name", "Acme") as IReadOnlyDictionary<string, object?>;
            return wrapped != null && wrapped["name"] as string == "Acme"
                && RegistryFieldConfig.BuildEditorSaveValue("other", "name", "Acme") as string == "Acme";
        }));

        checks.Add(("GetSingleFieldConfig returns the lone field, options and required", () =>
        {
            IReadOnlyDictionary<string, object?> spec = Map(("fields", Map(
                ("properties", Map(("status", Map(
                    ("title", "Status"),
                    ("enum", new List<object?> { "open", "closed" }))))),
                ("required", new List<object?> { "status" }))));
            SingleFieldConfig? field = RegistryFieldConfig.GetSingleFieldConfig(spec, null, "open", "writeX");
            bool single = field is { FieldKey: "status", IsRequired: true }
                && field.Options.Count == 2 && field.CurrentValue as string == "open";

            IReadOnlyDictionary<string, object?> multi = Map(("fields", Map(
                ("properties", Map(("a", Map()), ("b", Map()))))));
            bool none = RegistryFieldConfig.GetSingleFieldConfig(multi, null, null, null) is null;

            object? cv = Map(("status", "closed"));
            SingleFieldConfig? field2 = RegistryFieldConfig.GetSingleFieldConfig(spec, null, cv, "card_data");
            bool unwrapped = field2?.CurrentValue as string == "closed";

            return single && none && unwrapped;
        }));

        checks.Add(("RegistryJson maps JSON onto the loose object model", () =>
        {
            var d = RegistryJson.Parse("{\"n\":3,\"b\":true,\"s\":\"hi\",\"arr\":[1,2],\"nil\":null,\"obj\":{\"k\":\"v\"}}")
                as IReadOnlyDictionary<string, object?>;
            bool shape = d != null
                && d["n"] is double n && n == 3
                && d["b"] is true
                && d["s"] as string == "hi"
                && d["arr"] is IReadOnlyList<object?> arr && arr.Count == 2 && arr[0] is double
                && d["nil"] == null
                && (d["obj"] as IReadOnlyDictionary<string, object?>)?["k"] as string == "v";
            bool empties = RegistryJson.Parse(null) == null
                && RegistryJson.Parse("not json") == null
                && RegistryJson.ParseOrString("plain") as string == "plain";
            return shape && empties;
        }));

        checks.Add(("CardviewRenderer.BuildNamespaces parses card/card_data/runtime/requires", () =>
        {
            IReadOnlyDictionary<string, object?> ns = CardviewRenderer.BuildNamespaces(
                "b",
                "{\"card_data\":{\"title\":\"T\",\"status\":\"open\"},\"fieldValues\":{\"x\":1}}",
                "{\"computed_values\":{\"score\":7},\"runtime\":{\"phase\":\"idle\"}}",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["tok"] = "{\"a\":1}" });

            bool board = ns["boardId"] as string == "b";
            bool data = (ns["card_data"] as IReadOnlyDictionary<string, object?>)?["status"] as string == "open";
            bool computed = (ns["computed_values"] as IReadOnlyDictionary<string, object?>)?["score"] is double sc && sc == 7;
            bool runtime = (ns["runtime_state"] as IReadOnlyDictionary<string, object?>)?["phase"] as string == "idle";
            bool requires = ((ns["requires"] as IReadOnlyDictionary<string, object?>)?["tok"]
                as IReadOnlyDictionary<string, object?>)?["a"] is double a && a == 1;
            return board && data && computed && runtime && requires;
        }));

        checks.Add(("CardviewRenderer.NormalizeElement resolves contract-B data/spec/writeTo", () =>
        {
            IReadOnlyDictionary<string, object?> ns = Map(("card_data", Map(("status", "open"))));
            IReadOnlyDictionary<string, object?> element = Map(
                ("kind", "metric"),
                ("spec", Map(("format", "x"))),
                ("data", Map(("bind", "card_data.status"))),
                ("writeTo", "card_data.status"));
            CardviewRenderer.Normalized bound = CardviewRenderer.NormalizeElement(ns, element);
            bool dynamicBind = bound.Kind == "metric"
                && BoardData.Str(bound.Spec, "format") == "x"
                && bound.Bind == "card_data.status"
                && bound.WriteTo == "card_data.status"
                && bound.HasData && bound.Data as string == "open";

            CardviewRenderer.Normalized literal = CardviewRenderer.NormalizeElement(
                ns, Map(("kind", "text"), ("data", Map(("value", "lit")))));
            bool staticValue = literal.HasData && literal.Data as string == "lit" && literal.Bind == null;

            return dynamicBind && staticValue;
        }));

        checks.Add(("CardviewRenderer.NormalizeElement resolves ref via descriptor and data-shape", () =>
        {
            IReadOnlyDictionary<string, object?> descriptorView = Map(
                ("kind", "table"),
                ("spec", Map(("columns", new List<object?> { "a" }))),
                ("data", Map(("value", new List<object?> { Map(("a", "1")) }))));
            IReadOnlyDictionary<string, object?> ns = Map(("computed_values", Map(("view", descriptorView))));
            CardviewRenderer.Normalized viaDescriptor = CardviewRenderer.NormalizeElement(
                ns, Map(("kind", "ref"), ("spec", Map(("viewBind", "computed_values.view")))));
            bool descriptor = viaDescriptor.Kind == "table"
                && viaDescriptor.Spec.ContainsKey("columns")
                && viaDescriptor.Data is IReadOnlyList<object?>;

            CardviewRenderer.Normalized asText = CardviewRenderer.NormalizeElement(
                Map(), Map(("kind", "ref"), ("data", Map(("value", "hello")))));
            CardviewRenderer.Normalized asNarrative = CardviewRenderer.NormalizeElement(
                Map(), Map(("kind", "ref")));
            CardviewRenderer.Normalized viaFallback = CardviewRenderer.NormalizeElement(
                Map(), Map(("kind", "ref"), ("spec", Map(("fallbackKind", "badge")))));

            return descriptor
                && asText.Kind == "text"
                && asNarrative.Kind == "narrative"
                && viaFallback.Kind == "badge";
        }));

        checks.Add(("CardviewRenderer.BuildNextCardData merges, deep-sets and replaces", () =>
        {
            IReadOnlyDictionary<string, object?> cardData = Map(("a", "1"), ("notes", "x"));
            var merged = CardviewRenderer.BuildNextCardData(cardData, "card_data", Map(("a", "2")))
                as IReadOnlyDictionary<string, object?>;
            bool mergeOk = merged?["a"] as string == "2" && merged?["notes"] as string == "x";

            var deep = CardviewRenderer.BuildNextCardData(cardData, "card_data.a", "9")
                as IReadOnlyDictionary<string, object?>;
            bool deepOk = deep?["a"] as string == "9";

            bool replaceOk = CardviewRenderer.BuildNextCardData(cardData, "card_data", "scalar") as string == "scalar";

            return mergeOk && deepOk && replaceOk;
        }));

        checks.Add(("CardChrome.NormalizePathState keeps known states and trims rationale", () =>
        {
            var meta = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["path_state"] = "  Dead_Ended ",
                ["path_state_rationale"] = "  ruled out  ",
            };
            bool known = CardChrome.NormalizePathState(meta) == "dead_ended";
            bool rationale = CardChrome.NormalizePathStateRationale(meta) == "ruled out";
            bool unknown = CardChrome.NormalizePathState(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["path_state"] = "active" }) == string.Empty;
            bool none = CardChrome.NormalizePathState(null) == string.Empty
                && CardChrome.NormalizePathStateRationale(null) == string.Empty;
            return known && rationale && unknown && none;
        }));

        checks.Add(("CardChrome.ClampCardWidth clamps to bounds, rounds and honours the viewport", () =>
        {
            bool min = CardChrome.ClampCardWidth(100) == 280;
            bool max = CardChrome.ClampCardWidth(2000) == 960;
            bool round = CardChrome.ClampCardWidth(500.4) == 500;
            bool viewport = CardChrome.ClampCardWidth(900, 600) == 552;
            return min && max && round && viewport;
        }));

        checks.Add(("CardChrome.PathStateDefs map stamps and tones", () =>
            CardChrome.PathStateDefs["suspended"].ToneStatus == "blocked"
            && CardChrome.PathStateDefs["dead_ended"].Stamp == "Ruled out"
            && CardChrome.PathStateDefs["wiped"].ToneStatus == "secondary"));
        checks.Add(("InfiniteCanvasPane.UniqueTokens dedups, drops empties, preserves order", () =>
            InfiniteCanvasPane.UniqueTokens(new[] { "a", "", "b", "a", "c" }).SequenceEqual(new[] { "a", "b", "c" })
            && InfiniteCanvasPane.UniqueTokens(null).Count == 0));

        checks.Add(("InfiniteCanvasPane.GetStatusTone maps known statuses and falls back to fresh", () =>
            InfiniteCanvasPane.GetStatusTone("running") == "board-tone--running"
            && InfiniteCanvasPane.GetStatusTone("completed") == "board-tone--completed"
            && InfiniteCanvasPane.GetStatusTone("failed") == "board-tone--failed"
            && InfiniteCanvasPane.GetStatusTone("blocked") == "board-tone--blocked"
            && InfiniteCanvasPane.GetStatusTone("anything") == "board-tone--fresh"
            && InfiniteCanvasPane.GetStatusTone(null) == "board-tone--fresh"));

        checks.Add(("InfiniteCanvasPane.BuildGraph links providers to consumers and marks active tokens", () =>
        {
            var cards = new Dictionary<string, BoardCard>(StringComparer.Ordinal)
            {
                ["a"] = MakeBoardCard("a", "completed", Array.Empty<string>(), new[] { "tok" }),
                ["b"] = MakeBoardCard("b", "running", new[] { "tok" }, Array.Empty<string>()),
            };
            var dataObjects = new Dictionary<string, string>(StringComparer.Ordinal) { ["tok"] = "value" };
            var graph = InfiniteCanvasPane.BuildGraph(new[] { "a", "b" }, cards, dataObjects);

            bool edge = graph.Edges.Count == 1
                && graph.Edges[0].Id == "a::b::tok"
                && graph.Edges[0].Source == "a"
                && graph.Edges[0].Target == "b"
                && graph.Edges[0].Animated;
            bool adjacency = graph.Incoming["b"].Contains("a") && graph.Outgoing["a"].Contains("b");
            bool active = graph.Cards["a"].ProvidesActive.SequenceEqual(new[] { "tok" });
            bool noSelfEdge = !graph.Edges.Any(e => e.Source == e.Target);
            return edge && adjacency && active && noSelfEdge;
        }));

        checks.Add(("InfiniteCanvasPane.BuildDeterministicCanvasLayout seeds unsaved cards and skips stored", () =>
        {
            var cards = new Dictionary<string, BoardCard>(StringComparer.Ordinal)
            {
                ["a"] = MakeBoardCard("a", "completed", Array.Empty<string>(), new[] { "tok" }),
                ["b"] = MakeBoardCard("b", "fresh", new[] { "tok" }, Array.Empty<string>()),
            };
            var graph = InfiniteCanvasPane.BuildGraph(new[] { "a", "b" }, cards, new Dictionary<string, string>(StringComparer.Ordinal));
            var storedPositions = new Dictionary<string, BoardCanvasPointState>(StringComparer.Ordinal) { ["a"] = new BoardCanvasPointState(10, 20) };
            var storedWidths = new Dictionary<string, double>(StringComparer.Ordinal);

            var layout = InfiniteCanvasPane.BuildDeterministicCanvasLayout(
                new[] { "a", "b" }, cards, graph.Incoming, graph.Outgoing, storedPositions, storedWidths);
            var layout2 = InfiniteCanvasPane.BuildDeterministicCanvasLayout(
                new[] { "a", "b" }, cards, graph.Incoming, graph.Outgoing, storedPositions, storedWidths);

            bool storedSkipped = !layout.ContainsKey("a");
            bool unsavedPlaced = layout.ContainsKey("b");
            bool deterministic = unsavedPlaced && layout2.ContainsKey("b")
                && Math.Abs(layout["b"].X - layout2["b"].X) < 0.0001
                && Math.Abs(layout["b"].Y - layout2["b"].Y) < 0.0001;
            return storedSkipped && unsavedPlaced && deterministic;
        }));

        checks.Add(("RegistryThreshold.ParseThreshold reads a leading numeric prefix like JS parseFloat", () =>
        {
            ThresholdExpr? parsed = RegistryThreshold.ParseThreshold(">= 80%");
            return parsed is not null
                && parsed.Op == ">="
                && Math.Abs(parsed.Value - 80) < 0.0001
                && RegistryThreshold.EvalThreshold(80, ">= 80%")
                && !RegistryThreshold.EvalThreshold(70, ">= 80%");
        }));

        checks.Add(("CardViewShared.FormatFileSize matches the format.js tiers", () =>
            CardViewShared.FormatFileSize(0) == "Unknown size"
            && CardViewShared.FormatFileSize(512.0) == "512 B"
            && CardViewShared.FormatFileSize(2048.0) == "2 KB"
            && CardViewShared.FormatFileSize(5.0 * 1024 * 1024) == "5.0 MB"));

        checks.Add(("InfiniteCanvasPane.BuildDeterministicCanvasLayout trims footprint and is case-sensitive", () =>
        {
            BoardCard trimmed = MakeBoardCard("w", "fresh", Array.Empty<string>(), Array.Empty<string>(),
                rawDefinitionJson: "{\"meta\":{\"presentation\":{\"footprint\":\"  wide  \"}}}");
            BoardCard upper = MakeBoardCard("u", "fresh", Array.Empty<string>(), Array.Empty<string>(),
                rawDefinitionJson: "{\"meta\":{\"presentation\":{\"footprint\":\"WIDE\"}}}");
            var cards = new Dictionary<string, BoardCard>(StringComparer.Ordinal) { ["w"] = trimmed, ["u"] = upper };
            var graph = InfiniteCanvasPane.BuildGraph(new[] { "w", "u" }, cards, new Dictionary<string, string>(StringComparer.Ordinal));
            var layout = InfiniteCanvasPane.BuildDeterministicCanvasLayout(
                new[] { "w", "u" }, cards, graph.Incoming, graph.Outgoing,
                new Dictionary<string, BoardCanvasPointState>(StringComparer.Ordinal),
                new Dictionary<string, double>(StringComparer.Ordinal));
            return Math.Abs(layout["w"].W - 440) < 0.0001   // "  wide  " trimmed -> wide -> 440
                && Math.Abs(layout["u"].W - 360) < 0.0001;  // "WIDE" is a case-sensitive miss -> default 360
        }));

        checks.Add(("BoardRenderer.BuildPaneNodes emits gandalf/truthset/centre with the centre layout kind", () =>
        {
            Func<BoardCardState, bool>[] empty = Array.Empty<Func<BoardCardState, bool>>();
            RendererRule[] rules = Array.Empty<RendererRule>();
            IReadOnlyList<RegistryNode> panes = BoardRenderer.BuildPaneNodes("board-1", empty, empty, empty, "flowing-cards", rules);
            if (panes.Count != 3 || !panes.All(node => node.Kind == "pane"))
            {
                return false;
            }

            string PaneKind(int index) => panes[index].Spec?["paneKind"] as string ?? string.Empty;
            bool order = PaneKind(0) == "gandalf" && PaneKind(1) == "truthset" && PaneKind(2) == "centre";
            bool centreLayout = panes[2].Spec?["layoutStrategy"] as string == "flowing-cards";
            bool centreExcludes = panes[2].Spec!.ContainsKey("excludeFilters") && !panes[2].Spec!.ContainsKey("includeFilters");
            bool railIncludes = panes[0].Spec!.ContainsKey("includeFilters") && !panes[0].Spec!.ContainsKey("excludeFilters");
            return order && centreLayout && centreExcludes && railIncludes;
        }));

        checks.Add(("PaneRenderer.PaneIsHidden hides empty rails but never the centre", () =>
            PaneRenderer.PaneIsHidden("gandalf", 0)
            && PaneRenderer.PaneIsHidden("truthset", 0)
            && !PaneRenderer.PaneIsHidden("gandalf", 2)
            && !PaneRenderer.PaneIsHidden("centre", 0)));

        var failures = 0;
        for (var i = 0; i < checks.Count; i++)
        {
            var (name, run) = checks[i];
            bool pass;
            try
            {
                pass = run();
            }
            catch (Exception ex)
            {
                pass = false;
                Console.WriteLine($"    threw: {ex.GetType().Name}: {ex.Message}");
            }

            if (!pass)
            {
                failures++;
            }

            Console.WriteLine($"[{i + 1}/{checks.Count}] {(pass ? "PASS" : "FAIL")} {name}");
        }

        if (failures == 0)
        {
            Console.WriteLine("[harness] ALL HOOK CHECKS PASSED");
            Environment.ExitCode = 0;
            return;
        }

        Console.Error.WriteLine($"[harness] {failures} HOOK CHECK(S) FAILED");
        Environment.ExitCode = 1;
    }

    private static IReadOnlyDictionary<string, object?> Map(params (string Key, object? Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => entry.Value);

    private static BoardCard MakeBoardCard(string id, string status, string[] requires, string[] provides, string? title = null, string rawDefinitionJson = "{}") =>
        new BoardCard(
            id,
            title ?? id,
            status,
            title is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal) { ["title"] = title },
            Array.Empty<BoardCardField>(),
            Array.Empty<BoardCardField>(),
            requires,
            provides,
            Array.Empty<string>(),
            Array.Empty<BoardRenderElement>(),
            Array.Empty<BoardSourceDefinition>(),
            Array.Empty<BoardChatMessage>(),
            false,
            false,
            rawDefinitionJson,
            "{}",
            "1");
}
