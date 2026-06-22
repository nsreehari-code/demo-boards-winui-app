using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemoBoards_WinUI.Controls;

public sealed class TimerButton : UserControl
{
    private const string DefaultRefreshGlyph = "\uE72C";
    private readonly Button RootButton;
    private readonly ProgressRing BusyRing;
    private readonly FontIcon RefreshIcon;
    private readonly TextBlock LabelText;
    private string label = "Refresh";
    private string timeText = string.Empty;
    private bool isBusy;
    private bool isActionEnabled = true;
    private string toolTipText = "Refresh all cards";
    private string iconGlyph = DefaultRefreshGlyph;
    private Style? buttonStyle;

    public TimerButton()
    {
        BusyRing = new ProgressRing { Width = 14, Height = 14, IsActive = false, Visibility = Visibility.Collapsed };
        RefreshIcon = new FontIcon { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        LabelText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
        RootButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { BusyRing, RefreshIcon, LabelText }
            }
        };
        RootButton.Click += OnButtonClick;
        Content = RootButton;
        UpdateVisualState();
    }

    public event RoutedEventHandler? Click;

    public string Label
    {
        get => label;
        set
        {
            label = value;
            UpdateVisualState();
        }
    }

    public string TimeText
    {
        get => timeText;
        set
        {
            timeText = value;
            UpdateVisualState();
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        set
        {
            isBusy = value;
            UpdateVisualState();
        }
    }

    public bool IsActionEnabled
    {
        get => isActionEnabled;
        set
        {
            isActionEnabled = value;
            UpdateVisualState();
        }
    }

    public string ToolTipText
    {
        get => toolTipText;
        set
        {
            toolTipText = value;
            UpdateVisualState();
        }
    }

    public string IconGlyph
    {
        get => iconGlyph;
        set
        {
            iconGlyph = value;
            UpdateVisualState();
        }
    }

    public Style? ButtonStyle
    {
        get => buttonStyle;
        set
        {
            buttonStyle = value;
            UpdateVisualState();
        }
    }

    private void UpdateVisualState()
    {
        bool busy = IsBusy;
        RootButton.Style = ResolveButtonStyle();
        RootButton.IsEnabled = IsActionEnabled && !busy;
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        RefreshIcon.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        RefreshIcon.Glyph = string.IsNullOrWhiteSpace(IconGlyph) ? DefaultRefreshGlyph : IconGlyph;
        LabelText.Text = ComposeLabelText();
        ToolTipService.SetToolTip(RootButton, string.IsNullOrWhiteSpace(ToolTipText) ? null : ToolTipText);
    }

    private Style? ResolveButtonStyle()
    {
        if (ButtonStyle is not null)
        {
            return ButtonStyle;
        }

        return Application.Current.Resources.TryGetValue("BoardToolbarButtonStyle", out object resource)
            ? resource as Style
            : null;
    }

    private string ComposeLabelText()
    {
        string label = Label?.Trim() ?? string.Empty;
        string timeText = TimeText?.Trim() ?? string.Empty;

        if (label.Length == 0)
        {
            return timeText;
        }

        if (timeText.Length == 0)
        {
            return label;
        }

        return $"{label} {timeText}";
    }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        Click?.Invoke(this, e);
    }
}