using System;
using System.Collections.Generic;

namespace DemoBoards_WinUI.State;

/// <summary>Well-known keys for <see cref="GlobalStateStore"/> entries.</summary>
public static class GlobalStateKeys
{
    /// <summary>The active board id (WinUI analog of the frontend's <c>DEFAULT_BOARD_ID</c>).</summary>
    public const string BoardId = "board.id";

    /// <summary>The active hosted server URL backing the current board session.</summary>
    public const string ServerUrl = "board.serverUrl";

    /// <summary>In-memory flag that swaps the main surface to the historical InfiniteCanvas test page.</summary>
    public const string TestPageMode = "app.testPageMode";
}

/// <summary>Event payload describing which <see cref="GlobalStateStore"/> key changed.</summary>
public sealed class GlobalStateChangedEventArgs : EventArgs
{
    public GlobalStateChangedEventArgs(string key) => Key = key;

    public string Key { get; }
}

/// <summary>
/// Process-wide, keyed in-memory store — the WinUI analog of the frontend's app-level globals
/// (e.g. <c>DEFAULT_BOARD_ID</c> in <c>appConfig.js</c>). Components observe it through the
/// <c>UseGlobalState&lt;T&gt;</c> hook, which subscribes to <see cref="Changed"/> and re-renders.
/// </summary>
public sealed class GlobalStateStore
{
    public static GlobalStateStore Current { get; } = new();

    private readonly Dictionary<string, object?> values = new(StringComparer.Ordinal);
    private readonly object gate = new();

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

            values[key] = initialValue;
            return initialValue;
        }
    }

    /// <summary>Stores <paramref name="value"/> for <paramref name="key"/> and notifies subscribers when it changed.</summary>
    public void Set<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        lock (gate)
        {
            if (values.TryGetValue(key, out object? existing)
                && EqualityComparer<T>.Default.Equals(existing is T typed ? typed : default!, value))
            {
                return;
            }

            values[key] = value;
        }

        Changed?.Invoke(this, new GlobalStateChangedEventArgs(key));
    }
}
