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
                });
        }
        finally
        {
            App.Current.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
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

        return Microsoft.UI.Reactor.Factories.Component<Controls.ReactorMainShellComponent>();
    }
}