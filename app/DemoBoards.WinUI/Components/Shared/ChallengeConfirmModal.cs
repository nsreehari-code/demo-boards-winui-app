using System;
using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using DemoBoards_WinUI;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Mirrors <c>ChallengeConfirmModal.jsx</c> — a confirmation overlay that requires solving a small
/// arithmetic challenge before the destructive action proceeds.
/// </summary>
public sealed record ChallengeConfirmModalProps(string Message, Action OnConfirm, Action OnCancel, bool Pending = false);

public sealed class ChallengeConfirmModal : Component<ChallengeConfirmModalProps>
{
    public override Element Render()
    {
        AppTheme theme = UseContext(AppThemeContext.Current);
        var (lhs, _) = UseState(Random.Shared.Next(0, 8));
        var (rhs, _) = UseState(Random.Shared.Next(4, 10));
        var (answer, setAnswer) = UseState(string.Empty);

        int expected = lhs + rhs;
        bool answered = !string.IsNullOrWhiteSpace(answer);
        bool isCorrect = int.TryParse(answer.Trim(), out int parsed) && parsed == expected;

        Element errorLine = answered && !isCorrect
            ? (Element)TextBlock("Incorrect - try again.").Foreground(new SolidColorBrush(Colors.IndianRed))
            : TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed);

        return Border(VStack(12,
                TextBlock("Confirm action").FontSize(18).Bold().Foreground(theme.TextPrimary),
                TextBlock(Props.Message).Foreground(theme.TextPrimary).Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                TextBlock($"Solve to confirm: {lhs} + {rhs} = ?").Opacity(0.74).Foreground(theme.TextPrimary),
                TextBox(answer, setAnswer).AutomationName("Confirmation challenge answer").PlaceholderText("Enter the sum"),
                errorLine,
                HStack(8,
                    Button("Cancel", Props.OnCancel).SubtleButton().AutomationName("Cancel confirmation"),
                    Button(Props.Pending ? "Working..." : "Confirm", Props.OnConfirm).AccentButton().AutomationName("Confirm destructive action")
                        .Set(button => button.IsEnabled = isCorrect && !Props.Pending))))
            .Padding(14)
            .Background(theme.CardBackground)
            .WithBorder(theme.CardBorder, 1)
            .CornerRadius(14);
    }
}
