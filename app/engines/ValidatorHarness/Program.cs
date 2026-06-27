using System;
using System.Collections.Generic;
using System.Linq;
using DemoBoards_WinUI.Controls.Shared;
using DemoBoards.RuntimeHost;

namespace ValidatorHarness;

// Parity harness for JsonataSync.RunValidators — the C# counterpart of the
// frontend Form's JSONata validator pass (compileSync + evaluate against
// { data: values }, must return literal `true`). Each case asserts the exact
// failing-message list, covering the documented behaviours:
//   * true            -> passes (no message)
//   * false           -> fails  (message emitted)
//   * cross-field     -> values from sibling fields resolve via data.<key>
//   * order/multiple  -> only failing messages, in declaration order
//   * un-compilable   -> skipped entirely (never produces a message)
//   * evaluate throws -> treated as a failure
//   * non-true result -> treated as a failure
//   * empty list      -> passes
internal static class Program
{
    private sealed record Case(
        string Name,
        IReadOnlyList<JsonataValidator> Validators,
        IReadOnlyDictionary<string, object?> Values,
        IReadOnlyList<string> Expected);

    private static int Main()
    {
        var cases = new List<Case>
        {
            new("passes when expression is true",
                new[] { V("data.age >= 18", "Must be 18 or older") },
                D(("age", 21)),
                Array.Empty<string>()),

            new("fails when expression is false",
                new[] { V("data.age >= 18", "Must be 18 or older") },
                D(("age", 16)),
                new[] { "Must be 18 or older" }),

            new("cross-field validator satisfied",
                new[] { V("data.max >= data.min", "Max must be >= min") },
                D(("min", 5), ("max", 10)),
                Array.Empty<string>()),

            new("cross-field validator violated",
                new[] { V("data.max >= data.min", "Max must be >= min") },
                D(("min", 5), ("max", 3)),
                new[] { "Max must be >= min" }),

            new("multiple validators keep declaration order, only failing ones",
                new[]
                {
                    V("data.a > 0", "A must be positive"),
                    V("data.b > 0", "B must be positive"),
                    V("data.c > 0", "C must be positive"),
                },
                D(("a", 1), ("b", -1), ("c", -1)),
                new[] { "B must be positive", "C must be positive" }),

            new("un-compilable expression is skipped, valid sibling still passes",
                new[]
                {
                    V("((( not valid jsonata", "should never appear"),
                    V("data.ok = true", "ok must be true"),
                },
                D(("ok", true)),
                Array.Empty<string>()),

            new("un-compilable expression is skipped, valid sibling still fails",
                new[]
                {
                    V("((( not valid jsonata", "should never appear"),
                    V("data.ok = true", "ok must be true"),
                },
                D(("ok", false)),
                new[] { "ok must be true" }),

            new("evaluate-time error is treated as a failure",
                new[] { V("$number('abc') > 0", "amount must be numeric") },
                D(("amount", "abc")),
                new[] { "amount must be numeric" }),

            new("non-boolean-true result is treated as a failure",
                new[] { V("data.count", "count must be exactly true") },
                D(("count", 3)),
                new[] { "count must be exactly true" }),

            new("string non-empty check passes",
                new[] { V("data.name != ''", "Name is required") },
                D(("name", "Jo")),
                Array.Empty<string>()),

            new("string non-empty check fails",
                new[] { V("data.name != ''", "Name is required") },
                D(("name", "")),
                new[] { "Name is required" }),

            new("empty validator list passes",
                Array.Empty<JsonataValidator>(),
                D(("anything", 1)),
                Array.Empty<string>()),
        };

        var failures = 0;
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i];
            IReadOnlyList<string> actual = JsonataSync.RunValidators(c.Validators, c.Values);
            var pass = actual.SequenceEqual(c.Expected);
            if (!pass)
            {
                failures++;
            }

            Console.WriteLine($"[{i + 1}/{cases.Count}] {(pass ? "PASS" : "FAIL")} {c.Name}");
            if (!pass)
            {
                Console.WriteLine($"    expected: [{string.Join(" | ", c.Expected)}]");
                Console.WriteLine($"    actual:   [{string.Join(" | ", actual)}]");
            }
        }

        if (failures == 0)
        {
            Console.WriteLine("[harness] ALL VALIDATOR CHECKS PASSED");
            return 0;
        }

        Console.Error.WriteLine($"[harness] {failures} VALIDATOR CHECK(S) FAILED");
        return 1;
    }

    private static JsonataValidator V(string expr, string message) => new(expr, message);

    private static Dictionary<string, object?> D(params (string Key, object? Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => entry.Value);
}
