using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace DemoBoards_WinUI.State;

/// <summary>Well-known keys for <see cref="GlobalStateStore"/> entries.</summary>
public static class GlobalStateKeys
{
    /// <summary>The active board id (WinUI analog of the frontend's <c>DEFAULT_BOARD_ID</c>).</summary>
    public const string BoardId = "board.id";

    /// <summary>The active hosted server URL backing the current board session.</summary>
    public const string ServerUrl = "board.serverUrl";

    /// <summary>Persisted flag that swaps the main surface to the historical InfiniteCanvas test page.</summary>
    public const string TestPageMode = "app.testPageMode";
}

/// <summary>Event payload describing which <see cref="GlobalStateStore"/> key changed.</summary>
public sealed class GlobalStateChangedEventArgs : EventArgs
{
    public GlobalStateChangedEventArgs(string key) => Key = key;

    public string Key { get; }
}

/// <summary>
/// Process-wide, keyed global store — the WinUI analog of the frontend's app-level globals
/// (e.g. <c>DEFAULT_BOARD_ID</c> in <c>appConfig.js</c>). Values are persisted under
/// <c>%LOCALAPPDATA%\DemoBoards.WinUI\global-state.json</c> whenever a key is updated. Components observe it through the
/// <c>UseGlobalState&lt;T&gt;</c> hook, which subscribes to <see cref="Changed"/> and re-renders.
/// </summary>
public sealed class GlobalStateStore
{
    public static GlobalStateStore Current { get; } = new();

    private readonly Dictionary<string, object?> values = new(StringComparer.Ordinal);
    private readonly object gate = new();

    private static string StorageFilePath
    {
        get
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "DemoBoards.WinUI", "global-state.json");
        }
    }

    private GlobalStateStore()
    {
        LoadPersistedValues();
    }

    /// <summary>Raised after a key's value changes. Handlers run on the caller's thread.</summary>
    public event EventHandler<GlobalStateChangedEventArgs>? Changed;

    /// <summary>Returns the stored value for <paramref name="key"/>, seeding it with <paramref name="initialValue"/> on first access.</summary>
    public T GetOrAdd<T>(string key, T initialValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        lock (gate)
        {
            if (values.TryGetValue(key, out object? existing) && existing is T typed)
            {
                return typed;
            }

            if (values.TryGetValue(key, out existing) && existing is JsonNode node)
            {
                if (TryReadNode(node, out T converted))
                {
                    values[key] = converted;
                    return converted;
                }
            }

            if (values.TryGetValue(key, out existing) && existing is not null)
            {
                try
                {
                    T converted = (T)Convert.ChangeType(existing, typeof(T));
                    values[key] = converted;
                    return converted;
                }
                catch
                {
                }
            }

            values[key] = initialValue;
            return initialValue;
        }
    }

    private static bool TryReadNode<T>(JsonNode node, out T value)
    {
        object? converted = node switch
        {
            JsonValue scalar => TryReadScalar(typeof(T), scalar, out object? scalarValue) ? scalarValue : null,
            JsonObject obj when typeof(T) == typeof(JsonObject) => obj.DeepClone(),
            JsonArray arr when typeof(T) == typeof(JsonArray) => arr.DeepClone(),
            _ when typeof(T) == typeof(JsonNode) => node.DeepClone(),
            _ => null,
        };

        if (converted is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    private static bool TryReadScalar(Type targetType, JsonValue scalar, out object? value)
    {
        try
        {
            if (targetType == typeof(string))
            {
                value = scalar.GetValue<string>();
                return true;
            }

            if (targetType == typeof(bool))
            {
                value = scalar.GetValue<bool>();
                return true;
            }

            if (targetType == typeof(int))
            {
                value = scalar.GetValue<int>();
                return true;
            }

            if (targetType == typeof(long))
            {
                value = scalar.GetValue<long>();
                return true;
            }

            if (targetType == typeof(double))
            {
                value = scalar.GetValue<double>();
                return true;
            }

            if (targetType == typeof(float))
            {
                value = scalar.GetValue<float>();
                return true;
            }

            if (targetType == typeof(decimal))
            {
                value = scalar.GetValue<decimal>();
                return true;
            }
        }
        catch
        {
        }

        value = null;
        return false;
    }

    /// <summary>Stores <paramref name="value"/> for <paramref name="key"/> and notifies subscribers when it changed.</summary>
    public void Set<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        bool changed = false;
        lock (gate)
        {
            if (values.TryGetValue(key, out object? existing)
                && EqualityComparer<T>.Default.Equals(existing is T typed ? typed : default!, value))
            {
                return;
            }

            values[key] = value;
            changed = true;
            SavePersistedValues();
        }

        if (changed)
        {
            Changed?.Invoke(this, new GlobalStateChangedEventArgs(key));
        }
    }

    private void LoadPersistedValues()
    {
        try
        {
            string path = StorageFilePath;
            if (!File.Exists(path))
            {
                return;
            }

            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root)
            {
                return;
            }

            foreach ((string key, JsonNode? node) in root)
            {
                if (node is null)
                {
                    continue;
                }

                values[key] = node.DeepClone();
            }
        }
        catch
        {
        }
    }

    private void SavePersistedValues()
    {
        string path = StorageFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var root = new JsonObject();
        foreach ((string key, object? value) in values)
        {
            if (value is null)
            {
                continue;
            }

            try
            {
                root[key] = value switch
                {
                    JsonNode node => node.DeepClone(),
                    string s => JsonValue.Create(s.Trim()),
                    bool b => JsonValue.Create(b),
                    int i => JsonValue.Create(i),
                    long l => JsonValue.Create(l),
                    double d => JsonValue.Create(d),
                    float f => JsonValue.Create(f),
                    decimal m => JsonValue.Create(m),
                    _ => null,
                };
            }
            catch
            {
                // Ignore non-serializable entries so one bad key does not block the rest of the store.
            }
        }

        string tempPath = path + ".tmp";
        File.WriteAllText(tempPath, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.Copy(tempPath, path, overwrite: true);
        File.Delete(tempPath);
    }
}
