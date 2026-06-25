using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>Render state handed to a <see cref="TimerButton"/>'s label builder — mirrors <c>{ remainingMs, pending }</c>.</summary>
public sealed record TimerButtonRenderState(double RemainingMs, bool Pending);

/// <summary>
/// Mirrors <c>TimerButton.jsx</c> — a button with a <c>Duration</c> (ms) countdown that fires
/// <c>OnClick</c> automatically when it elapses (then restarts), and immediately when clicked. While
/// <c>OnClick</c> resolves the button reports a pending state and is disabled. <c>Children</c> builds the
/// label from the live <see cref="TimerButtonRenderState"/>.
/// </summary>
public sealed record TimerButtonProps(
    double Duration = 0,
    Func<Task>? OnClick = null,
    bool Disabled = false,
    Func<TimerButtonRenderState, string>? Children = null);

public sealed class TimerButton : Component<TimerButtonProps>
{
    public override Element Render()
    {
        _ = UseContext(AppThemeContext.Current);

        var deadlineRef = UseRef(DateTime.UtcNow.AddMilliseconds(Props.Duration <= 0 ? 0 : Props.Duration));
        var pendingRef = UseRef(false);
        var tickRef = UseRef(0);
        var (_, setTick) = UseState(0);
        var (pending, setPending) = UseState(false);

        double remaining = Math.Max(0, (deadlineRef.Current - DateTime.UtcNow).TotalMilliseconds);

        async void Fire()
        {
            if (pendingRef.Current || Props.Disabled)
            {
                return;
            }

            pendingRef.Current = true;
            setPending(true);
            try
            {
                if (Props.OnClick != null)
                {
                    await Props.OnClick();
                }
            }
            finally
            {
                pendingRef.Current = false;
                setPending(false);
                deadlineRef.Current = DateTime.UtcNow.AddMilliseconds(Props.Duration <= 0 ? 0 : Props.Duration);
                tickRef.Current++;
                setTick(tickRef.Current);
            }
        }

        UseEffect(() =>
        {
            if (Props.Duration <= 0)
            {
                return null;
            }

            DispatcherQueue? queue = DispatcherQueue.GetForCurrentThread();
            if (queue is null)
            {
                return null;
            }

            DispatcherQueueTimer timer = queue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(250);
            timer.Tick += (_, _) =>
            {
                tickRef.Current++;
                setTick(tickRef.Current);
                double rem = (deadlineRef.Current - DateTime.UtcNow).TotalMilliseconds;
                if (rem <= 0 && !pendingRef.Current && !Props.Disabled)
                {
                    Fire();
                }
            };
            timer.Start();
            return () => timer.Stop();
        }, "timer-button");

        string label = Props.Children != null
            ? Props.Children(new TimerButtonRenderState(remaining, pending))
            : pending ? "Working..." : $"{Math.Ceiling(remaining / 1000.0):0}s";

        return Button(label, () => Fire())
            .AccentButton()
            .AutomationName("Timer action")
            .Set(button => button.IsEnabled = !Props.Disabled && !pending);
    }
}
