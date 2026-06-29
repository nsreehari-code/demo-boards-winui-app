using System;
using Microsoft.UI.Dispatching;

namespace DemoBoards_WinUI.Hooks;

public abstract partial class HookComponent<TProps>
{
    /// <summary>
    /// Local pulse clock for subtle UI animation. Uses Reactor local state so consumers can render a
    /// phase-driven animation without depending on composition APIs or global timers.
    /// </summary>
    protected double UsePulseProgress(bool enabled, double periodMs = 1050, double frameMs = 85)
    {
        var (tick, setTick) = UseState(DateTime.UtcNow.Ticks);

        UseEffect(() =>
        {
            if (!enabled)
            {
                return () => { };
            }

            DispatcherQueue? queue = DispatcherQueue.GetForCurrentThread();
            if (queue is null)
            {
                return () => { };
            }

            DispatcherQueueTimer timer = queue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(frameMs);
            timer.Tick += (_, _) => setTick(DateTime.UtcNow.Ticks);
            timer.Start();
            return () => timer.Stop();
        }, enabled ? 1 : 0);

        if (!enabled || periodMs <= 0)
        {
            return 0;
        }

        long nowTicks = tick;
        double nowMs = new DateTime(nowTicks, DateTimeKind.Utc).TimeOfDay.TotalMilliseconds;
        double wrapped = nowMs % periodMs;
        return wrapped / periodMs;
    }
}