using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using DemoBoards.RuntimeHost;
using System;
using System.Threading;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DemoBoards_WinUI;

/// <summary>
/// The main content page displayed inside the application window.
/// Add your UI logic, event handlers, and data binding here.
/// </summary>
public sealed partial class MainPage : Page
{
    private DemoBoardsRuntimeService? runtimeService;
    private int addedCardCounter;

    public MainPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        runtimeService = app.RuntimeService;
        runtimeService.BoardSnapshotChanged += OnBoardSnapshotChanged;

        RuntimeStatus status = runtimeService.GetStatus();
        RuntimeStatusText.Text = status.IsRunning
            ? "Embedded V8 runtime is active. Host-backed KV / Journal / Queue / Blob adapters are mounted into the app process."
            : "Embedded V8 runtime is stopped.";
        RootDirectoryText.Text = $"Root: {status.RootDirectory}";
        StorageDirectoryText.Text = $"Storage: {status.StorageDirectory}";
        AgentfaceEndpointText.Text = $"Endpoint: {status.AgentfaceEndpoint}";
        InvocationText.Text = string.IsNullOrWhiteSpace(status.LastInvocationJson)
            ? "No Copilot / Foundry invocation has been observed yet."
            : status.LastInvocationJson;

        BoardCanvasView.Render(runtimeService.GetBoardSnapshot());
    }

    private void OnUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (runtimeService is not null)
        {
            runtimeService.BoardSnapshotChanged -= OnBoardSnapshotChanged;
        }
    }

    private void OnBoardSnapshotChanged(object? sender, BoardSnapshot snapshot)
    {
        DispatcherQueue.TryEnqueue(() => BoardCanvasView.Render(snapshot));
    }

    private async void OnRefreshClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (runtimeService is null) return;
        await runtimeService.RefreshAsync();
    }

    private async void OnAddCardClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (runtimeService is null) return;

        int index = Interlocked.Increment(ref addedCardCounter);
        string cardJson =
            "{\"id\":\"shell-card-" + index + "\"," +
            "\"card_data\":{\"title\":\"Shell Card " + index + "\"," +
            "\"source\":\"winui-shell\",\"created\":\"live\"}," +
            "\"view\":{\"elements\":[]}}";

        await runtimeService.AddCardAsync(cardJson);
    }
}
