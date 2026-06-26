using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DemoBoards_WinUI.Controls.Shared;
using Microsoft.ClearScript.V8;

namespace DemoBoards.RuntimeHost;

/// <summary>
/// Synchronous JSONata evaluation bridge — the C# counterpart of the frontend's <c>compileSync</c>
/// from <c>yaml-flow/compute-jsonata</c>. Owns a dedicated V8 engine that loads the very same vendored
/// <c>compute-jsonata.js</c> bundle (exposing <c>globalThis.jsonataSync</c>), so expressions evaluate
/// byte-identically to the browser. Expressions are compiled once and cached by source string,
/// mirroring the frontend's precompiled validators.
/// </summary>
public static class JsonataSync
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static V8ScriptEngine? engine;

    // Mirrors the frontend's validator pass exactly: an un-compilable expression is skipped (never
    // produces a message), while a compiled expression that evaluates to anything other than literal
    // `true` (or throws) yields its message.
    private const string BridgeScript = @"
(function () {
  var cache = Object.create(null);
  function tryCompile(expr) {
    if (expr in cache) return cache[expr];
    var compiled = null;
    try { compiled = jsonataSync(expr); } catch (e) { compiled = null; }
    cache[expr] = compiled;
    return compiled;
  }
  globalThis.winuiRunValidators = function (validatorsJson, rootJson) {
    var validators = JSON.parse(validatorsJson);
    var root = JSON.parse(rootJson);
    var errors = [];
    for (var i = 0; i < validators.length; i++) {
      var entry = validators[i];
      var fn = tryCompile(entry.expr);
      if (!fn) continue;
      var ok = false;
      try { ok = fn.evaluate(root) === true; } catch (e) { ok = false; }
      if (!ok) errors.push(entry.message);
    }
    return JSON.stringify(errors);
  };
})();
";

    private static V8ScriptEngine Engine
    {
        get
        {
            if (engine != null)
            {
                return engine;
            }

            var created = new V8ScriptEngine();
            string bundle = Path.Combine(AppContext.BaseDirectory, "js", "compute-jsonata.js");
            created.Execute("compute-jsonata.js", File.ReadAllText(bundle));
            created.Execute("winui-validators.js", BridgeScript);
            engine = created;
            return engine;
        }
    }

    /// <summary>
    /// Runs the JSONata <paramref name="validators"/> against <c>{ data: values }</c> exactly like the
    /// frontend — each expression must evaluate to literal <c>true</c> to pass — and returns the
    /// messages of the validators that failed (empty = valid). If the engine cannot be initialised
    /// the form is not blocked (returns empty), matching the frontend's "skip rather than break".
    /// </summary>
    public static IReadOnlyList<string> RunValidators(
        IReadOnlyList<JsonataValidator> validators,
        IReadOnlyDictionary<string, object?> values)
    {
        if (validators == null || validators.Count == 0)
        {
            return Array.Empty<string>();
        }

        string validatorsJson = JsonSerializer.Serialize(
            validators.Select(v => new { expr = v.Expr, message = v.Message }));
        string rootJson = JsonSerializer.Serialize(
            new Dictionary<string, object?> { ["data"] = values }, SerializerOptions);

        try
        {
            lock (Gate)
            {
                var result = (string)Engine.Invoke("winuiRunValidators", validatorsJson, rootJson);
                return JsonSerializer.Deserialize<List<string>>(result) ?? new List<string>();
            }
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
