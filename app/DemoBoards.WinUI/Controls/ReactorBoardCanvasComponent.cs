using System;
using System.Collections.Generic;
using System.Linq;
using DemoBoards.RuntimeHost;
using DemoBoards_WinUI.Assets;
using DemoBoards_WinUI.State;
using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls;

public sealed record ReactorBoardCanvasProps(
    BoardInfoState BoardInfo,
    BoardSummaryState Summary,
    IReadOnlyList<BoardCard> Cards,
    BoardCanvasLayoutState LayoutState,
    IReadOnlyDictionary<string, string> DataObjects,
    IReadOnlyList<RendererRule>? RendererRules);

public sealed class ReactorBoardCanvasComponent : Component<ReactorBoardCanvasProps>
{
    private const double MiniMapWidth = 200;
    private const double MiniMapHeight = 128;
    private const double MiniMapPadding = 8;
    private const double BackgroundGridGap = 24;
    private const double MinViewportWidth = 1;
    private const double MinViewportHeight = 1;

    public override Element Render()
    {
        var scrollViewerRef = UseRef<ScrollViewer?>(null);
        var boardIdRef = UseRef(string.Empty);

        var (selectedToken, setSelectedToken) = UseState<string?>(null);
        var (focusedCardId, setFocusedCardId) = UseState<string?>(null);
        var (savedViewportBeforeTokenFocus, setSavedViewportBeforeTokenFocus) = UseState<BoardCanvasViewportState?>(null);
        var (savedViewportBeforeCardFocus, setSavedViewportBeforeCardFocus) = UseState<BoardCanvasViewportState?>(null);
        var (viewportState, setViewportState) = UseState(Props.LayoutState.Viewport ?? new BoardCanvasViewportState(0, 0, 1));

        IReadOnlyDictionary<string, BoardCanvasPlacement> placements = BoardCanvasLayoutEngine.BuildPlacements(Props.Cards, Props.LayoutState);
        HashSet<string> highlightedCardIds = BuildHighlightedCardIds(Props.Cards, selectedToken);
        double surfaceWidth = Math.Max(960, placements.Count == 0 ? 960 : placements.Values.Max(placement => placement.X + placement.Width) + 80);
        double surfaceHeight = Math.Max(540, placements.Count == 0 ? 540 : placements.Values.Max(placement => placement.Y + placement.Height) + 80);
        BoardConnection[] connections = BuildConnections(Props.Cards).ToArray();
        Rect viewportWorldRect = ResolveViewportWorldRect(scrollViewerRef.Current, viewportState);
        BoardMiniMapNode[] miniMapNodes = BuildMiniMapNodes(Props.Cards, placements);

        UseEffect(() =>
        {
            if (string.Equals(boardIdRef.Current, Props.BoardInfo.BoardId, StringComparison.Ordinal))
            {
                return;
            }

            boardIdRef.Current = Props.BoardInfo.BoardId;
            setSelectedToken(null);
            setFocusedCardId(null);
            setSavedViewportBeforeTokenFocus(null);
            setSavedViewportBeforeCardFocus(null);

            BoardCanvasViewportState nextViewport = Props.LayoutState.Viewport ?? new BoardCanvasViewportState(0, 0, 1);
            setViewportState(nextViewport);

            if (scrollViewerRef.Current is null)
            {
                return;
            }

            if (Props.LayoutState.Viewport is not null)
            {
                ApplyViewport(scrollViewerRef.Current, Props.LayoutState.Viewport, disableAnimation: true);
                return;
            }

            FitToCards(scrollViewerRef.Current, placements, Props.Cards.Select(card => card.Id));
        }, Props.BoardInfo.BoardId);

        Element[] canvasChildren = BuildBackgroundDots(surfaceWidth, surfaceHeight)
            .Concat(BuildConnectionElements(connections, placements, highlightedCardIds, focusedCardId))
            .Concat(BuildCardElements(
                placements,
                highlightedCardIds,
                selectedToken,
                focusedCardId,
                setSelectedToken,
                setFocusedCardId,
                setSavedViewportBeforeTokenFocus,
                setSavedViewportBeforeCardFocus,
                savedViewportBeforeTokenFocus,
                savedViewportBeforeCardFocus,
                viewportState,
                () => scrollViewerRef.Current,
                setViewportState))
            .ToArray();

        return Border(
                VStack(12,
                    BuildTokenBanner(selectedToken, setSelectedToken, savedViewportBeforeTokenFocus, scrollViewerRef.Current, setViewportState, setSavedViewportBeforeTokenFocus),
                    ScrollViewer(
                        Canvas(canvasChildren) with
                        {
                            Width = surfaceWidth,
                            Height = surfaceHeight,
                            Background = new SolidColorBrush(Colors.Transparent),
                        })
                        .ZoomMode(ZoomMode.Enabled)
                        .VerticalScrollMode(ScrollMode.Enabled)
                        .ViewChanged(args =>
                        {
                            if (scrollViewerRef.Current is null)
                            {
                                return;
                            }

                            BoardCanvasViewportState nextViewport = CaptureViewport(scrollViewerRef.Current);
                            setViewportState(nextViewport);
                            App.Current.BoardStore.SetCanvasViewport(nextViewport.X, nextViewport.Y, nextViewport.Zoom);
                            if (!args.IsIntermediate)
                            {
                                PersistLayoutAsync(Props.BoardInfo.BoardId);
                            }
                        })
                        .OnPointerWheelChanged((scrollViewer, args) =>
                        {
                            if (scrollViewerRef.Current is null)
                            {
                                return;
                            }

                            ScrollViewer currentScrollViewer = scrollViewerRef.Current;
                            if (!args.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control))
                            {
                                return;
                            }

                            float factor = args.GetCurrentPoint(currentScrollViewer).Properties.MouseWheelDelta > 0 ? 1.1f : 1f / 1.1f;
                            ZoomToPoint(currentScrollViewer, args.GetCurrentPoint(currentScrollViewer).Position, factor);
                            setViewportState(CaptureViewport(currentScrollViewer));
                            args.Handled = true;
                        })
                        .Set(scrollViewer =>
                        {
                            scrollViewerRef.Current = scrollViewer;
                            scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                            scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                            scrollViewer.MinZoomFactor = 0.24f;
                            scrollViewer.MaxZoomFactor = 1.35f;
                        })
                        .Flex(grow: 1),
                    HStack(12,
                        BuildControlPanel(scrollViewerRef.Current, placements, setViewportState, Props.Cards),
                        HStack(0, BuildMiniMapElement(miniMapNodes, viewportWorldRect))
                            .Flex(grow: 1)
                            .HAlign(HorizontalAlignment.Right)))
                .Flex(grow: 1))
            .Background(ReactorMainShellComponent.ResolveBrush("CardBackgroundFillColorSecondaryBrush"))
            .CornerRadius(14)
            .Padding(8)
            .Flex(grow: 1);
    }

    private IEnumerable<Element> BuildCardElements(
        IReadOnlyDictionary<string, BoardCanvasPlacement> placements,
        HashSet<string> highlightedCardIds,
        string? selectedToken,
        string? focusedCardId,
        Action<string?> setSelectedToken,
        Action<string?> setFocusedCardId,
        Action<BoardCanvasViewportState?> setSavedViewportBeforeTokenFocus,
        Action<BoardCanvasViewportState?> setSavedViewportBeforeCardFocus,
        BoardCanvasViewportState? savedViewportBeforeTokenFocus,
        BoardCanvasViewportState? savedViewportBeforeCardFocus,
        BoardCanvasViewportState viewportState,
        Func<ScrollViewer?> getScrollViewer,
        Action<BoardCanvasViewportState> setViewportState)
    {
        foreach (BoardCard card in Props.Cards)
        {
            if (!placements.TryGetValue(card.Id, out BoardCanvasPlacement? placement))
            {
                continue;
            }

            bool isHighlighted = highlightedCardIds.Contains(card.Id);
            bool isFocused = string.Equals(focusedCardId, card.Id, StringComparison.Ordinal);
            bool hasCardFocus = !string.IsNullOrWhiteSpace(focusedCardId);
            bool isDimmed = (selectedToken is not null && !isHighlighted) || (hasCardFocus && !isFocused);

            yield return Border(
                    VStack(8,
                        TextBlock(card.Title)
                            .Bold()
                            .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                        TextBlock(card.ViewKinds.Count > 0 ? $"{card.Id}  •  {string.Join(", ", card.ViewKinds)}" : card.Id)
                            .Opacity(0.72)
                            .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                        TextBlock(string.IsNullOrWhiteSpace(card.Status) ? "Ready" : $"Status: {card.Status}")
                            .Opacity(0.76)
                            .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                        BuildPathStateBadge(card),
                        BuildTokenRows(card, setSelectedToken))
                    .Set(stack => stack.Visibility = Visibility.Visible))
                .Background(BoardTheme.CreateResourceBrush("CardBackgroundFillColorSecondaryBrush", 0x55, Colors.Transparent))
                .WithBorder(ResolveCardBorderBrush(card.Status, isFocused, isHighlighted, selectedToken), ResolveCardBorderThickness(isFocused, isHighlighted, selectedToken))
                .CornerRadius(16)
                .Padding(14)
                .Width(placement.Width)
                .MinHeight(placement.Height)
                .Opacity(isDimmed ? 0.42 : 1)
                .Canvas(placement.X, placement.Y)
                .OnDoubleTapped((_, args) =>
                {
                    ScrollViewer? scrollViewer = getScrollViewer();
                    if (scrollViewer is null)
                    {
                        return;
                    }

                    if (string.Equals(focusedCardId, card.Id, StringComparison.Ordinal))
                    {
                        setFocusedCardId(null);
                        if (savedViewportBeforeCardFocus is not null)
                        {
                            ApplyViewport(scrollViewer, savedViewportBeforeCardFocus, disableAnimation: false);
                            setViewportState(savedViewportBeforeCardFocus);
                            setSavedViewportBeforeCardFocus(null);
                        }
                    }
                    else
                    {
                        if (focusedCardId is null)
                        {
                            setSavedViewportBeforeCardFocus(viewportState);
                        }

                        setFocusedCardId(card.Id);
                        BoardCanvasViewportState nextViewport = FocusPlacement(scrollViewer, placement);
                        setViewportState(nextViewport);
                    }

                    args.Handled = true;
                })
                .WithKey($"board-card-{card.Id}");
        }
    }

    private static Element BuildTokenBanner(
        string? selectedToken,
        Action<string?> setSelectedToken,
        BoardCanvasViewportState? savedViewportBeforeTokenFocus,
        ScrollViewer? scrollViewer,
        Action<BoardCanvasViewportState> setViewportState,
        Action<BoardCanvasViewportState?> setSavedViewportBeforeTokenFocus)
    {
        if (selectedToken is null)
        {
            return TextBlock(string.Empty)
                .Set(text => text.Visibility = Visibility.Collapsed);
        }

        return Border(
                HStack(8,
                    TextBlock("Token focus").Opacity(0.72),
                    Button($"{selectedToken} ×", () =>
                    {
                        setSelectedToken(null);
                        if (scrollViewer is not null && savedViewportBeforeTokenFocus is not null)
                        {
                            ApplyViewport(scrollViewer, savedViewportBeforeTokenFocus, disableAnimation: false);
                            setViewportState(savedViewportBeforeTokenFocus);
                            setSavedViewportBeforeTokenFocus(null);
                        }
                    }).AutomationName($"Clear token focus for {selectedToken}").SubtleButton()))
            .Padding(10, 6, 10, 6)
            .Background(BoardTheme.ResolveBrush("BoardCanvasPanelBrush", Colors.WhiteSmoke))
            .WithBorder(BoardTheme.ResolveBrush("BoardCanvasPanelBorderBrush", Colors.LightGray), 1)
            .CornerRadius(10);
    }

    private static Element BuildControlPanel(
        ScrollViewer? scrollViewer,
        IReadOnlyDictionary<string, BoardCanvasPlacement> placements,
        Action<BoardCanvasViewportState> setViewportState,
        IReadOnlyList<BoardCard> cards)
    {
        return Border(
                HStack(8,
                    Button(Image(HostIconSources.ControlZoomIn).AccessibilityHidden(), () =>
                    {
                        if (scrollViewer is null)
                        {
                            return;
                        }

                        ZoomToPoint(scrollViewer, new Point(scrollViewer.ActualWidth / 2, scrollViewer.ActualHeight / 2), 1.1f);
                        setViewportState(CaptureViewport(scrollViewer));
                    }).AutomationName("Zoom in board").SubtleButton(),
                    Button(Image(HostIconSources.ControlZoomOut).AccessibilityHidden(), () =>
                    {
                        if (scrollViewer is null)
                        {
                            return;
                        }

                        ZoomToPoint(scrollViewer, new Point(scrollViewer.ActualWidth / 2, scrollViewer.ActualHeight / 2), 1f / 1.1f);
                        setViewportState(CaptureViewport(scrollViewer));
                    }).AutomationName("Zoom out board").SubtleButton(),
                    Button(Image(HostIconSources.ControlFitView).AccessibilityHidden(), () =>
                    {
                        if (scrollViewer is null)
                        {
                            return;
                        }

                        BoardCanvasViewportState nextViewport = FitToCards(scrollViewer, placements, cards.Select(card => card.Id));
                        setViewportState(nextViewport);
                        }).AutomationName("Fit board to cards").SubtleButton()))
            .Padding(6)
            .Background(BoardTheme.ResolveBrush("BoardCanvasPanelBrush", Colors.WhiteSmoke))
            .WithBorder(BoardTheme.ResolveBrush("BoardCanvasPanelBorderBrush", Colors.LightGray), 1)
            .CornerRadius(10);
    }

    private static Element BuildMiniMapElement(IReadOnlyList<BoardMiniMapNode> nodes, Rect viewportWorldRect)
    {
        if (!TryBuildMiniMapTransform(nodes, viewportWorldRect, out MiniMapTransform transform))
        {
            return TextBlock(string.Empty)
                .Set(text => text.Visibility = Visibility.Collapsed);
        }

        var children = new List<Element>();
        foreach (BoardMiniMapNode node in nodes)
        {
            double width = Math.Max(4, node.Width * transform.Scale);
            double height = Math.Max(4, node.Height * transform.Scale);
            children.Add(
                Rectangle()
                    .Width(width)
                    .Height(height)
                    .Background(new SolidColorBrush(node.IsRunning
                        ? WithAlpha(BoardTheme.ResolveColor("BoardStatusCompletedColor", Colors.SeaGreen), 235)
                        : WithAlpha(BoardTheme.ResolveColor("BoardStatusFreshColor", Colors.SlateGray), 148)))
                    .WithBorder(new SolidColorBrush(node.IsRunning
                        ? WithAlpha(BoardTheme.ResolveColor("BoardStatusCompletedColor", Colors.SeaGreen), 245)
                        : WithAlpha(BoardTheme.ResolveColor("BoardStatusFreshColor", Colors.SlateGray), 87)), 1)
                    .Set(rectangle =>
                    {
                        rectangle.RadiusX = 2.5;
                        rectangle.RadiusY = 2.5;
                    })
                    .Canvas(transform.OffsetX + ((node.X - transform.WorldBounds.X) * transform.Scale), transform.OffsetY + ((node.Y - transform.WorldBounds.Y) * transform.Scale)));
        }

        double viewportLeft = transform.OffsetX + ((viewportWorldRect.X - transform.WorldBounds.X) * transform.Scale);
        double viewportTop = transform.OffsetY + ((viewportWorldRect.Y - transform.WorldBounds.Y) * transform.Scale);
        double viewportWidth = Math.Max(10, viewportWorldRect.Width * transform.Scale);
        double viewportHeight = Math.Max(10, viewportWorldRect.Height * transform.Scale);

        children.Add(
            Rectangle()
                .Width(viewportWidth)
                .Height(viewportHeight)
                .Background(new SolidColorBrush(WithAlpha(BoardTheme.ResolveColor("BoardColorBorderStrong", Colors.SteelBlue), 18)))
                .WithBorder(new SolidColorBrush(WithAlpha(BoardTheme.ResolveColor("BoardColorBorderStrong", Colors.SteelBlue), 92)), 1)
                .Canvas(viewportLeft, viewportTop));

        return Border(
                Canvas(children.ToArray()) with
                {
                    Width = MiniMapWidth,
                    Height = MiniMapHeight,
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new Point(0.5, 0),
                        EndPoint = new Point(0.5, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new() { Color = WithAlpha(BoardTheme.ResolveColor("BoardColorSurfaceStrong", Colors.WhiteSmoke), 230), Offset = 0 },
                            new() { Color = WithAlpha(BoardTheme.ResolveColor("BoardColorSurfaceMuted", Colors.GhostWhite), 209), Offset = 1 },
                        }
                    }
                })
            .Padding(6)
            .WithBorder(new SolidColorBrush(WithAlpha(BoardTheme.ResolveColor("BoardColorBorderStrong", Colors.SteelBlue), 61)), 1)
            .CornerRadius(8);
    }

    private static IEnumerable<Element> BuildBackgroundDots(double width, double height)
    {
        double gap = BackgroundGridGap;
        while ((width / gap) * (height / gap) > 6000)
        {
            gap *= 2;
        }

        Brush dotBrush = BoardTheme.CreateResourceBrush("BoardColorBorderStrong", 0x2A, Colors.SlateGray);
        const double dotSize = 1.6;
        for (double x = 0; x <= width; x += gap)
        {
            for (double y = 0; y <= height; y += gap)
            {
                yield return Ellipse()
                    .Width(dotSize)
                    .Height(dotSize)
                    .Fill(dotBrush)
                    .Canvas(x - (dotSize / 2), y - (dotSize / 2));
            }
        }
    }

    private static IEnumerable<Element> BuildConnectionElements(
        IReadOnlyList<BoardConnection> connections,
        IReadOnlyDictionary<string, BoardCanvasPlacement> placements,
        HashSet<string> highlightedCardIds,
        string? focusedCardId)
    {
        foreach (BoardConnection connection in connections)
        {
            if (!placements.TryGetValue(connection.SourceCardId, out BoardCanvasPlacement? sourcePlacement)
                || !placements.TryGetValue(connection.TargetCardId, out BoardCanvasPlacement? targetPlacement))
            {
                continue;
            }

            bool sourceHighlighted = highlightedCardIds.Contains(connection.SourceCardId);
            bool targetHighlighted = highlightedCardIds.Contains(connection.TargetCardId);
            bool isHighlighted = connection.TokenFocusMatched;
            bool hasCardFocus = !string.IsNullOrWhiteSpace(focusedCardId);
            bool isDimmed = (!sourceHighlighted && !targetHighlighted)
                || (hasCardFocus && !(string.Equals(focusedCardId, connection.SourceCardId, StringComparison.Ordinal)
                    || string.Equals(focusedCardId, connection.TargetCardId, StringComparison.Ordinal)));

            foreach (Element element in BuildConnectionVisuals(sourcePlacement, targetPlacement, isHighlighted, isDimmed, connection.IsRunning))
            {
                yield return element;
            }

            yield return BuildConnectionLabel(connection.Token, sourcePlacement, targetPlacement, isHighlighted, isDimmed);
        }
    }

    private static IEnumerable<Element> BuildConnectionVisuals(BoardCanvasPlacement source, BoardCanvasPlacement target, bool isHighlighted, bool isDimmed, bool isRunning)
    {
        double startX = source.X + (source.Width / 2);
        double startY = source.Y + source.Height;
        double endX = target.X + (target.Width / 2);
        double endY = target.Y;
        double horizontalDistance = Math.Abs(endX - startX);
        double verticalDistance = Math.Abs(endY - startY);
        double deltaY = horizontalDistance < 180 && verticalDistance < 120
            ? 42
            : horizontalDistance > 520 || verticalDistance > 320
                ? 90
                : Math.Max(60, verticalDistance * 0.38);

        var geometry = new PathGeometry
        {
            Figures =
            {
                new PathFigure
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
                }
            }
        };

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
            yield return Path2D()
                .Set(path => path.Data = geometry)
                .Stroke(new SolidColorBrush(isHighlighted
                    ? Windows.UI.Color.FromArgb(0xCC, accentStrongColor.R, accentStrongColor.G, accentStrongColor.B)
                    : Windows.UI.Color.FromArgb(0xA8, runningColor.R, runningColor.G, runningColor.B)))
                .StrokeThickness(isHighlighted ? 3.1 : 2.3)
                .StrokeDashArray(new[] { 2d, 3d })
                .Opacity(isDimmed ? 0.32 : 0.9);
        }

        yield return Path2D()
            .Set(path => path.Data = geometry)
            .Stroke(new SolidColorBrush(baseColor))
            .StrokeThickness(isHighlighted ? 2.4 : 1.5);

        yield return Ellipse()
            .Width(6)
            .Height(6)
            .Fill(new SolidColorBrush(baseColor))
            .Opacity(isDimmed ? 0.42 : 1)
            .Canvas(startX - 3, startY - 3);

        yield return Ellipse()
            .Width(8)
            .Height(8)
            .Fill(new SolidColorBrush(baseColor))
            .Opacity(isDimmed ? 0.42 : 1)
            .Canvas(endX - 4, endY - 4);
    }

    private static Element BuildConnectionLabel(string token, BoardCanvasPlacement source, BoardCanvasPlacement target, bool isHighlighted, bool isDimmed)
    {
        double centerX = ((source.X + (source.Width / 2)) + (target.X + (target.Width / 2))) / 2;
        double centerY = ((source.Y + source.Height) + target.Y) / 2;
        return Border(TextBlock(token).FontSize(11).Opacity(0.84))
            .Padding(4, 1, 4, 1)
            .Background(isHighlighted
                ? BoardTheme.CreateResourceBrush("BoardColorAccentSoft", 0xF0, Colors.LightBlue)
                : BoardTheme.CreateResourceBrush("BoardColorSurfaceStrong", 0xD8, Colors.White))
            .Opacity(isDimmed ? 0.42 : 1)
            .Canvas(Math.Max(0, centerX - 24), Math.Max(0, centerY - 10));
    }

    private static Element BuildPathStateBadge(BoardCard card)
    {
        if (!card.MetaValues.TryGetValue("path_state", out string? pathState) || string.IsNullOrWhiteSpace(pathState))
        {
            return TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed);
        }

        return Border(TextBlock(pathState.Replace('_', ' ')).Bold())
            .Background(CardToneBrushes.CreateToneBrush(pathState, 0x16))
            .CornerRadius(10)
            .Padding(8);
    }

    private static Element BuildTokenRows(BoardCard card, Action<string?> setSelectedToken)
    {
        var rows = new List<Element>();
        if (card.Requires.Count > 0)
        {
            rows.Add(BuildTokenRow("requires", card.Requires, card, isProvide: false, setSelectedToken));
        }

        if (card.Provides.Count > 0)
        {
            rows.Add(BuildTokenRow("provides", card.Provides, card, isProvide: true, setSelectedToken));
        }

        return VStack(4, rows.ToArray());
    }

    private static Element BuildTokenRow(string label, IReadOnlyList<string> tokens, BoardCard card, bool isProvide, Action<string?> setSelectedToken)
    {
        var tokenButtons = new List<Element>
        {
            TextBlock(label).FontSize(12).Opacity(0.6)
        };

        foreach (string token in tokens)
        {
            bool isActive = isProvide ? App.Current.BoardStore.State.DataObjectsByToken.ContainsKey(token) : true;
            bool isMissing = !isProvide && !App.Current.BoardStore.State.DataObjectsByToken.ContainsKey(token);
            tokenButtons.Add(
                Button(token, () => setSelectedToken(token))
                    .AutomationName($"Focus board token {token}")
                    .Background(isProvide
                        ? BoardTheme.CreateStatusBrush("completed", isActive ? (byte)0x66 : (byte)0x33)
                        : isMissing ? BoardTheme.CreateStatusBrush("failed", 0x44) : BoardTheme.CreateStatusBrush("fresh", 0x33))
                    .WithBorder(isProvide
                        ? BoardTheme.CreateStatusBrush("completed", 0x88)
                        : isMissing ? BoardTheme.CreateStatusBrush("failed", 0x99) : BoardTheme.CreateStatusBrush("fresh", 0x88), 1)
                    .CornerRadius(10)
                    .Padding(8, 2, 8, 2)
                    .MinWidth(0)
                    .Set(button => button.FontSize = 11));
        }

        return HStack(6, tokenButtons.ToArray())
            .HAlign(HorizontalAlignment.Center);
    }

    private static HashSet<string> BuildHighlightedCardIds(IReadOnlyList<BoardCard> cards, string? selectedToken)
    {
        return selectedToken is null
            ? cards.Select(card => card.Id).ToHashSet(StringComparer.Ordinal)
            : cards.Where(card => card.Requires.Contains(selectedToken, StringComparer.Ordinal) || card.Provides.Contains(selectedToken, StringComparer.Ordinal))
                .Select(card => card.Id)
                .ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyList<BoardConnection> BuildConnections(IReadOnlyList<BoardCard> cards)
    {
        var tokenProviders = new Dictionary<string, List<string>>(StringComparer.Ordinal);
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

                foreach (string providerId in providers.Where(providerId => !string.Equals(providerId, card.Id, StringComparison.Ordinal)))
                {
                    connections.Add(new BoardConnection(
                        providerId,
                        card.Id,
                        token,
                        string.Equals(card.Status, "running", StringComparison.OrdinalIgnoreCase),
                        true));
                }
            }
        }

        return connections;
    }

    private static BoardMiniMapNode[] BuildMiniMapNodes(IReadOnlyList<BoardCard> cards, IReadOnlyDictionary<string, BoardCanvasPlacement> placements)
    {
        return cards
            .Where(card => placements.ContainsKey(card.Id))
            .Select(card =>
            {
                BoardCanvasPlacement placement = placements[card.Id];
                return new BoardMiniMapNode(
                    placement.X,
                    placement.Y,
                    placement.Width,
                    placement.Height,
                    string.Equals(card.Status, "running", StringComparison.OrdinalIgnoreCase));
            })
            .ToArray();
    }

    private static Rect ResolveViewportWorldRect(ScrollViewer? scrollViewer, BoardCanvasViewportState viewportState)
    {
        if (scrollViewer is null)
        {
            return new Rect(viewportState.X, viewportState.Y, 1200, 800);
        }

        double zoom = scrollViewer.ZoomFactor > 0 ? scrollViewer.ZoomFactor : Math.Max(0.24, viewportState.Zoom);
        double viewportWidth = scrollViewer.ActualWidth > 0 ? scrollViewer.ActualWidth / zoom : 1200;
        double viewportHeight = scrollViewer.ActualHeight > 0 ? scrollViewer.ActualHeight / zoom : 800;
        return new Rect(
            scrollViewer.HorizontalOffset / zoom,
            scrollViewer.VerticalOffset / zoom,
            Math.Max(MinViewportWidth, viewportWidth),
            Math.Max(MinViewportHeight, viewportHeight));
    }

    private static BoardCanvasViewportState CaptureViewport(ScrollViewer scrollViewer)
    {
        return new BoardCanvasViewportState(scrollViewer.HorizontalOffset, scrollViewer.VerticalOffset, scrollViewer.ZoomFactor > 0 ? scrollViewer.ZoomFactor : 1);
    }

    private static void ApplyViewport(ScrollViewer scrollViewer, BoardCanvasViewportState viewport, bool disableAnimation)
    {
        if (!TryCreateViewport(viewport.X, viewport.Y, viewport.Zoom, out BoardCanvasViewportState safeViewport))
        {
            return;
        }

        scrollViewer.ChangeView(safeViewport.X, safeViewport.Y, (float)safeViewport.Zoom, disableAnimation);
    }

    private static void ZoomToPoint(ScrollViewer scrollViewer, Point viewportPoint, float multiplier)
    {
        float currentZoom = scrollViewer.ZoomFactor > 0 ? scrollViewer.ZoomFactor : 1;
        float targetZoom = (float)Math.Max(0.24, Math.Min(1.35, currentZoom * multiplier));
        if (Math.Abs(targetZoom - currentZoom) < 0.0005)
        {
            return;
        }

        double contentX = (scrollViewer.HorizontalOffset + viewportPoint.X) / currentZoom;
        double contentY = (scrollViewer.VerticalOffset + viewportPoint.Y) / currentZoom;
        double targetHorizontal = Math.Max(0, (contentX * targetZoom) - viewportPoint.X);
        double targetVertical = Math.Max(0, (contentY * targetZoom) - viewportPoint.Y);
        if (!TryCreateViewport(targetHorizontal, targetVertical, targetZoom, out BoardCanvasViewportState safeViewport))
        {
            return;
        }

        scrollViewer.ChangeView(safeViewport.X, safeViewport.Y, (float)safeViewport.Zoom, true);
    }

    private static BoardCanvasViewportState FitToCards(ScrollViewer scrollViewer, IReadOnlyDictionary<string, BoardCanvasPlacement> placements, IEnumerable<string> cardIds)
    {
        BoardCanvasPlacement[] nodes = cardIds
            .Where(id => placements.ContainsKey(id))
            .Select(id => placements[id])
            .ToArray();
        if (nodes.Length == 0)
        {
            return CaptureViewport(scrollViewer);
        }

        double minLeft = nodes.Min(host => host.X);
        double minTop = nodes.Min(host => host.Y);
        double maxRight = nodes.Max(host => host.X + host.Width);
        double maxBottom = nodes.Max(host => host.Y + host.Height);
        double boundsWidth = Math.Max(1, maxRight - minLeft);
        double boundsHeight = Math.Max(1, maxBottom - minTop);
        double viewportWidth = scrollViewer.ActualWidth > 0 ? scrollViewer.ActualWidth : 1200;
        double viewportHeight = scrollViewer.ActualHeight > 0 ? scrollViewer.ActualHeight : 800;
        double targetZoom = Math.Min((viewportWidth * 0.7) / boundsWidth, (viewportHeight * 0.7) / boundsHeight);
        targetZoom = Math.Max(0.5, Math.Min(1.08, targetZoom));
        double centerX = minLeft + (boundsWidth / 2);
        double centerY = minTop + (boundsHeight / 2);
        double targetHorizontal = Math.Max(0, (centerX * targetZoom) - (viewportWidth / 2));
        double targetVertical = Math.Max(0, (centerY * targetZoom) - (viewportHeight / 2));
        if (!TryCreateViewport(targetHorizontal, targetVertical, targetZoom, out BoardCanvasViewportState targetViewport))
        {
            return CaptureViewport(scrollViewer);
        }

        scrollViewer.ChangeView(targetViewport.X, targetViewport.Y, (float)targetViewport.Zoom, false);
        return targetViewport;
    }

    private static BoardCanvasViewportState FocusPlacement(ScrollViewer scrollViewer, BoardCanvasPlacement placement)
    {
        double viewportWidth = scrollViewer.ActualWidth > 0 ? scrollViewer.ActualWidth : 1200;
        double viewportHeight = scrollViewer.ActualHeight > 0 ? scrollViewer.ActualHeight : 800;
        double zoomForHeight = (viewportHeight * 0.9) / placement.Height;
        double zoomForWidth = (viewportWidth * 0.95) / placement.Width;
        double targetZoom = Math.Min(zoomForHeight, zoomForWidth);
        targetZoom = Math.Max(0.35, Math.Min(1.35, targetZoom));
        double centerX = placement.X + (placement.Width / 2);
        double centerY = placement.Y + (placement.Height / 2);
        double targetHorizontal = Math.Max(0, (centerX * targetZoom) - (viewportWidth / 2));
        double targetVertical = Math.Max(0, (centerY * targetZoom) - (viewportHeight / 2));
        if (!TryCreateViewport(targetHorizontal, targetVertical, targetZoom, out BoardCanvasViewportState targetViewport))
        {
            return CaptureViewport(scrollViewer);
        }

        scrollViewer.ChangeView(targetViewport.X, targetViewport.Y, (float)targetViewport.Zoom, false);
        return targetViewport;
    }

    private static double ResolveCardBorderThickness(bool isFocused, bool isHighlighted, string? selectedToken)
    {
        return isFocused ? 2.5 : isHighlighted && selectedToken is not null ? 2 : 0;
    }

    private static Brush ResolveCardBorderBrush(string status, bool isFocused, bool isHighlighted, string? selectedToken)
    {
        if (isFocused)
        {
            return BoardTheme.CreateResourceBrush("BoardColorAccentStrong", 0xCC, Colors.CornflowerBlue);
        }

        if (isHighlighted && selectedToken is not null)
        {
            return BoardTheme.CreateResourceBrush("BoardColorAccent", 0xAA, Colors.SteelBlue);
        }

        return new SolidColorBrush(Colors.Transparent);
    }

    private static async void PersistLayoutAsync(string boardId)
    {
        if (string.IsNullOrWhiteSpace(boardId))
        {
            return;
        }

        try
        {
            await App.Current.BoardClient.SaveLayoutAsync(boardId, App.Current.BoardStore.GetCanvasLayout());
        }
        catch
        {
        }
    }

    private static bool TryBuildMiniMapTransform(IReadOnlyList<BoardMiniMapNode> nodes, Rect viewportWorldRect, out MiniMapTransform transform)
    {
        transform = default;
        if (nodes.Count == 0)
        {
            return false;
        }

        double minLeft = nodes.Min(node => node.X);
        double minTop = nodes.Min(node => node.Y);
        double maxRight = nodes.Max(node => node.X + node.Width);
        double maxBottom = nodes.Max(node => node.Y + node.Height);
        Rect nodeBounds = new(minLeft, minTop, Math.Max(1, maxRight - minLeft), Math.Max(1, maxBottom - minTop));
        Rect union = UnionRects(nodeBounds, viewportWorldRect);
        Rect padded = new(
            union.X - MiniMapPadding,
            union.Y - MiniMapPadding,
            union.Width + (MiniMapPadding * 2),
            union.Height + (MiniMapPadding * 2));
        double scale = Math.Min(MiniMapWidth / padded.Width, MiniMapHeight / padded.Height);
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            return false;
        }

        double offsetX = (MiniMapWidth - (padded.Width * scale)) / 2;
        double offsetY = (MiniMapHeight - (padded.Height * scale)) / 2;
        transform = new MiniMapTransform(padded, scale, offsetX, offsetY);
        return true;
    }

    private static bool TryCreateViewport(double x, double y, double zoom, out BoardCanvasViewportState viewport)
    {
        viewport = default;
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(zoom) || zoom <= 0)
        {
            return false;
        }

        viewport = new BoardCanvasViewportState(x, y, zoom);
        return true;
    }

    private static Rect UnionRects(Rect a, Rect b)
    {
        double left = Math.Min(a.Left, b.Left);
        double top = Math.Min(a.Top, b.Top);
        double right = Math.Max(a.Right, b.Right);
        double bottom = Math.Max(a.Bottom, b.Bottom);
        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static Windows.UI.Color WithAlpha(Windows.UI.Color color, byte alpha)
    {
        return Windows.UI.Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private readonly record struct MiniMapTransform(Rect WorldBounds, double Scale, double OffsetX, double OffsetY);

    private sealed record BoardConnection(string SourceCardId, string TargetCardId, string Token, bool IsRunning, bool TokenFocusMatched);
}