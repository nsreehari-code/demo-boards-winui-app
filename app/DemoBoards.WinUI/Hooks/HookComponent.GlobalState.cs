using System;
using DemoBoards_WinUI.State;

namespace DemoBoards_WinUI.Hooks;

// =====================================================================================
//  UseGlobalState — generic keyed global-state hook (React useGlobalState analog)
//
//  Mirrors UseBoardStoreSubscription: subscribes to the shared GlobalStateStore for one
//  key, bumps a revision UseState when that key changes, and returns the current value
//  plus a setter. The setter accepts either a direct value (setValue(next)) or a
//  functional updater (setValue((Func<T,T>)(prev => ...))), matching React's setState.
//  An optional persist callback lets a key write through to durable storage (e.g. the
//  board id override file) whenever it is updated.
// =====================================================================================

public abstract partial class HookComponent<TProps>
{
    /// <summary>
    /// Reads and writes a value in the process-wide <see cref="GlobalStateStore"/> reactively.
    /// The returned setter supports both <c>setValue(next)</c> and a functional
    /// <c>setValue((Func&lt;T,T&gt;)(prev =&gt; ...))</c> updater.
    /// </summary>
    /// <param name="key">The global-state key (see <see cref="GlobalStateKeys"/>).</param>
    /// <param name="initialValue">Seed value used the first time the key is accessed.</param>
    /// <param name="persist">Optional write-through invoked with the resolved value after each set.</param>
    protected (T Value, Action<object?> SetValue) UseGlobalState<T>(string key, T initialValue, Action<T>? persist = null)
    {
        GlobalStateStore store = GlobalStateStore.Current;
        T current = store.GetOrAdd(key, initialValue);
        var (_, setRevision) = UseState(string.Empty);

        UseEffect(() =>
        {
            EventHandler<GlobalStateChangedEventArgs> handler = (_, args) =>
            {
                if (string.Equals(args.Key, key, StringComparison.Ordinal))
                {
                    setRevision(Guid.NewGuid().ToString("N"));
                }
            };

            store.Changed += handler;
            return () => store.Changed -= handler;
        }, key);

        void SetValue(object? next)
        {
            T resolved = next is Func<T, T> updater
                ? updater(store.GetOrAdd(key, initialValue))
                : (T)next!;

            store.Set(key, resolved);
            persist?.Invoke(resolved);
        }

        return (current, SetValue);
    }
}
