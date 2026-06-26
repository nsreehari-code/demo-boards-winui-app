using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.UI.Reactor.Core;
using DemoBoards_WinUI.Controls;
using DemoBoards_WinUI.Controls.Registry;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards_WinUI.Hooks;

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
}
