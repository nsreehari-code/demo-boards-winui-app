using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ClearScript.V8;

namespace DemoBoards.RuntimeHost;

internal sealed class RuntimeJsHost : IDisposable
{
    private readonly V8ScriptEngine engine;
    private readonly SemaphoreSlim engineLock = new(1, 1);

    public RuntimeJsHost(
        HostStorageBridge storageBridge,
        HostControlfaceBridge controlfaceBridge,
        CopilotFoundryInvocationBridge invocationBridge,
        HostBoardNotifier boardNotifier)
    {
        engine = new V8ScriptEngine(
            V8ScriptEngineFlags.EnableTaskPromiseConversion
            | V8ScriptEngineFlags.EnableValueTaskPromiseConversion);

        InitializeEngine(storageBridge, controlfaceBridge, invocationBridge, boardNotifier);
    }

    public async Task<string> InvokeJsAsync(string function, params object[] args)
    {
        await engineLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await AwaitJsString(engine.Invoke(function, args)).ConfigureAwait(false);
        }
        finally
        {
            engineLock.Release();
        }
    }

    public void Dispose()
    {
        engine.Dispose();
        engineLock.Dispose();
    }

    private void InitializeEngine(
        HostStorageBridge storageBridge,
        HostControlfaceBridge controlfaceBridge,
        CopilotFoundryInvocationBridge invocationBridge,
        HostBoardNotifier boardNotifier)
    {
        engine.AddHostObject("HostStorageBridge", storageBridge);
        engine.AddHostObject("HostControlfaceBridge", controlfaceBridge);
        engine.AddHostObject("HostInvocationBridge", invocationBridge);
        engine.AddHostObject("HostNotifier", boardNotifier);
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

        string baseDir = AppContext.BaseDirectory;
        string jsDir = Path.Combine(baseDir, "js");
        engine.Execute("board-sse-state.js", File.ReadAllText(Path.Combine(jsDir, "board-sse-state.js")));
        engine.Execute("board-sse-reducer-host.js", File.ReadAllText(Path.Combine(jsDir, "board-sse-reducer-host.js")));
        engine.Execute("compute-jsonata.js", File.ReadAllText(Path.Combine(jsDir, "compute-jsonata.js")));
        engine.Execute("golden-driver.js", File.ReadAllText(Path.Combine(jsDir, "golden-driver.js")));
        engine.Execute("controlface-embedded-shared.js", File.ReadAllText(Path.Combine(jsDir, "controlface-embedded-shared.js")));
        engine.Execute("agentface-embedded-shared.js", File.ReadAllText(Path.Combine(jsDir, "agentface-embedded-shared.js")));
        engine.Execute("server-runtime-controlface.js", File.ReadAllText(Path.Combine(jsDir, "server-runtime-controlface.js")));
        engine.Execute("producer-driver.js", File.ReadAllText(Path.Combine(jsDir, "producer-driver.js")));
    }

    private static async Task<string> AwaitJsString(object value)
    {
        if (value is Task<string> stringTask)
        {
            return await stringTask.ConfigureAwait(false);
        }

        if (value is Task<object> objectTask)
        {
            return (string)(await objectTask.ConfigureAwait(false));
        }

        if (value is string text)
        {
            return text;
        }

        throw new InvalidOperationException($"Unexpected JS return type: {value?.GetType().FullName ?? "<null>"}");
    }
}
