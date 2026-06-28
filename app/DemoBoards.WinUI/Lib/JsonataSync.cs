using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DemoBoards_WinUI.Controls.Shared;
using Microsoft.ClearScript.V8;

namespace DemoBoards_WinUI.Lib;

/// <summary>
/// Synchronous JSONata evaluation bridge owned by the WinUI app. Loads the same
/// browser bundle used elsewhere in the repo so form validators stay byte-identical
/// with the frontend reducer/runtime behavior.
/// </summary>
public static class JsonataSync
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static V8ScriptEngine? engine;

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
            string bundle = Path.Combine(AppContext.BaseDirectory, "Lib", "compute-jsonata.js");
            created.Execute("compute-jsonata.js", File.ReadAllText(bundle));
            created.Execute("winui-validators.js", BridgeScript);
            engine = created;
            return engine;
        }
    }

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