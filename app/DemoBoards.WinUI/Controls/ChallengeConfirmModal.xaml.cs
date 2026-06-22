using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace DemoBoards_WinUI.Controls;

public sealed class ChallengeConfirmModal : UserControl
{
    private readonly TextBlock PromptText;
    private readonly TextBlock ChallengeText;
    private readonly TextBox AnswerTextBox;
    private readonly TextBlock ValidationText;
    private readonly Button CancelButton;
    private readonly Button ConfirmButton;

    private int expectedAnswer;

    public ChallengeConfirmModal()
    {
        PromptText = new TextBlock { TextWrapping = TextWrapping.WrapWholeWords };
        ChallengeText = new TextBlock { Opacity = 0.78, TextWrapping = TextWrapping.WrapWholeWords };
        AnswerTextBox = new TextBox { PlaceholderText = "Enter the sum...", InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } } };
        AnswerTextBox.TextChanged += OnAnswerTextChanged;
        AnswerTextBox.KeyDown += OnAnswerTextBoxKeyDown;
        ValidationText = new TextBlock
        {
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed),
            Opacity = 0,
            Text = "Incorrect - try again.",
            TextWrapping = TextWrapping.WrapWholeWords,
        };
        CancelButton = new Button { Content = "Cancel" };
        CancelButton.Click += OnCancelClick;
        ConfirmButton = new Button { Content = "Confirm", IsEnabled = false };
        ConfirmButton.Click += OnConfirmClick;

        Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                PromptText,
                ChallengeText,
                new StackPanel
                {
                    Spacing = 6,
                    Children = { AnswerTextBox, ValidationText }
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { CancelButton, ConfirmButton }
                }
            }
        };
        Loaded += OnLoaded;
    }

    public event EventHandler? Confirmed;
    public event EventHandler? Cancelled;

    public void Render(string prompt)
    {
        PromptText.Text = prompt;
        int left = Random.Shared.Next(0, 8);
        int right = Random.Shared.Next(4, 10);
        expectedAnswer = left + right;
        ChallengeText.Text = $"Solve to confirm: {left} + {right} = ?";
        AnswerTextBox.Text = string.Empty;
        ValidationText.Opacity = 0;
        ConfirmButton.IsEnabled = false;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _ = AnswerTextBox.Focus(FocusState.Programmatic);
    }

    private void OnAnswerTextChanged(object sender, TextChangedEventArgs e)
    {
        string raw = AnswerTextBox.Text?.Trim() ?? string.Empty;
        bool answered = raw.Length > 0;
        bool isCorrect = answered && int.TryParse(raw, out int parsed) && parsed == expectedAnswer;
        ConfirmButton.IsEnabled = isCorrect;
        ValidationText.Opacity = answered && !isCorrect ? 1 : 0;
    }

    private void OnAnswerTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            Cancelled?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (e.Key == Windows.System.VirtualKey.Enter && ConfirmButton.IsEnabled)
        {
            e.Handled = true;
            Confirmed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (ConfirmButton.IsEnabled)
        {
            Confirmed?.Invoke(this, EventArgs.Empty);
        }
    }
}
