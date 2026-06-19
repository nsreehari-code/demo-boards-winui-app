using System;
using System.IO;
using Microsoft.ClearScript.V8;

namespace GoldenHarness;

// Phase C proof harness: load the platform-free board "brain" (board-sse-state
// IIFE bundle) into an embedded V8 engine via ClearScript — a LIGHTER engine than
// full Node (real V8, no fs/net/libuv) — replay the deterministic-smoke golden
// frames, and assert the reduced snapshot is byte-identical to the frozen golden
// fixture. Passing proves the brain runs identically in the embedded engine.
internal static class Program
{
    private static int Main(string[] args)
    {
        var baseDir = AppContext.BaseDirectory;
        var bundlePath = Path.Combine(baseDir, "js", "board-sse-state.js");
        var driverPath = Path.Combine(baseDir, "js", "golden-driver.js");
        var framesPath = Path.Combine(baseDir, "fixtures", "frames.json");
        var snapshotPath = Path.Combine(baseDir, "fixtures", "snapshot.json");

        foreach (var (label, path) in new[]
                 {
                     ("brain bundle", bundlePath),
                     ("golden driver", driverPath),
                     ("frames fixture", framesPath),
                     ("snapshot fixture", snapshotPath),
                 })
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"[golden-harness] missing {label}: {path}");
                return 2;
            }
        }

        var bundleSource = File.ReadAllText(bundlePath);
        var driverSource = File.ReadAllText(driverPath);
        var framesJson = File.ReadAllText(framesPath);
        var expected = File.ReadAllText(snapshotPath);

        string actual;
        using (var engine = new V8ScriptEngine())
        {
            // The brain is platform-free; this embedded V8 engine has no Node
            // runtime, no fs, no sockets — only ECMAScript.
            engine.Execute("board-sse-state.js", bundleSource);
            engine.Execute("golden-driver.js", driverSource);
            actual = (string)engine.Invoke("runGoldenReplay", framesJson);
        }

        // Normalize line endings so CRLF/LF checkout differences can't mask a match.
        var normActual = actual.Replace("\r\n", "\n");
        var normExpected = expected.Replace("\r\n", "\n");

        if (normActual == normExpected)
        {
            Console.WriteLine(
                "[golden-harness] PASS — embedded V8 reduced the deterministic smoke frames "
                + "to the frozen golden snapshot (byte-identical).");
            return 0;
        }

        Console.Error.WriteLine(
            "[golden-harness] FAIL — snapshot mismatch between embedded V8 and the golden fixture.");
        WriteFirstDiff(normExpected, normActual);
        return 1;
    }

    private static void WriteFirstDiff(string expected, string actual)
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
                Console.Error.WriteLine($"  first diff at line {i + 1}:");
                Console.Error.WriteLine($"    expected: {el}");
                Console.Error.WriteLine($"    actual:   {al}");
                return;
            }
        }
    }
}
