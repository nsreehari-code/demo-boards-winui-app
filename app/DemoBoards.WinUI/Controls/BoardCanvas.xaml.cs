using DemoBoards.RuntimeHost;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI;
using DemoBoards_WinUI.Assets;
using DemoBoards_WinUI.State;
using Windows.Foundation;

namespace DemoBoards_WinUI.Controls;

/// <summary>
/// Native board surface that re-instantiates the demo-boards-frontend card view
/// over the embedded runtime snapshot. Each card has a status-toned front and a
/// flippable runtime back, mirroring CardShell / CardBackface.
/// </summary>
public sealed partial class BoardCanvas : UserControl
{
    private const double MinCardWidth = 280;
    private const double MaxCardWidth = 960;
    private const double MiniMapWidth = 200;
    private const double MiniMapHeight = 128;
    private const double BackgroundGridGap = 24;

    private string currentBoardId = string.Empty;
    private bool suppressViewportUpdates;
    private bool isMiniMapDragging;
    private string? draggingCardId;
    private FrameworkElement? draggingHost;
    private Point dragStartPoint;
    private double dragStartLeft;
    private double dragStartTop;
    private string? resizingCardId;
    private FrameworkElement? resizingHost;
    private Point resizeStartPoint;
    private double resizeStartWidth;
    private string? selectedToken;
    private string? focusedCardId;
    private BoardCanvasViewportState? savedViewportBeforeTokenFocus;
    private BoardCanvasViewportState? savedViewportBeforeCardFocus;
    private BoardInfoState? lastBoardInfo;
    private BoardSummaryState? lastSummary;
    private IReadOnlyList<BoardCard> lastCards = System.Array.Empty<BoardCard>();
    private BoardCanvasLayoutState lastLayoutState = BoardCanvasLayoutState.Empty;
    private IReadOnlyList<RendererRule>? lastRendererRules;
    private IReadOnlyDictionary<string, string> lastDataObjects = new Dictionary<string, string>(System.StringComparer.Ordinal);
    private IReadOnlyDictionary<string, BoardCanvasPlacement> lastPlacements = new Dictionary<string, BoardCanvasPlacement>(System.StringComparer.Ordinal);
    private readonly ScrollViewer CanvasScrollViewer;
    private readonly Grid CanvasSurface;
    private readonly Canvas BackgroundGridHost;
    private readonly Canvas CardsHost;
    private readonly Button ZoomOutButton;
    private readonly Button ZoomInButton;
    private readonly Button FitCanvasButton;
    private readonly Border TokenFocusBanner;
    private readonly Button ClearTokenFocusButton;
    private readonly Canvas MiniMapHost;
    private readonly Border MiniMapViewport;

    public BoardCanvas()
    {
        BackgroundGridHost = new Canvas { IsHitTestVisible = false };
        CardsHost = new Canvas();
        CanvasSurface = new Grid
        {
            Children =
            {
                BackgroundGridHost,
                CardsHost,
            }
        };
        CanvasScrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Enabled,
            ZoomMode = ZoomMode.Enabled,
            MinZoomFactor = 0.24f,
            MaxZoomFactor = 1.35f,
            Content = CanvasSurface,
        };
        ZoomInButton = BuildControlButton(HostIconSources.ControlZoomIn, "Zoom in");
        ZoomOutButton = BuildControlButton(HostIconSources.ControlZoomOut, "Zoom out");
        FitCanvasButton = BuildControlButton(HostIconSources.ControlFitView, "Fit to view");
        ClearTokenFocusButton = new Button { Padding = new Thickness(10, 2, 10, 2) };
        TokenFocusBanner = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(12),
            CornerRadius = new CornerRadius(10),
            Background = BoardTheme.ResolveBrush("BoardCanvasPanelBrush", Colors.WhiteSmoke),
            BorderBrush = BoardTheme.ResolveBrush("BoardCanvasPanelBorderBrush", Colors.LightGray),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Token focus",
                        FontSize = 12,
                        Opacity = 0.72,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    ClearTokenFocusButton,
                }
            }
        };
        MiniMapHost = new Canvas
        {
            Width = 200,
            Height = 128,
            // A transparent background makes the whole mini-map area hit-testable
            // so click/drag panning works over empty regions, not just node rects.
            Background = new SolidColorBrush(Colors.Transparent)
        };
        MiniMapViewport = new Border
        {
            BorderBrush = BoardTheme.ResolveBrush("BoardCanvasMiniMapViewportBorderBrush", Colors.SteelBlue),
            BorderThickness = new Thickness(1.5),
            Background = BoardTheme.ResolveBrush("BoardCanvasMiniMapViewportBrush", Colors.LightBlue),
            IsHitTestVisible = false
        };

        var controlsPanel = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(12),
            CornerRadius = new CornerRadius(10),
            Background = BoardTheme.ResolveBrush("BoardCanvasPanelBrush", Colors.WhiteSmoke),
            BorderBrush = BoardTheme.ResolveBrush("BoardCanvasPanelBorderBrush", Colors.LightGray),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 0,
                Children =
                {
                    ZoomInButton,
                    BuildControlSeparator(),
                    ZoomOutButton,
                    BuildControlSeparator(),
                    FitCanvasButton,
                }
            }
        };

        var miniMapPanel = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Padding = new Thickness(6),
            Margin = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            Background = BoardTheme.ResolveBrush("BoardCanvasMiniMapBrush", Colors.WhiteSmoke),
            BorderBrush = BoardTheme.ResolveBrush("BoardCanvasMiniMapBorderBrush", Colors.LightGray),
            BorderThickness = new Thickness(1),
            Child = new Border
            {
                CornerRadius = new CornerRadius(8),
                Child = new Grid
                {
                    Width = 200,
                    Height = 128,
                    Children =
                    {
                        MiniMapHost,
                        MiniMapViewport,
                    }
                }
            }
        };

        var canvasLayer = new Grid();
        canvasLayer.Children.Add(CanvasScrollViewer);
        canvasLayer.Children.Add(controlsPanel);
        canvasLayer.Children.Add(TokenFocusBanner);
        canvasLayer.Children.Add(miniMapPanel);

        Content = canvasLayer;

        Unloaded += OnUnloaded;
        CanvasScrollViewer.ViewChanged += OnCanvasViewChanged;
        CanvasScrollViewer.SizeChanged += (_, _) => UpdateMiniMapViewport();
        ClearTokenFocusButton.Click += (_, _) => ToggleTokenFocus(null);
        ZoomInButton.Click += (_, _) => AdjustZoom(1.1f);
        ZoomOutButton.Click += (_, _) => AdjustZoom(1f / 1.1f);
        FitCanvasButton.Click += (_, _) => FitToCards(lastCards.Select(card => card.Id));
        MiniMapHost.PointerPressed += OnMiniMapPointerPressed;
        MiniMapHost.PointerMoved += OnMiniMapPointerMoved;
        MiniMapHost.PointerReleased += OnMiniMapPointerReleased;
        MiniMapHost.PointerCanceled += OnMiniMapPointerReleased;
        // handledEventsToo so we still receive the wheel after the ScrollViewer
        // processes it; we only take over when Ctrl is held (zoom-to-cursor),
        // otherwise the ScrollViewer keeps panning on plain/Shift wheel.
        CanvasScrollViewer.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnCanvasPointerWheelChanged),
            true);
    }

    public void Render(
        BoardInfoState boardInfo,
        BoardSummaryState summary,
        IReadOnlyList<BoardCard> cards,
        BoardCanvasLayoutState layoutState,
        IReadOnlyDictionary<string, string> dataObjects,
        IReadOnlyList<RendererRule>? rendererRules = null)
    {
        lastBoardInfo = boardInfo;
        lastSummary = summary;
        lastCards = cards;
        lastLayoutState = layoutState;
        lastRendererRules = rendererRules;
        lastDataObjects = dataObjects;

        bool boardChanged = !string.Equals(currentBoardId, boardInfo.BoardId, System.StringComparison.Ordinal);
        currentBoardId = boardInfo.BoardId;
        if (boardChanged)
        {
            selectedToken = null;
            focusedCardId = null;
            savedViewportBeforeTokenFocus = null;
            savedViewportBeforeCardFocus = null;
        }

        CardsHost.Children.Clear();
        CardsHost.Width = 0;
        CardsHost.Height = 0;

        if (cards.Count == 0)
        {
            return;
        }

        IReadOnlyDictionary<string, BoardCanvasPlacement> placements = BoardCanvasLayoutEngine.BuildPlacements(cards, layoutState);
        lastPlacements = placements;
        HashSet<string> highlightedCardIds = selectedToken is null
            ? cards.Select(card => card.Id).ToHashSet(System.StringComparer.Ordinal)
            : cards.Where(card => card.Requires.Contains(selectedToken, System.StringComparer.Ordinal) || card.Provides.Contains(selectedToken, System.StringComparer.Ordinal))
                .Select(card => card.Id)
                .ToHashSet(System.StringComparer.Ordinal);
        RenderBackgroundGrid();

        double maxRight = 0;
        double maxBottom = 0;

        foreach (BoardCard card in cards)
        {
            bool isHighlighted = highlightedCardIds.Contains(card.Id);
            bool isFocused = string.Equals(focusedCardId, card.Id, System.StringComparison.Ordinal);
            bool hasCardFocus = !string.IsNullOrWhiteSpace(focusedCardId);
            bool isDimmed = (selectedToken is not null && !isHighlighted) || (hasCardFocus && !isFocused);

            var cardSummary = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = card.Title,
                        FontSize = 16,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        TextWrapping = TextWrapping.WrapWholeWords
                    },
                    new TextBlock
                    {
                        Text = card.ViewKinds.Count > 0 ? $"{card.Id}  •  {string.Join(", ", card.ViewKinds)}" : card.Id,
                        Opacity = 0.72,
                        TextWrapping = TextWrapping.WrapWholeWords
                    },
                    new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(card.Status) ? "Ready" : $"Status: {card.Status}",
                        Opacity = 0.76,
                        TextWrapping = TextWrapping.WrapWholeWords
                    }
                }
            };
            var host = new Border
            {
                Child = new Border
                {
                    MinHeight = 120,
                    Padding = new Thickness(14),
                    Background = BoardTheme.CreateResourceBrush("CardBackgroundFillColorSecondaryBrush", 0x55, Colors.Transparent),
                    CornerRadius = new CornerRadius(12),
                    Child = cardSummary
                },
                Tag = card.Id,
                Padding = new Thickness(4),
                CornerRadius = new CornerRadius(16),
                Opacity = isDimmed ? 0.42 : 1,
                BorderThickness = isFocused
                    ? new Thickness(2.5)
                    : isHighlighted && selectedToken is not null ? new Thickness(2) : new Thickness(0),
                BorderBrush = isFocused
                    ? BoardTheme.CreateResourceBrush("BoardColorAccentStrong", 0xCC, Colors.CornflowerBlue)
                    : isHighlighted && selectedToken is not null ? BoardTheme.CreateResourceBrush("BoardColorAccent", 0xAA, Colors.SteelBlue) : null,
            };
            if (placements.TryGetValue(card.Id, out BoardCanvasPlacement? placement))
            {
                host.Width = placement.Width;
                host.MinHeight = placement.Height;
                Canvas.SetLeft(host, placement.X);
                Canvas.SetTop(host, placement.Y);
                maxRight = System.Math.Max(maxRight, placement.X + placement.Width);
                maxBottom = System.Math.Max(maxBottom, placement.Y + placement.Height);
            }
            host.PointerPressed += OnCardPointerPressed;
            host.PointerMoved += OnCardPointerMoved;
            host.PointerReleased += OnCardPointerReleased;
            host.PointerCanceled += OnCardPointerCanceled;
            host.DoubleTapped += OnCardDoubleTapped;
            CardsHost.Children.Add(host);
        }

        CardsHost.Width = System.Math.Max(maxRight + 80, 960);
        CardsHost.Height = System.Math.Max(maxBottom + 80, 540);
        BackgroundGridHost.Width = CardsHost.Width;
        BackgroundGridHost.Height = CardsHost.Height;
        CanvasSurface.Width = CardsHost.Width;
        CanvasSurface.Height = CardsHost.Height;
        RenderBackgroundGrid();
        UpdateMiniMap();

        if (selectedToken is null)
        {
            TokenFocusBanner.Visibility = Visibility.Collapsed;
        }
        else
        {
            TokenFocusBanner.Visibility = Visibility.Visible;
            ClearTokenFocusButton.Content = $"{selectedToken} ×";
        }

        if (boardChanged && layoutState.Viewport is not null)
        {
            suppressViewportUpdates = true;
            CanvasScrollViewer.ChangeView(layoutState.Viewport.X, layoutState.Viewport.Y, (float)layoutState.Viewport.Zoom, true);
        }
    }

    private async void PersistLayoutAsync()
    {
        if (string.IsNullOrWhiteSpace(currentBoardId))
        {
            return;
        }

        var app = (App)Application.Current;
        try
        {
            await app.BoardClient.SaveLayoutAsync(currentBoardId, app.BoardStore.GetCanvasLayout());
        }
        catch
        {
            // Ignore persistence failures; the in-memory layout state remains authoritative for the current session.
        }
    }

    private void OnCanvasViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(currentBoardId))
        {
            return;
        }

        if (suppressViewportUpdates)
        {
            if (!e.IsIntermediate)
            {
                suppressViewportUpdates = false;
            }
            return;
        }

        var app = (App)Application.Current;
        app.BoardStore.SetCanvasViewport(CanvasScrollViewer.HorizontalOffset, CanvasScrollViewer.VerticalOffset, CanvasScrollViewer.ZoomFactor);
        UpdateMiniMapViewport();
        if (!e.IsIntermediate)
        {
            PersistLayoutAsync();
        }
    }

    private void OnCardPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (resizingHost is not null)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source
            && FindAncestor<Button>(source) is not null)
        {
            return;
        }

        if (sender is not FrameworkElement host || host.Tag is not string cardId || string.IsNullOrWhiteSpace(cardId))
        {
            return;
        }

        draggingHost = host;
        draggingCardId = cardId;
        dragStartPoint = e.GetCurrentPoint(CardsHost).Position;
        dragStartLeft = Canvas.GetLeft(host);
        dragStartTop = Canvas.GetTop(host);
        host.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnCardPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (draggingHost is null || !ReferenceEquals(sender, draggingHost) || draggingCardId is null)
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(CardsHost);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        double nextLeft = System.Math.Max(0, dragStartLeft + (point.Position.X - dragStartPoint.X));
        double nextTop = System.Math.Max(0, dragStartTop + (point.Position.Y - dragStartPoint.Y));
        Canvas.SetLeft(draggingHost, nextLeft);
        Canvas.SetTop(draggingHost, nextTop);
        e.Handled = true;
    }

    private void OnCardPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        CommitDrag();
        e.Handled = true;
    }

    private void OnCardPointerCanceled(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        CommitDrag();
        e.Handled = true;
    }

    private void CommitDrag()
    {
        if (draggingHost is null || draggingCardId is null)
        {
            return;
        }

        double left = Canvas.GetLeft(draggingHost);
        double top = Canvas.GetTop(draggingHost);
        draggingHost.ReleasePointerCaptures();
        ((App)Application.Current).BoardStore.SetCanvasCardPosition(draggingCardId, left, top);
        draggingHost = null;
        draggingCardId = null;
        PersistLayoutAsync();
    }

    private void OnResizePointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement handle || handle.Tag is not FrameworkElement host || host.Tag is not string cardId)
        {
            return;
        }

        resizingHost = host;
        resizingCardId = cardId;
        resizeStartPoint = e.GetCurrentPoint(CardsHost).Position;
        resizeStartWidth = host.Width > 0 ? host.Width : host.ActualWidth;
        handle.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnResizePointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (resizingHost is null || resizingCardId is null)
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(CardsHost);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        double nextWidth = ClampCardWidth(resizeStartWidth + (point.Position.X - resizeStartPoint.X));
        resizingHost.Width = nextWidth;
        e.Handled = true;
    }

    private void OnResizePointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        CommitResize();
        e.Handled = true;
    }

    private void OnResizePointerCanceled(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        CommitResize();
        e.Handled = true;
    }

    private void CommitResize()
    {
        if (resizingHost is null || resizingCardId is null)
        {
            return;
        }

        double width = resizingHost.Width > 0 ? resizingHost.Width : resizingHost.ActualWidth;
        resizingHost.ReleasePointerCaptures();
        ((App)Application.Current).BoardStore.SetCanvasCardWidth(resizingCardId, width);
        resizingHost = null;
        resizingCardId = null;
        PersistLayoutAsync();
    }

    private static double ClampCardWidth(double width)
    {
        return System.Math.Max(MinCardWidth, System.Math.Min(MaxCardWidth, System.Math.Round(width)));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        PersistLayoutAsync();
    }

    private void AdjustZoom(float multiplier)
    {
        double viewportWidth = CanvasScrollViewer.ActualWidth > 0 ? CanvasScrollViewer.ActualWidth : 1;
        double viewportHeight = CanvasScrollViewer.ActualHeight > 0 ? CanvasScrollViewer.ActualHeight : 1;
        ZoomToPoint(new Point(viewportWidth / 2, viewportHeight / 2), multiplier);
    }

    private void OnCanvasPointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        // Plain / Shift wheel keeps the ScrollViewer's native pan behaviour
        // (mirrors ReactFlow panOnScroll); Ctrl+wheel zooms toward the cursor.
        if (!e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control))
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(CanvasScrollViewer);
        float factor = point.Properties.MouseWheelDelta > 0 ? 1.1f : 1f / 1.1f;
        ZoomToPoint(point.Position, factor);
        e.Handled = true;
    }

    private void ZoomToPoint(Point viewportPoint, float multiplier)
    {
        float currentZoom = CanvasScrollViewer.ZoomFactor > 0 ? CanvasScrollViewer.ZoomFactor : 1;
        float targetZoom = (float)System.Math.Max(0.24, System.Math.Min(1.35, currentZoom * multiplier));
        if (System.Math.Abs(targetZoom - currentZoom) < 0.0005)
        {
            return;
        }

        // Keep the content point under the cursor anchored while zooming.
        double contentX = (CanvasScrollViewer.HorizontalOffset + viewportPoint.X) / currentZoom;
        double contentY = (CanvasScrollViewer.VerticalOffset + viewportPoint.Y) / currentZoom;
        double targetHorizontal = System.Math.Max(0, (contentX * targetZoom) - viewportPoint.X);
        double targetVertical = System.Math.Max(0, (contentY * targetZoom) - viewportPoint.Y);
        suppressViewportUpdates = true;
        CanvasScrollViewer.ChangeView(targetHorizontal, targetVertical, targetZoom, false);
    }

    private void OnMiniMapPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (CardsHost.Width <= 0 || CardsHost.Height <= 0)
        {
            return;
        }

        isMiniMapDragging = true;
        MiniMapHost.CapturePointer(e.Pointer);
        PanToMiniMapPoint(e.GetCurrentPoint(MiniMapHost).Position);
        e.Handled = true;
    }

    private void OnMiniMapPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!isMiniMapDragging)
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(MiniMapHost);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        PanToMiniMapPoint(point.Position);
        e.Handled = true;
    }

    private void OnMiniMapPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!isMiniMapDragging)
        {
            return;
        }

        isMiniMapDragging = false;
        MiniMapHost.ReleasePointerCaptures();
        e.Handled = true;
    }

    private void PanToMiniMapPoint(Point point)
    {
        if (CardsHost.Width <= 0 || CardsHost.Height <= 0)
        {
            return;
        }

        double scale = System.Math.Min(MiniMapWidth / CardsHost.Width, MiniMapHeight / CardsHost.Height);
        if (scale <= 0)
        {
            return;
        }

        double contentX = point.X / scale;
        double contentY = point.Y / scale;
        double zoom = CanvasScrollViewer.ZoomFactor > 0 ? CanvasScrollViewer.ZoomFactor : 1;
        double targetHorizontal = System.Math.Max(0, (contentX * zoom) - (CanvasScrollViewer.ActualWidth / 2));
        double targetVertical = System.Math.Max(0, (contentY * zoom) - (CanvasScrollViewer.ActualHeight / 2));
        suppressViewportUpdates = true;
        CanvasScrollViewer.ChangeView(targetHorizontal, targetVertical, CanvasScrollViewer.ZoomFactor, false);
    }

    private void OnCardDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement host || host.Tag is not string cardId)
        {
            return;
        }

        ToggleCardFocus(cardId, host);
        e.Handled = true;
    }

    private void ToggleTokenFocus(string? token)
    {
        string? normalized = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        if (string.Equals(selectedToken, normalized, System.StringComparison.Ordinal))
        {
            selectedToken = null;
            if (savedViewportBeforeTokenFocus is not null)
            {
                RestoreViewport(savedViewportBeforeTokenFocus);
                savedViewportBeforeTokenFocus = null;
            }
        }
        else
        {
            if (selectedToken is null)
            {
                savedViewportBeforeTokenFocus = CaptureViewport();
            }
            selectedToken = normalized;
        }

        RerenderCurrentState();
        if (selectedToken is not null)
        {
            FitToCards(lastCards.Where(card => card.Requires.Contains(selectedToken, System.StringComparer.Ordinal) || card.Provides.Contains(selectedToken, System.StringComparer.Ordinal)).Select(card => card.Id));
        }
    }

    private void ToggleCardFocus(string cardId, FrameworkElement host)
    {
        if (string.Equals(focusedCardId, cardId, System.StringComparison.Ordinal))
        {
            focusedCardId = null;
            RerenderCurrentState();
            if (savedViewportBeforeCardFocus is not null)
            {
                RestoreViewport(savedViewportBeforeCardFocus);
                savedViewportBeforeCardFocus = null;
            }
            return;
        }

        if (focusedCardId is null)
        {
            savedViewportBeforeCardFocus = CaptureViewport();
        }
        focusedCardId = cardId;
        RerenderCurrentState();
        FocusHost(host);
    }

    private void FocusHost(FrameworkElement host)
    {
        double viewportWidth = CanvasScrollViewer.ActualWidth > 0 ? CanvasScrollViewer.ActualWidth : 1200;
        double viewportHeight = CanvasScrollViewer.ActualHeight > 0 ? CanvasScrollViewer.ActualHeight : 800;
        double nodeWidth = host.Width > 0 ? host.Width : host.ActualWidth;
        double nodeHeight = host.ActualHeight > 0 ? host.ActualHeight : host.Height;
        if (nodeWidth <= 0 || nodeHeight <= 0)
        {
            return;
        }

        double zoomForHeight = (viewportHeight * 0.9) / nodeHeight;
        double zoomForWidth = (viewportWidth * 0.95) / nodeWidth;
        double targetZoom = System.Math.Min(zoomForHeight, zoomForWidth);
        targetZoom = System.Math.Max(0.35, System.Math.Min(1.35, targetZoom));
        double centerX = Canvas.GetLeft(host) + (nodeWidth / 2);
        double centerY = Canvas.GetTop(host) + (nodeHeight / 2);
        double targetHorizontal = System.Math.Max(0, (centerX * targetZoom) - (viewportWidth / 2));
        double targetVertical = System.Math.Max(0, (centerY * targetZoom) - (viewportHeight / 2));
        suppressViewportUpdates = true;
        CanvasScrollViewer.ChangeView(targetHorizontal, targetVertical, (float)targetZoom, false);
    }

    private void FitToCards(IEnumerable<string> cardIds)
    {
        HashSet<string> ids = cardIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(System.StringComparer.Ordinal);
        if (ids.Count == 0)
        {
            return;
        }

        FrameworkElement[] hosts = CardsHost.Children
            .OfType<FrameworkElement>()
            .Where(element => element.Tag is string cardId && ids.Contains(cardId))
            .ToArray();
        if (hosts.Length == 0)
        {
            return;
        }

        double minLeft = hosts.Min(host => Canvas.GetLeft(host));
        double minTop = hosts.Min(host => Canvas.GetTop(host));
        double maxRight = hosts.Max(host => Canvas.GetLeft(host) + (host.Width > 0 ? host.Width : host.ActualWidth));
        double maxBottom = hosts.Max(host => Canvas.GetTop(host) + (host.ActualHeight > 0 ? host.ActualHeight : host.Height));
        double boundsWidth = System.Math.Max(1, maxRight - minLeft);
        double boundsHeight = System.Math.Max(1, maxBottom - minTop);
        double viewportWidth = CanvasScrollViewer.ActualWidth > 0 ? CanvasScrollViewer.ActualWidth : 1200;
        double viewportHeight = CanvasScrollViewer.ActualHeight > 0 ? CanvasScrollViewer.ActualHeight : 800;
        double targetZoom = System.Math.Min((viewportWidth * 0.7) / boundsWidth, (viewportHeight * 0.7) / boundsHeight);
        targetZoom = System.Math.Max(0.5, System.Math.Min(1.08, targetZoom));
        double centerX = minLeft + (boundsWidth / 2);
        double centerY = minTop + (boundsHeight / 2);
        double targetHorizontal = System.Math.Max(0, (centerX * targetZoom) - (viewportWidth / 2));
        double targetVertical = System.Math.Max(0, (centerY * targetZoom) - (viewportHeight / 2));
        suppressViewportUpdates = true;
        CanvasScrollViewer.ChangeView(targetHorizontal, targetVertical, (float)targetZoom, false);
    }

    private BoardCanvasViewportState CaptureViewport()
    {
        return new BoardCanvasViewportState(CanvasScrollViewer.HorizontalOffset, CanvasScrollViewer.VerticalOffset, CanvasScrollViewer.ZoomFactor);
    }

    private void RestoreViewport(BoardCanvasViewportState viewport)
    {
        suppressViewportUpdates = true;
        CanvasScrollViewer.ChangeView(viewport.X, viewport.Y, (float)viewport.Zoom, false);
    }

    private void RerenderCurrentState()
    {
        if (lastBoardInfo is null || lastSummary is null)
        {
            return;
        }

        Render(lastBoardInfo, lastSummary, lastCards, lastLayoutState, lastDataObjects, lastRendererRules);
    }

    private static IReadOnlyList<BoardConnection> BuildConnections(IReadOnlyList<BoardCard> cards)
    {
        var tokenProviders = new Dictionary<string, List<string>>(System.StringComparer.Ordinal);
        foreach (BoardCard card in cards)
        {
            foreach (string token in card.Provides.Where(token => !string.IsNullOrWhiteSpace(token)))
            {
                if (!tokenProviders.TryGetValue(token, out List<string>? providers))
                {
                    providers = new List<string>();
                    tokenProviders[token] = providers;
                }

                providers.Add(card.Id);
            }
        }

        var connections = new List<BoardConnection>();
        foreach (BoardCard card in cards)
        {
            foreach (string token in card.Requires.Where(token => !string.IsNullOrWhiteSpace(token)))
            {
                if (!tokenProviders.TryGetValue(token, out List<string>? providers))
                {
                    continue;
                }

                foreach (string providerId in providers.Where(providerId => !string.Equals(providerId, card.Id, System.StringComparison.Ordinal)))
                {
                    connections.Add(new BoardConnection(providerId, card.Id, token, string.Equals(card.Status, "running", System.StringComparison.OrdinalIgnoreCase)));
                }
            }
        }

        return connections;
    }

    private IEnumerable<UIElement> BuildConnectionVisuals(BoardCanvasPlacement source, BoardCanvasPlacement target, bool isHighlighted, bool isDimmed, bool isRunning)
    {
        double startX = source.X + (source.Width / 2);
        double startY = source.Y + source.Height;
        double endX = target.X + (target.Width / 2);
        double endY = target.Y;
        double horizontalDistance = System.Math.Abs(endX - startX);
        double verticalDistance = System.Math.Abs(endY - startY);
        double deltaY = horizontalDistance < 180 && verticalDistance < 120
            ? 42
            : horizontalDistance > 520 || verticalDistance > 320
                ? 90
                : System.Math.Max(60, verticalDistance * 0.38);

        var figure = new PathFigure
        {
            StartPoint = new Point(startX, startY),
            Segments =
            {
                new BezierSegment
                {
                    Point1 = new Point(startX, startY + deltaY),
                    Point2 = new Point(endX, endY - deltaY),
                    Point3 = new Point(endX, endY)
                }
            }
        };

        var geometry = new PathGeometry { Figures = { figure } };
        Windows.UI.Color accentColor = BoardTheme.ResolveColor("BoardColorAccent", Colors.SteelBlue);
        Windows.UI.Color accentStrongColor = BoardTheme.ResolveColor("BoardColorAccentStrong", Colors.CornflowerBlue);
        Windows.UI.Color runningColor = BoardTheme.ResolveColor("BoardStatusRunningColor", Colors.Goldenrod);
        Windows.UI.Color baseColor = isHighlighted
            ? Windows.UI.Color.FromArgb(0xD8, accentStrongColor.R, accentStrongColor.G, accentStrongColor.B)
            : isDimmed
                ? Windows.UI.Color.FromArgb(0x34, accentColor.R, accentColor.G, accentColor.B)
                : Windows.UI.Color.FromArgb(0x70, accentColor.R, accentColor.G, accentColor.B);
        if (isRunning)
        {
            var flowPath = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = geometry,
                Stroke = new SolidColorBrush(isHighlighted
                    ? Windows.UI.Color.FromArgb(0xCC, accentStrongColor.R, accentStrongColor.G, accentStrongColor.B)
                    : Windows.UI.Color.FromArgb(0xA8, runningColor.R, runningColor.G, runningColor.B)),
                StrokeThickness = isHighlighted ? 3.1 : 2.3,
                StrokeDashArray = new DoubleCollection { 2, 3 },
                Opacity = isDimmed ? 0.32 : 0.9,
                IsHitTestVisible = false,
            };
            yield return flowPath;
        }

        var mainPath = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(baseColor),
            StrokeThickness = isHighlighted ? 2.4 : 1.5,
            IsHitTestVisible = false,
        };
        yield return mainPath;

        var startPlug = new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = new SolidColorBrush(baseColor),
            Opacity = isDimmed ? 0.42 : 1,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(startPlug, startX - 3);
        Canvas.SetTop(startPlug, startY - 3);
        yield return startPlug;

        var endPlug = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = new SolidColorBrush(baseColor),
            Opacity = isDimmed ? 0.42 : 1,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(endPlug, endX - 4);
        Canvas.SetTop(endPlug, endY - 4);
        yield return endPlug;
    }

    private static FrameworkElement BuildConnectionLabel(string token, BoardCanvasPlacement source, BoardCanvasPlacement target, bool isHighlighted, bool isDimmed)
    {
        double centerX = ((source.X + (source.Width / 2)) + (target.X + (target.Width / 2))) / 2;
        double centerY = ((source.Y + source.Height) + target.Y) / 2;
        var label = new Border
        {
            Padding = new Thickness(4, 1, 4, 1),
            Background = isHighlighted
                ? BoardTheme.CreateResourceBrush("BoardColorAccentSoft", 0xF0, Colors.LightBlue)
                : BoardTheme.CreateResourceBrush("BoardColorSurfaceStrong", 0xD8, Colors.White),
            Opacity = isDimmed ? 0.42 : 1,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = token,
                FontSize = 11,
                Opacity = 0.84,
            }
        };
        Canvas.SetLeft(label, System.Math.Max(0, centerX - 24));
        Canvas.SetTop(label, System.Math.Max(0, centerY - 10));
        return label;
    }

    private static FrameworkElement BuildTokenPanel(
        IReadOnlyList<string> tokens,
        BoardCard card,
        bool isProvide,
        IReadOnlyDictionary<string, string> dataObjects,
        System.Action<string?> toggleTokenFocus)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = tokens.Count == 0 ? Visibility.Collapsed : Visibility.Visible,
        };

        foreach (string token in tokens)
        {
            bool isActive = isProvide ? dataObjects.ContainsKey(token) : true;
            bool isMissing = !isProvide && !dataObjects.ContainsKey(token);
            var button = new Button
            {
                Content = token,
                Padding = new Thickness(8, 2, 8, 2),
                MinWidth = 0,
                FontSize = 11,
                CornerRadius = new CornerRadius(10),
                Background = isProvide
                    ? BoardTheme.CreateStatusBrush("completed", isActive ? (byte)0x66 : (byte)0x33)
                    : isMissing ? BoardTheme.CreateStatusBrush("failed", 0x44) : BoardTheme.CreateStatusBrush("fresh", 0x33),
                BorderBrush = isProvide
                    ? BoardTheme.CreateStatusBrush("completed", 0x88)
                    : isMissing ? BoardTheme.CreateStatusBrush("failed", 0x99) : BoardTheme.CreateStatusBrush("fresh", 0x88),
                BorderThickness = new Thickness(1),
            };
            string capturedToken = token;
            button.Click += (_, _) => toggleTokenFocus(capturedToken);
            row.Children.Add(button);
        }

        return row;
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        DependencyObject? current = start;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static Button BuildControlButton(string svgPath, string tooltip)
    {
        var button = new Button
        {
            Width = 36,
            Height = 32,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new Image
            {
                Width = 16,
                Height = 16,
                Source = HostIconSources.CreateSvg(svgPath),
            },
        };
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    private static Border BuildControlSeparator()
    {
        return new Border
        {
            Height = 1,
            Margin = new Thickness(4, 0, 4, 0),
            Background = BoardTheme.CreateResourceBrush("BoardColorBorderStrong", 0x18, Colors.SlateGray),
        };
    }

    private void RenderBackgroundGrid()
    {
        BackgroundGridHost.Children.Clear();
        if (CardsHost.Width <= 0 || CardsHost.Height <= 0)
        {
            return;
        }

        // Dot pattern matching ReactFlow's default <Background variant="dots" />.
        // Widen the effective gap if the surface is large so the dot count stays
        // bounded (each dot is a visual-tree element).
        double gap = BackgroundGridGap;
        while ((CardsHost.Width / gap) * (CardsHost.Height / gap) > 6000)
        {
            gap *= 2;
        }

        Brush dotBrush = BoardTheme.CreateResourceBrush("BoardColorBorderStrong", 0x2A, Colors.SlateGray);
        const double dotSize = 1.6;
        for (double x = 0; x <= CardsHost.Width; x += gap)
        {
            for (double y = 0; y <= CardsHost.Height; y += gap)
            {
                var dot = new Ellipse
                {
                    Width = dotSize,
                    Height = dotSize,
                    Fill = dotBrush,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(dot, x - (dotSize / 2));
                Canvas.SetTop(dot, y - (dotSize / 2));
                BackgroundGridHost.Children.Add(dot);
            }
        }
    }

    private void UpdateMiniMap()
    {
        MiniMapHost.Children.Clear();
        if (lastPlacements.Count == 0 || CardsHost.Width <= 0 || CardsHost.Height <= 0)
        {
            UpdateMiniMapViewport();
            return;
        }

        double scale = System.Math.Min(MiniMapWidth / CardsHost.Width, MiniMapHeight / CardsHost.Height);
        foreach (BoardCard card in lastCards)
        {
            if (!lastPlacements.TryGetValue(card.Id, out BoardCanvasPlacement? placement))
            {
                continue;
            }

            var rect = new Border
            {
                Width = System.Math.Max(8, placement.Width * scale),
                Height = System.Math.Max(6, placement.Height * scale),
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(GetMiniMapNodeColor(card.Status)),
                BorderBrush = new SolidColorBrush(GetMiniMapNodeStrokeColor(card.Status)),
                BorderThickness = new Thickness(1),
                Opacity = 0.9,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(rect, placement.X * scale);
            Canvas.SetTop(rect, placement.Y * scale);
            MiniMapHost.Children.Add(rect);
        }

        UpdateMiniMapViewport();
    }

    private void UpdateMiniMapViewport()
    {
        if (CardsHost.Width <= 0 || CardsHost.Height <= 0)
        {
            MiniMapViewport.Visibility = Visibility.Collapsed;
            return;
        }

        double scale = System.Math.Min(MiniMapWidth / CardsHost.Width, MiniMapHeight / CardsHost.Height);
        double zoom = CanvasScrollViewer.ZoomFactor > 0 ? CanvasScrollViewer.ZoomFactor : 1;
        double left = (CanvasScrollViewer.HorizontalOffset / zoom) * scale;
        double top = (CanvasScrollViewer.VerticalOffset / zoom) * scale;
        double viewportWidth = (CanvasScrollViewer.ActualWidth / zoom) * scale;
        double viewportHeight = (CanvasScrollViewer.ActualHeight / zoom) * scale;
        MiniMapViewport.Visibility = Visibility.Visible;
        MiniMapViewport.Width = System.Math.Max(12, viewportWidth);
        MiniMapViewport.Height = System.Math.Max(12, viewportHeight);
        MiniMapViewport.Margin = new Thickness(left, top, 0, 0);
    }

    private static Windows.UI.Color GetMiniMapNodeColor(string status)
    {
        Windows.UI.Color resolved = BoardTheme.ResolveColor(
            string.Equals(status, "running", System.StringComparison.OrdinalIgnoreCase)
                ? "BoardStatusCompletedColor"
                : "BoardStatusFreshColor",
            string.Equals(status, "running", System.StringComparison.OrdinalIgnoreCase) ? Colors.SeaGreen : Colors.SlateGray);
        return Windows.UI.Color.FromArgb(
            string.Equals(status, "running", System.StringComparison.OrdinalIgnoreCase) ? (byte)0xEA : (byte)0x94,
            resolved.R,
            resolved.G,
            resolved.B);
    }

    private static Windows.UI.Color GetMiniMapNodeStrokeColor(string status)
    {
        Windows.UI.Color resolved = BoardTheme.ResolveColor(
            string.Equals(status, "running", System.StringComparison.OrdinalIgnoreCase)
                ? "BoardStatusCompletedColor"
                : "BoardStatusFreshColor",
            string.Equals(status, "running", System.StringComparison.OrdinalIgnoreCase) ? Colors.SeaGreen : Colors.SlateGray);
        return Windows.UI.Color.FromArgb(
            string.Equals(status, "running", System.StringComparison.OrdinalIgnoreCase) ? (byte)0xF6 : (byte)0x8A,
            resolved.R,
            resolved.G,
            resolved.B);
    }

    private sealed record BoardConnection(string SourceCardId, string TargetCardId, string Token, bool IsRunning);
}
