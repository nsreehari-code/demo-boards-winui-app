using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls;

public sealed class ReactorSmokeRunnerComponent : Component
{
    public override Element Render()
    {
        var (running, setRunning) = UseState(false);
        var (statusText, setStatusText) = UseState("Run the embedded GoldenHarness smoke suite from inside the WinUI app.");
        var (outputText, setOutputText) = UseState(string.Empty);

        return Border(
                VStack(10,
                    TextBlock("Smoke Runner").FontSize(20).Bold(),
                    TextBlock(statusText)
                        .Opacity(0.72)
                        .Set(text => text.TextWrapping = TextWrapping.WrapWholeWords),
                    HStack(8,
                        Button(running ? "Running..." : "Run Tests", () =>
                        {
                            if (running)
                            {
                                return;
                            }

                            _ = RunSmokeAsync(setRunning, setStatusText, setOutputText);
                        })
                            .AutomationName("Run smoke tests")
                            .AccentButton()),
                    TextBox(outputText)
                        .AutomationName("Smoke runner output")
                        .IsReadOnly(true)
                        .AcceptsReturn(true)
                        .TextWrapping(TextWrapping.Wrap)
                        .PlaceholderText("Smoke output will appear here.")
                        .Set(textBox =>
                        {
                            textBox.MinWidth = 760;
                            textBox.MinHeight = 520;
                            textBox.VerticalAlignment = VerticalAlignment.Stretch;
                            textBox.FontFamily = new FontFamily("Consolas");
                        })))
            .Padding(8);
    }

    private static async Task RunSmokeAsync(Action<bool> setRunning, Action<string> setStatusText, Action<string> setOutputText)
    {
        setRunning(true);
        setStatusText("Running GoldenHarness smoke suite...");
        setOutputText(string.Empty);

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

            setOutputText(output);
            setStatusText(exitCode == 0
                ? "Smoke suite passed."
                : $"Smoke suite failed with exit code {exitCode}.");
        }
        catch (Exception ex)
        {
            setStatusText(ex.Message);
            setOutputText(ex.ToString());
        }
        finally
        {
            setRunning(false);
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