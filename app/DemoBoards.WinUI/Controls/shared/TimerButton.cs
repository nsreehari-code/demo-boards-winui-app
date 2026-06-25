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
/// <c>OnClick</c> resolves the button reports a pending state and is disabled. <c>Children</c> is a render
/// function building the visible content (any element) from the live <see cref="TimerButtonRenderState"/>;
/// a static node is just a function that ignores the state.
/// </summary>
public sealed record TimerButtonProps(
    double Duration = 0,
    Func<Task>? OnClick = null,
    bool Disabled = false,
    Func<TimerButtonRenderState, Element>? Children = null);

public sealed class TimerButton : Component<TimerButtonProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);

        // Same shape as the frontend: the countdown lives in state (deadline + nowMs);
        // remainingMs is derived. Refs only guard re-entrancy and the "armed" gate.
        var (deadline, setDeadline) = UseState(DateTime.UtcNow.AddMilliseconds(Props.Duration));
        var (nowMs, setNowMs) = UseState(DateTime.UtcNow);
        var (pending, setPending) = UseState(false);
        var pendingRef = UseRef(false);
        var armedRef = UseRef(false);

        double remainingMs = Math.Max(0, (deadline - nowMs).TotalMilliseconds);

        void ResetCountdown()
        {
            DateTime now = DateTime.UtcNow;
            setNowMs(now);
            setDeadline(now.AddMilliseconds(Props.Duration));
            armedRef.Current = false;
        }

        async void Fire()
        {
            if (pendingRef.Current)
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
                ResetCountdown();
            }
        }

        // Restart the countdown whenever the duration changes (frontend: effect on resetCountdown).
        UseEffect(ResetCountdown, Props.Duration);

        // Tick nowMs once a second while idle (frontend: window.setInterval gated on !pending).
        UseEffect(() =>
        {
            DispatcherQueue? queue = pending ? null : DispatcherQueue.GetForCurrentThread();
            if (queue is null)
            {
                return null;
            }

            DispatcherQueueTimer timer = queue.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += (_, _) =>
            {
                armedRef.Current = true;
                setNowMs(DateTime.UtcNow);
            };
            timer.Start();
            return () => timer.Stop();
        }, pending);

        // Auto-fire when the countdown elapses (frontend: effect on [remainingMs, pending, disabled]).
        UseEffect(() =>
        {
            if (armedRef.Current && remainingMs <= 0 && !pending && !Props.Disabled)
            {
                Fire();
            }
        }, (remainingMs, pending, Props.Disabled));

        Element content = Props.Children != null
            ? Props.Children(new TimerButtonRenderState(remainingMs, pending))
            : TextBlock(pending ? "Working..." : $"{Math.Ceiling(remainingMs / 1000.0):0}s").Foreground(theme.TextPrimary);

        return Border(content)
            .Padding(10)
            .Background(theme.Accent)
            .CornerRadius(8)
            .AutomationName("Timer action")
            .Set(border =>
            {
                border.Opacity = Props.Disabled || pending ? 0.5 : 1.0;
                border.Tapped += (_, _) =>
                {
                    if (!Props.Disabled && !pending)
                    {
                        Fire();
                    }
                };
            });
    }
}
