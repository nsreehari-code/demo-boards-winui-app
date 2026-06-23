using System;
using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls;

public sealed record ReactorChallengeConfirmModalProps(string Message, Action ConfirmAction, Action CancelAction, bool Pending = false);

public sealed class ReactorChallengeConfirmModalComponent : Component<ReactorChallengeConfirmModalProps>
{
    public override Element Render()
    {
        var (lhs, _) = UseState(Random.Shared.Next(0, 8));
        var (rhs, _) = UseState(Random.Shared.Next(4, 10));
        var (answerText, setAnswerText) = UseState(string.Empty);

        int expected = lhs + rhs;
        bool answered = !string.IsNullOrWhiteSpace(answerText);
        bool isCorrect = int.TryParse(answerText.Trim(), out int parsed) && parsed == expected;

        return Border(
                VStack(12,
                    TextBlock("Confirm action").FontSize(18).Bold(),
                    TextBlock(Props.Message)
                        .Opacity(0.8)
                        .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                    TextBlock($"Solve to confirm: {lhs} + {rhs} = ?")
                        .Opacity(0.74)
                        .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                    TextBox(answerText, setAnswerText)
                        .AutomationName("Confirmation challenge answer")
                        .Set(textBox =>
                        {
                            textBox.PlaceholderText = "Enter the sum";
                        }),
                    answered && !isCorrect
                        ? (Element)TextBlock("Incorrect - try again.")
                            .Foreground(new SolidColorBrush(Colors.IndianRed))
                        : TextBlock(string.Empty).Set(text => text.Visibility = Visibility.Collapsed),
                    HStack(8,
                        Button("Cancel", Props.CancelAction)
                            .AutomationName("Cancel confirmation")
                            .SubtleButton(),
                        Button(Props.Pending ? "Working..." : "Confirm", Props.ConfirmAction)
                            .AutomationName("Confirm destructive action")
                            .AccentButton()
                            .Set(button => button.IsEnabled = isCorrect && !Props.Pending))))
            .Padding(14)
            .Background(BoardTheme.ResolveBrush("CardBackgroundFillColorDefaultBrush", Colors.White))
            .WithBorder(BoardTheme.ResolveBrush("BoardBorderStrongBrush", Colors.LightGray), 1)
            .CornerRadius(14);
    }
}