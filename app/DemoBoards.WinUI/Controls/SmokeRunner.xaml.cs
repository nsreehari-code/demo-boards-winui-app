using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemoBoards_WinUI.Controls;

public sealed class SmokeRunner : UserControl
{
    private readonly TextBlock StatusText;
    private readonly Button RunButton;
    private readonly TextBox OutputTextBox;
    private bool running;

    public SmokeRunner()
    {
        StatusText = new TextBlock
        {
            Opacity = 0.72,
            Text = "Run the embedded GoldenHarness smoke suite from inside the WinUI app.",
            TextWrapping = TextWrapping.WrapWholeWords,
        };
        RunButton = new Button { Content = "Run Tests" };
        RunButton.Click += OnRunClick;
        OutputTextBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Stretch,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            PlaceholderText = "Smoke output will appear here.",
        };
        var root = new Grid { RowSpacing = 10, MinWidth = 760, MinHeight = 520 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Smoke Runner", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                StatusText,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { RunButton } }
            }
        });
        Grid.SetRow(OutputTextBox, 1);
        root.Children.Add(OutputTextBox);
        Content = root;
    }

    private async void OnRunClick(object sender, RoutedEventArgs e)
    {
        if (running)
        {
            return;
        }

        RunButton.IsEnabled = false;
        running = true;
        StatusText.Text = "Running GoldenHarness smoke suite...";
        OutputTextBox.Text = string.Empty;

        try
        {
            string projectPath = ResolveGoldenHarnessProjectPath();
            string workingDirectory = Path.GetDirectoryName(projectPath) is string projectDirectory
                ? Directory.GetParent(projectDirectory)?.FullName ?? projectDirectory
                : AppContext.BaseDirectory;

            (int exitCode, string output) = await RunProcessAsync(
                "dotnet",
                $"run --project \"{projectPath}\"",
                workingDirectory);

            OutputTextBox.Text = output;
            StatusText.Text = exitCode == 0
                ? "Smoke suite passed."
                : $"Smoke suite failed with exit code {exitCode}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            OutputTextBox.Text = ex.ToString();
        }
        finally
        {
            running = false;
            RunButton.IsEnabled = true;
        }
    }

    private static string ResolveGoldenHarnessProjectPath()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "engine", "GoldenHarness", "GoldenHarness.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate engine/GoldenHarness/GoldenHarness.csproj from the WinUI app runtime path.");
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(string fileName, string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var outputBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                outputBuilder.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                outputBuilder.AppendLine(e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {fileName} {arguments}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        return (process.ExitCode, outputBuilder.ToString().Trim());
    }
}