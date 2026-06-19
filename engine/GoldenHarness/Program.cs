using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.ClearScript.V8;

namespace GoldenHarness;

// Phase C proof harness: run the platform-free board "brain" inside an embedded
// V8 engine via ClearScript — a LIGHTER engine than full Node (real V8, no
// fs/net/libuv) — and assert it behaves identically to the Node reference.
//
// Three checks:
//   1. Consumer golden — replay the deterministic-smoke frames through the
//      board-sse-state reducer and assert a byte-identical snapshot.
//   2. Compute battery — evaluate vendored-jsonata cases and assert byte-identical
//      results vs the Node-generated oracle (retires rounding-mode divergence).
//   3. Producer load — load the full server-runtime bundle into V8 and assert it
//      initializes without any Node builtins.
internal static class Program
{
    private static int Main(string[] args)
    {
        var baseDir = AppContext.BaseDirectory;
        var jsDir = Path.Combine(baseDir, "js");
        var fixturesDir = Path.Combine(baseDir, "fixtures");

        var paths = new Dictionary<string, string>
        {
            ["board-sse-state bundle"] = Path.Combine(jsDir, "board-sse-state.js"),
            ["compute-jsonata bundle"] = Path.Combine(jsDir, "compute-jsonata.js"),
            ["server-runtime bundle"] = Path.Combine(jsDir, "server-runtime-controlface.js"),
            ["golden driver"] = Path.Combine(jsDir, "golden-driver.js"),
            ["frames fixture"] = Path.Combine(fixturesDir, "frames.json"),
            ["snapshot fixture"] = Path.Combine(fixturesDir, "snapshot.json"),
            ["compute cases"] = Path.Combine(fixturesDir, "compute-cases.json"),
            ["compute expected"] = Path.Combine(fixturesDir, "compute-cases.expected.json"),
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
        using (var engine = new V8ScriptEngine())
        {
            // Load order: compute-jsonata first (sets globalThis.jsonataSync), then
            // the consumer reducer and the producer runtime. The brain is
            // platform-free; this embedded V8 engine has no Node, fs or sockets.
            engine.Execute("compute-jsonata.js", File.ReadAllText(paths["compute-jsonata bundle"]));
            engine.Execute("board-sse-state.js", File.ReadAllText(paths["board-sse-state bundle"]));
            engine.Execute("golden-driver.js", File.ReadAllText(paths["golden driver"]));

            ok &= RunConsumerGolden(engine, paths["frames fixture"], paths["snapshot fixture"]);
            ok &= RunComputeBattery(engine, paths["compute cases"], paths["compute expected"]);
            ok &= RunProducerLoad(engine, paths["server-runtime bundle"]);
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
