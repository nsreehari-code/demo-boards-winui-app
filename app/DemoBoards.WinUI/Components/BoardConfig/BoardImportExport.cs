using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI.Controls.Shared;

namespace DemoBoards_WinUI.Controls.Registry.BoardConfig;

/// <summary>
/// Mirrors <c>BoardImportExport.jsx</c> — renders import/export/refresh buttons for board operations.
/// </summary>
public sealed record BoardImportExportProps(
    Action? OnImport = null,
    Action? OnExport = null,
    Action? OnRefreshBootstrap = null,
    bool Importing = false,
    bool Exporting = false,
    bool Refreshing = false,
    bool Disabled = false);

public sealed class BoardImportExport : Component<BoardImportExportProps>
{
    public override Element Render()
    {
        return VStack(12,
            TextBlock("Board Import / Export")
                .FontSize(14)
                .Bold(),
            HStack(8,
                Button(Props.Importing ? "Importing…" : "Import Board", Props.OnImport)
                    .IsEnabled(!(Props.Importing || Props.Disabled))
                    .AutomationName("Import board configuration")
                    .SubtleButton(),
                Button(Props.Exporting ? "Saving…" : "Export Board", Props.OnExport)
                    .IsEnabled(!(Props.Exporting || Props.Disabled))
                    .AutomationName("Export board configuration")
                    .SubtleButton(),
                Button(Props.Refreshing ? "Refreshing…" : "Refresh Workspace Bootstrap", Props.OnRefreshBootstrap)
                    .IsEnabled(!(Props.Refreshing || Props.Disabled))
                    .AutomationName("Refresh workspace bootstrap data")
                    .SubtleButton()
            )
        )
        .Set(stack => stack.Padding = new(12))
        .Set(stack => stack.BorderThickness = new(1))
        .Set(stack =>
        {
            stack.BorderBrush = new SolidColorBrush(BoardShared.ToneColor("border-tertiary"));
            stack.Background = new SolidColorBrush(BoardShared.ToneColor("surface-elevated"));
        });
    }
}
