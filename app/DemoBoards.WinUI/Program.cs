using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemoBoards_WinUI;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        if (Array.Exists(Environment.GetCommandLineArgs(), arg => string.Equals(arg, "--render-harness", StringComparison.OrdinalIgnoreCase)))
        {
            RenderHarness.RunAndExit();
            return;
        }

        if (Array.Exists(Environment.GetCommandLineArgs(), arg => string.Equals(arg, "--hooks-harness", StringComparison.OrdinalIgnoreCase)))
        {
            HooksHarness.RunAndExit();
            return;
        }

        App.Current.Start();

        try
        {
            ReactorApp.Run<DemoBoardsRoot>(
                "DemoBoards.WinUI",
                width: 1440,
                height: 900,
                configure: host =>
                {
                    XamlInterop.Register(host.Reconciler);
                    App.Current.AttachWindow(host.Window);
                    FitWindowToWorkArea(host.Window);
                });
        }
        finally
        {
            App.Current.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    // The window is requested at a comfortable design size, but on smaller displays that
    // size can run off the bottom/right of the screen — clipping anything anchored to the
    // window edges (e.g. the canvas minimap and zoom controls). Clamp the window to the
    // display's work area (which already excludes the taskbar) and centre it so every edge
    // stays on-screen regardless of resolution.
    private static void FitWindowToWorkArea(Window window)
    {
        Microsoft.UI.Windowing.AppWindow? appWindow = window.AppWindow;
        if (appWindow is null)
        {
            return;
        }

        Microsoft.UI.Windowing.DisplayArea display = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
            appWindow.Id,
            Microsoft.UI.Windowing.DisplayAreaFallback.Primary);

        Windows.Graphics.RectInt32 work = display.WorkArea;
        const int margin = 24;
        int width = Math.Min(appWindow.Size.Width, work.Width - margin);
        int height = Math.Min(appWindow.Size.Height, work.Height - margin);
        int x = work.X + Math.Max(0, (work.Width - width) / 2);
        int y = work.Y + Math.Max(0, (work.Height - height) / 2);
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
    }
}

internal sealed class DemoBoardsRoot : Component
{
    public override Element Render()
    {
        App.Current.EnsureUiResources();

        if (App.Current.StartupException is Exception ex)
        {
            return Microsoft.UI.Reactor.Factories.ScrollViewer(
                    Microsoft.UI.Reactor.Factories.TextBox($"DemoBoards.WinUI failed to start.{Environment.NewLine}{Environment.NewLine}{ex}")
                        .AutomationName("Startup failure details")
                        .IsReadOnly(true)
                        .AcceptsReturn(true)
                        .Set(textBox => textBox.TextWrapping = TextWrapping.Wrap)
                        .Margin(16))
                .Set(scrollViewer => scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto);
        }

        return Microsoft.UI.Reactor.Factories.Component<Controls.AppRoot>();
    }
}