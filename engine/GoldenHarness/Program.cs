using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.ClearScript.V8;

namespace GoldenHarness;

// Phase C proof harness: run the platform-free board "brain" inside an embedded
// V8 engine via ClearScript — a LIGHTER engine than full Node (real V8, no
// fs/net/libuv) — and assert it behaves identically to the Node reference.
//
// Five checks:
//   1. Consumer golden — replay the deterministic-smoke frames through the
//      board-sse-state reducer and assert a byte-identical snapshot.
//   2. Compute battery — evaluate vendored-jsonata cases and assert byte-identical
//      results vs the Node-generated oracle (retires rounding-mode divergence).
//   3. Producer load — load the full server-runtime bundle into V8 and assert it
//      initializes without any Node builtins.
//   4. Producer golden — drive the producer against host-backed KV/Journal/
//      Queue/Blob adapters and assert a byte-identical published payload vs the
//      Node localstorage reference golden.
//   5. Invocation seam — inject a host LLM/chat invocation adapter and assert
//      the runtime routes a chat-agent request into it with the expected shape.
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var baseDir = AppContext.BaseDirectory;
        var jsDir = Path.Combine(baseDir, "js");
        var fixturesDir = Path.Combine(baseDir, "fixtures");
        var hostStorageRoot = Path.Combine(baseDir, "host-storage");

        var paths = new Dictionary<string, string>
        {
            ["board-sse-state bundle"] = Path.Combine(jsDir, "board-sse-state.js"),
            ["compute-jsonata bundle"] = Path.Combine(jsDir, "compute-jsonata.js"),
            ["server-runtime bundle"] = Path.Combine(jsDir, "server-runtime-controlface.js"),
            ["golden driver"] = Path.Combine(jsDir, "golden-driver.js"),
            ["producer driver"] = Path.Combine(jsDir, "producer-driver.js"),
            ["frames fixture"] = Path.Combine(fixturesDir, "frames.json"),
            ["snapshot fixture"] = Path.Combine(fixturesDir, "snapshot.json"),
            ["compute cases"] = Path.Combine(fixturesDir, "compute-cases.json"),
            ["compute expected"] = Path.Combine(fixturesDir, "compute-cases.expected.json"),
            ["producer cards"] = Path.Combine(fixturesDir, "producer-cards.json"),
            ["producer expected"] = Path.Combine(fixturesDir, "producer-payload.expected.json"),
        };

        foreach (var (label, path) in paths)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"[harness] missing {label}: {path}");
                return 2;
            }
        }

        var ok = true;
        var storageBridge = new HostStorageBridge(hostStorageRoot);
        var invocationBridge = new HostInvocationBridge();
        using (var engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableTaskPromiseConversion | V8ScriptEngineFlags.EnableValueTaskPromiseConversion))
        {
            engine.AddHostObject("HostStorageBridge", storageBridge);
            engine.AddHostObject("HostInvocationBridge", invocationBridge);
                        engine.Execute("polyfills.js", @"
if (typeof TextEncoder === 'undefined') {
    globalThis.TextEncoder = class TextEncoder {
        encode(text) {
            const utf8 = unescape(encodeURIComponent(String(text)));
            const bytes = new Uint8Array(utf8.length);
            for (let i = 0; i < utf8.length; i += 1) bytes[i] = utf8.charCodeAt(i);
            return bytes;
        }
    };
}
if (typeof TextDecoder === 'undefined') {
    globalThis.TextDecoder = class TextDecoder {
        decode(bytes) {
            let binary = '';
            for (let i = 0; i < bytes.length; i += 1) binary += String.fromCharCode(bytes[i]);
            return decodeURIComponent(escape(binary));
        }
    };
}
if (typeof btoa === 'undefined') {
    const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=';
    globalThis.btoa = function (input) {
        let output = '';
        for (let i = 0; i < input.length; i += 3) {
            const a = input.charCodeAt(i);
            const b = i + 1 < input.length ? input.charCodeAt(i + 1) : NaN;
            const c = i + 2 < input.length ? input.charCodeAt(i + 2) : NaN;
            const triplet = (a << 16) | ((isNaN(b) ? 0 : b) << 8) | (isNaN(c) ? 0 : c);
            output += alphabet[(triplet >> 18) & 63];
            output += alphabet[(triplet >> 12) & 63];
            output += isNaN(b) ? '=' : alphabet[(triplet >> 6) & 63];
            output += isNaN(c) ? '=' : alphabet[triplet & 63];
        }
        return output;
    };
}
if (typeof atob === 'undefined') {
    const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=';
    globalThis.atob = function (input) {
        let sanitized = String(input).replace(/=+$/, '');
        let output = '';
        let buffer = 0;
        let bits = 0;
        for (let i = 0; i < sanitized.length; i += 1) {
            const value = alphabet.indexOf(sanitized.charAt(i));
            if (value < 0 || value > 63) continue;
            buffer = (buffer << 6) | value;
            bits += 6;
            if (bits >= 8) {
                bits -= 8;
                output += String.fromCharCode((buffer >> bits) & 255);
            }
        }
        return output;
    };
}
");

            // Load order: compute-jsonata first (sets globalThis.jsonataSync), then
            // the consumer reducer and the producer runtime. The brain is
            // platform-free; this embedded V8 engine has no Node, fs or sockets.
            engine.Execute("compute-jsonata.js", File.ReadAllText(paths["compute-jsonata bundle"]));
            engine.Execute("board-sse-state.js", File.ReadAllText(paths["board-sse-state bundle"]));
            engine.Execute("golden-driver.js", File.ReadAllText(paths["golden driver"]));
            engine.Execute("producer-driver.js", File.ReadAllText(paths["producer driver"]));

            ok &= RunConsumerGolden(engine, paths["frames fixture"], paths["snapshot fixture"]);
            ok &= RunComputeBattery(engine, paths["compute cases"], paths["compute expected"]);
            ok &= RunProducerLoad(engine, paths["server-runtime bundle"]);
            ok &= await RunProducerGolden(engine, storageBridge, paths["producer cards"], paths["producer expected"]);
            ok &= await RunInvocationAdapterProof(engine, storageBridge, invocationBridge);
        }

        Console.WriteLine(ok
            ? "[harness] ALL CHECKS PASSED — the board brain runs identically in embedded V8."
            : "[harness] FAILURES above.");
        return ok ? 0 : 1;
    }

    private static bool RunConsumerGolden(V8ScriptEngine engine, string framesPath, string snapshotPath)
    {
        var framesJson = File.ReadAllText(framesPath);
        var expected = File.ReadAllText(snapshotPath).Replace("\r\n", "\n");
        var actual = ((string)engine.Invoke("runGoldenReplay", framesJson)).Replace("\r\n", "\n");

        if (actual == expected)
        {
            Console.WriteLine("[1/3] consumer golden  PASS — deterministic smoke frames reduce to the frozen snapshot.");
            return true;
        }

        Console.Error.WriteLine("[1/3] consumer golden  FAIL — snapshot mismatch between embedded V8 and the golden fixture.");
        WriteFirstLineDiff(expected, actual);
        return false;
    }

    private static bool RunComputeBattery(V8ScriptEngine engine, string casesPath, string expectedPath)
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(casesPath));
        using var expected = JsonDocument.Parse(File.ReadAllText(expectedPath));

        var expectedByName = new Dictionary<string, string>();
        foreach (var item in expected.RootElement.EnumerateArray())
        {
            expectedByName[item.GetProperty("name").GetString()!] = item.GetProperty("result").GetString()!;
        }

        var allOk = true;
        var count = 0;
        foreach (var item in cases.RootElement.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString()!;
            var expr = item.GetProperty("expr").GetString()!;
            var dataJson = item.GetProperty("data").GetRawText();

            var actual = (string)engine.Invoke("runComputeCase", expr, dataJson);
            count++;

            if (!expectedByName.TryGetValue(name, out var want))
            {
                Console.Error.WriteLine($"        compute case '{name}' has no Node expected entry.");
                allOk = false;
            }
            else if (actual != want)
            {
                Console.Error.WriteLine($"        compute case '{name}' MISMATCH: V8={actual} Node={want}");
                allOk = false;
            }
        }

        Console.WriteLine(allOk
            ? $"[2/3] compute battery  PASS — {count} jsonata cases byte-identical to the Node oracle (incl. IEEE-754 edge cases)."
            : "[2/3] compute battery  FAIL — see mismatches above.");
        return allOk;
    }

    private static bool RunProducerLoad(V8ScriptEngine engine, string producerBundlePath)
    {
        try
        {
            engine.Execute("server-runtime-controlface.js", File.ReadAllText(producerBundlePath));
            var hasCreate = (bool)engine.Evaluate(
                "typeof ServerRuntimeControlface === 'object'"
                + " && typeof ServerRuntimeControlface.createSingleBoardServerRuntime === 'function'"
                + " && typeof ServerRuntimeControlface.createMultiBoardServerRuntime === 'function'");
            if (hasCreate)
            {
                Console.WriteLine("[3/3] producer load    PASS — full server-runtime bundle initialized in V8 (no Node builtins).");
                return true;
            }

            Console.Error.WriteLine("[3/3] producer load    FAIL — server-runtime factories not found after load.");
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[3/3] producer load    FAIL — {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> RunProducerGolden(
        V8ScriptEngine engine,
        HostStorageBridge storageBridge,
        string cardsPath,
        string expectedPath)
    {
        storageBridge.ResetStorage();
        using var fixture = JsonDocument.Parse(File.ReadAllText(cardsPath));
        var boardId = fixture.RootElement.GetProperty("boardId").GetString()!;
        var cardsJson = fixture.RootElement.GetProperty("cards").GetRawText();
        var expected = File.ReadAllText(expectedPath).Replace("\r\n", "\n");
        var actual = (await AwaitJsString(engine.Invoke("runHostBackedProducerPayload", boardId, cardsJson))).Replace("\r\n", "\n");

        if (actual == expected)
        {
            Console.WriteLine("[4/5] producer golden  PASS — host-backed KV/Journal/Queue/Blob adapters match the Node producer golden.");
            return true;
        }

        Console.Error.WriteLine("[4/5] producer golden  FAIL — embedded host-backed producer payload diverged from the Node golden.");
        WriteFirstLineDiff(expected, actual);
        return false;
    }

    private static async Task<bool> RunInvocationAdapterProof(
        V8ScriptEngine engine,
        HostStorageBridge storageBridge,
        HostInvocationBridge invocationBridge)
    {
        storageBridge.ResetStorage();
        invocationBridge.Reset();
        var actual = await AwaitJsString(engine.Invoke("runInvocationAdapterProof"));

        using var doc = JsonDocument.Parse(actual);
        var root = doc.RootElement;
        var describe = root.GetProperty("describe");
        var lastInvocation = root.GetProperty("lastInvocation");
        var ok = describe.GetProperty("kind").GetString() == "chat-handler"
            && lastInvocation.GetProperty("ref").GetProperty("meta").GetString() == "chat-handler"
            && lastInvocation.GetProperty("args").GetProperty("prompt").GetString() == "hello from phase d";

        Console.WriteLine(ok
            ? "[5/5] invocation seam PASS — host invocation adapter received the chat-agent request from the runtime."
            : "[5/5] invocation seam FAIL — host invocation adapter payload was malformed.");
        if (!ok)
        {
            Console.Error.WriteLine(actual.Trim());
        }

        return ok;
    }

    private static async Task<string> AwaitJsString(object value)
    {
        if (value is Task<string> stringTask)
        {
            return await stringTask;
        }

        if (value is Task<object> objectTask)
        {
            return (string)(await objectTask);
        }

        if (value is string text)
        {
            return text;
        }

        throw new InvalidOperationException($"Unexpected JS return type: {value?.GetType().FullName ?? "<null>"}");
    }

    private static void WriteFirstLineDiff(string expected, string actual)
    {
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        var n = Math.Max(e.Length, a.Length);
        for (var i = 0; i < n; i++)
        {
            var el = i < e.Length ? e[i] : "<EOF>";
            var al = i < a.Length ? a[i] : "<EOF>";
            if (el != al)
            {
                Console.Error.WriteLine($"        first diff at line {i + 1}:");
                Console.Error.WriteLine($"          expected: {el}");
                Console.Error.WriteLine($"          actual:   {al}");
                return;
            }
        }
    }
}
