using Microsoft.UI.Xaml.Media;

namespace DemoBoards_WinUI.Controls;

internal static class CardToneBrushes
{
    public static Brush CreateToneBrush(string status, byte alpha)
    {
        return BoardTheme.CreateStatusBrush(status, alpha);
    }
}