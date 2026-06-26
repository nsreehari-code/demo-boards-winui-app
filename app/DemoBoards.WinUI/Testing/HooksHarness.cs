using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using DemoBoards_WinUI.Controls;
using DemoBoards_WinUI.Hooks;

namespace DemoBoards_WinUI;

/// <summary>
/// Pure-logic parity harness (run via <c>--hooks-harness</c>) for the hook support code that has no UI
/// surface: the <see cref="ChatMessages"/> helpers ported from <c>chatMessages.js</c> — live/history
/// accumulation, turn-id utilities, the scalar effect signature, and the MCP payload parse. Unlike the
/// RenderHarness it needs no window or board store; it just runs deterministic assertions and prints
/// <c>[i/N] PASS/FAIL</c> lines plus a final banner, mirroring the other harnesses.
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
