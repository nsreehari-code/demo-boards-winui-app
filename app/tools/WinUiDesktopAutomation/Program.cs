using System.Diagnostics;
using System.Globalization;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace WinUiDesktopAutomation;

internal static class Program
{
    private const string ExecutedSmokeSubset = "MB1, T3u";
    private const string DefaultServerUrl = "http://localhost:7799";
    private const string AlternateServerUrl = "http://127.0.0.1:7799";
    private const string InvalidServerUrl = "http://127.0.0.1:1";

    private static readonly string[] RunnableSmokeCaseIds =
    {
        "MB1",
        "MB2",
        "T0",
        "T1",
        "T2",
        "T3",
        "T3u",
        "T4",
        "T8",
        "T8F",
        "TR",
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            string repoRoot = FindRepoRoot();

            if (!options.ReuseRunning)
            {
                await RunPowerShellAsync(Path.Combine(repoRoot, "scripts", "stop-winui.ps1"), repoRoot, ignoreExitCode: true).ConfigureAwait(false);
                await RunPowerShellAsync(Path.Combine(repoRoot, "scripts", "start-winui.ps1"), repoRoot).ConfigureAwait(false);
            }

            Process process = WaitForAppProcess(options.Timeout);
            int originalProcessId = process.Id;

            using var automation = new UIA3Automation();
            using var application = Application.Attach(process);
            Window mainWindow = Retry.WhileNull(
                () => application.GetMainWindow(automation),
                options.Timeout,
                TimeSpan.FromMilliseconds(250),
                ignoreException: true).Result
                ?? throw new InvalidOperationException("Failed to locate the DemoBoards.WinUI main window.");

            mainWindow.Focus();
            if (options.VerifyServerSwitch)
            {
                VerifyServerSwitchUi(mainWindow, originalProcessId, options.Timeout);
            }
            else
            {
                VerifySmokeRunnerUi(mainWindow, options.Timeout, options.RunSuite);
            }

            if (!options.VerifyServerSwitch && options.RunSuite && options.HoldOpenDuration > TimeSpan.Zero)
            {
                Console.WriteLine($"[ui-smoke] Holding Smoke Runner open for {options.HoldOpenDuration.TotalSeconds:0} seconds.");
                await Task.Delay(options.HoldOpenDuration).ConfigureAwait(false);
            }

            Console.WriteLine(options.VerifyServerSwitch
                ? "[ui-smoke] PASS - Server switch updated healthz state, reloaded boards, and rebound the live board session without restart."
                : options.RunSuite
                    ? "[ui-smoke] PASS - Smoke Runner modal opened, toggle UI synchronized, and MB1/T3u passed."
                    : "[ui-smoke] PASS - Smoke Runner modal opened and toggle UI responded as expected.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[ui-smoke] FAIL - {exception.Message}");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void VerifySmokeRunnerUi(Window mainWindow, TimeSpan timeout, bool runSuite)
    {
        Button settingsButton = FindButtonByAnyName(mainWindow, timeout, "Show board settings", "Hide board settings");
        settingsButton.Invoke();

        Button launchSmokeRunnerButton = FindButtonByName(mainWindow, "Run smoke tests", timeout);
        launchSmokeRunnerButton.Invoke();

        TextBox runTestsTextBox = FindTextBoxByName(mainWindow, "Run Tests", timeout);

        foreach (string caseId in RunnableSmokeCaseIds)
        {
            _ = FindButtonByName(mainWindow, $"Toggle smoke case {caseId}", timeout);
        }

        if (!string.IsNullOrWhiteSpace(ReadText(runTestsTextBox)))
        {
            throw new InvalidOperationException("Expected the Run Tests textbox to start empty when all runnable cases are selected.");
        }

        Button mb1Toggle = FindButtonByName(mainWindow, "Toggle smoke case MB1", timeout);
        mb1Toggle.Invoke();

        Retry.WhileFalse(
            () =>
            {
                string value = ReadText(runTestsTextBox);
                return value.Length > 0 && !value.Contains("MB1", StringComparison.Ordinal);
            },
            timeout,
            TimeSpan.FromMilliseconds(150),
            throwOnTimeout: true,
            timeoutMessage: "Expected clicking the MB1 toggle to populate the Run Tests textbox without MB1.");

        if (!runSuite)
        {
            return;
        }

        SetText(runTestsTextBox, ExecutedSmokeSubset);
        Retry.WhileFalse(
            () => string.Equals(ReadText(runTestsTextBox), ExecutedSmokeSubset, StringComparison.Ordinal),
            timeout,
            TimeSpan.FromMilliseconds(150),
            throwOnTimeout: true,
            timeoutMessage: "Expected the Run Tests textbox to accept the MB1/T3u subset.");

        Button runButton = FindButtonByName(mainWindow, "Run smoke tests", timeout);
        runButton.Invoke();

        _ = FindTextByName(mainWindow, "Selected frontend smoke cases passed.", timeout);
    }

    private static void VerifyServerSwitchUi(Window mainWindow, int originalProcessId, TimeSpan timeout)
    {
        Button settingsButton = FindButtonByAnyName(mainWindow, timeout, "Show board settings", "Hide board settings");
        settingsButton.Invoke();

        TextBox serverTextBox = FindTextBoxByName(mainWindow, "Server URL", timeout);
        ComboBox boardComboBox = FindBoardSelector(mainWindow, timeout);

        string originalServerUrl = NormalizeServerUrl(ReadText(serverTextBox));
        if (string.IsNullOrWhiteSpace(originalServerUrl))
        {
            originalServerUrl = DefaultServerUrl;
        }

        string validAlternateServerUrl = string.Equals(originalServerUrl, NormalizeServerUrl(DefaultServerUrl), StringComparison.OrdinalIgnoreCase)
            ? AlternateServerUrl
            : DefaultServerUrl;

        Retry.WhileFalse(
            () => IsBoardSelectorPopulated(boardComboBox),
            timeout,
            TimeSpan.FromMilliseconds(200),
            throwOnTimeout: true,
            timeoutMessage: "Expected the board selector to be populated before switching server origins.");

        SetText(serverTextBox, InvalidServerUrl);

        Retry.WhileNull(
            () => FindDescendantByNameContaining(mainWindow, "Server health check failed"),
            timeout,
            TimeSpan.FromMilliseconds(200),
            ignoreException: true,
            throwOnTimeout: true,
            timeoutMessage: "Expected an unreachable server URL to surface a health check failure.");

        Retry.WhileFalse(
            () => string.Equals(ReadComboBoxText(boardComboBox), "No boards available", StringComparison.Ordinal),
            timeout,
            TimeSpan.FromMilliseconds(200),
            throwOnTimeout: true,
            timeoutMessage: "Expected the board selector to empty after switching to an unreachable server.");

        SetText(serverTextBox, validAlternateServerUrl);

        _ = FindElementByExactName(mainWindow, "Server reachable", timeout);

        Retry.WhileFalse(
            () =>
            {
                string comboText = ReadComboBoxText(boardComboBox);
                return !string.IsNullOrWhiteSpace(comboText)
                    && !string.Equals(comboText, "No boards available", StringComparison.Ordinal)
                    && !string.Equals(comboText, "Loading boards…", StringComparison.Ordinal);
            },
            timeout,
            TimeSpan.FromMilliseconds(200),
            throwOnTimeout: true,
            timeoutMessage: "Expected the board selector to repopulate after switching back to a reachable server.");

        Retry.WhileFalse(
            () => Process.GetProcessesByName("DemoBoards.WinUI").Any(candidate => candidate.Id == originalProcessId),
            timeout,
            TimeSpan.FromMilliseconds(200),
            throwOnTimeout: true,
            timeoutMessage: "Expected the existing DemoBoards.WinUI process to stay alive during server rebinding.");

        string reboundLine = Retry.WhileNull(
            () => ReadBoardSessionReboundLine(NormalizeServerUrl(validAlternateServerUrl)),
            timeout,
            TimeSpan.FromMilliseconds(250),
            ignoreException: true,
            throwOnTimeout: true,
            timeoutMessage: "Expected the startup log to record a board session rebind for the new server origin.").Result
            ?? throw new InvalidOperationException("Expected the startup log to record a board session rebind.");

        Console.WriteLine($"[ui-smoke] Observed session rebind: {reboundLine}");

        SetText(serverTextBox, originalServerUrl);
        _ = FindElementByExactName(mainWindow, "Server reachable", timeout);
    }

    private static void SetText(TextBox textBox, string value)
    {
        if (textBox.Patterns.Value.IsSupported)
        {
            textBox.Patterns.Value.Pattern.SetValue(value);
            return;
        }

        textBox.Focus();
        Keyboard.TypeSimultaneously(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL, FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_A);
        Keyboard.Type(value);
    }

    private static string ReadText(TextBox textBox)
    {
        try
        {
            return textBox.Text ?? string.Empty;
        }
        catch
        {
            return textBox.Patterns.Value.PatternOrDefault?.Value.Value ?? string.Empty;
        }
    }

    private static Process WaitForAppProcess(TimeSpan timeout)
    {
        Process? process = Retry.WhileNull(
            () => Process.GetProcessesByName("DemoBoards.WinUI").OrderByDescending(candidate => candidate.StartTime).FirstOrDefault(),
            timeout,
            TimeSpan.FromMilliseconds(250),
            ignoreException: true).Result;

        return process ?? throw new InvalidOperationException("DemoBoards.WinUI process did not appear in time.");
    }

    private static Button FindButtonByAnyName(AutomationElement scope, TimeSpan timeout, params string[] names)
    {
        return Retry.WhileNull(
            () => names.Select(name => scope.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(name))).AsButton()).FirstOrDefault(button => button is not null),
            timeout,
            TimeSpan.FromMilliseconds(150),
            ignoreException: true).Result
            ?? throw new InvalidOperationException($"Failed to locate any of these buttons: {string.Join(", ", names)}.");
    }

    private static Button FindButtonByName(AutomationElement scope, string name, TimeSpan timeout)
    {
        return Retry.WhileNull(
            () => scope.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(name))).AsButton(),
            timeout,
            TimeSpan.FromMilliseconds(150),
            ignoreException: true).Result
            ?? throw new InvalidOperationException($"Failed to locate button '{name}'.");
    }

    private static TextBox FindTextBoxByName(AutomationElement scope, string name, TimeSpan timeout)
    {
        return Retry.WhileNull(
            () => scope.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit).And(cf.ByName(name))).AsTextBox(),
            timeout,
            TimeSpan.FromMilliseconds(150),
            ignoreException: true).Result
            ?? throw new InvalidOperationException($"Failed to locate textbox '{name}'.");
    }

    private static AutomationElement FindTextByName(AutomationElement scope, string name, TimeSpan timeout)
    {
        return Retry.WhileNull(
            () => scope.FindFirstDescendant(cf => cf.ByControlType(ControlType.Text).And(cf.ByName(name))),
            timeout,
            TimeSpan.FromMilliseconds(150),
            ignoreException: true).Result
            ?? throw new InvalidOperationException($"Failed to locate text '{name}'.");
    }

    private static ComboBox FindBoardSelector(AutomationElement scope, TimeSpan timeout)
    {
        return Retry.WhileNull(
            () => scope.FindAllDescendants(cf => cf.ByControlType(ControlType.ComboBox))
                .Select(element => element.AsComboBox())
                .FirstOrDefault(comboBox => comboBox is not null),
            timeout,
            TimeSpan.FromMilliseconds(150),
            ignoreException: true).Result
            ?? throw new InvalidOperationException("Failed to locate the board selector combo box.");
    }

    private static AutomationElement FindElementByExactName(AutomationElement scope, string name, TimeSpan timeout)
    {
        return Retry.WhileNull(
            () => scope.FindFirstDescendant(cf => cf.ByName(name)),
            timeout,
            TimeSpan.FromMilliseconds(150),
            ignoreException: true).Result
            ?? throw new InvalidOperationException($"Failed to locate element '{name}'.");
    }

    private static AutomationElement? FindDescendantByNameContaining(AutomationElement scope, string fragment)
    {
        return scope.FindAllDescendants()
            .FirstOrDefault(element => GetElementNameSafe(element).Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetElementNameSafe(AutomationElement element)
    {
        try
        {
            return element.Name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadComboBoxText(ComboBox comboBox)
    {
        try
        {
            string? value = comboBox.Patterns.Value.PatternOrDefault?.Value.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        catch
        {
        }

        try
        {
            string? selectedText = comboBox.SelectedItem?.Text;
            if (!string.IsNullOrWhiteSpace(selectedText))
            {
                return selectedText;
            }
        }
        catch
        {
        }

        try
        {
            AutomationElement? textChild = comboBox.FindFirstDescendant(cf => cf.ByControlType(ControlType.Text));
            if (textChild is not null)
            {
                string childName = textChild.Name ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(childName))
                {
                    return childName;
                }
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static bool IsBoardSelectorPopulated(ComboBox comboBox)
    {
        string comboText = ReadComboBoxText(comboBox);
        if (!string.IsNullOrWhiteSpace(comboText)
            && !string.Equals(comboText, "No boards available", StringComparison.Ordinal)
            && !string.Equals(comboText, "Loading boards…", StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            comboBox.Expand();
            try
            {
                IReadOnlyList<ComboBoxItem> items = comboBox.Items;
                return items.Any(item => !string.IsNullOrWhiteSpace(item.Text));
            }
            finally
            {
                comboBox.Collapse();
            }
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeServerUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : value.Trim();
    }

    private static string? ReadBoardSessionReboundLine(string expectedServerUrl)
    {
        string startupLogPath = Path.Combine(Path.GetTempPath(), "DemoBoards.WinUI.startup.log");
        if (!File.Exists(startupLogPath))
        {
            return null;
        }

        return File.ReadLines(startupLogPath)
            .Reverse()
            .FirstOrDefault(line => line.Contains("Board session rebound.", StringComparison.Ordinal)
                && line.Contains($"Server={expectedServerUrl}", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task RunPowerShellAsync(string scriptPath, string workingDirectory, bool ignoreExitCode = false)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath },
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.Start();
        string stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        string stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        if (!ignoreExitCode && process.ExitCode != 0)
        {
            string output = string.Join(Environment.NewLine, new[] { stdout.Trim(), stderr.Trim() }.Where(text => !string.IsNullOrWhiteSpace(text)));
            throw new InvalidOperationException($"Script '{Path.GetFileName(scriptPath)}' failed with exit code {process.ExitCode}.{Environment.NewLine}{output}");
        }
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "package.json"))
                && File.Exists(Path.Combine(current.FullName, "DemoBoards.WinUI.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not resolve the demo-boards-winui-app repository root.");
    }

    private sealed record Options(bool ReuseRunning, TimeSpan Timeout, bool RunSuite, TimeSpan HoldOpenDuration, bool VerifyServerSwitch)
    {
        public static Options Parse(string[] args)
        {
            bool reuseRunning = false;
            double timeoutSeconds = 45;
            bool runSuite = true;
            double holdOpenSeconds = 20;
            bool verifyServerSwitch = false;

            for (int index = 0; index < args.Length; index++)
            {
                string arg = args[index];
                if (string.Equals(arg, "--reuse-running", StringComparison.OrdinalIgnoreCase))
                {
                    reuseRunning = true;
                    continue;
                }

                if (string.Equals(arg, "--timeout-seconds", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length || !double.TryParse(args[++index], NumberStyles.Float, CultureInfo.InvariantCulture, out timeoutSeconds) || timeoutSeconds <= 0)
                    {
                        throw new ArgumentException("--timeout-seconds requires a positive numeric value.");
                    }

                    continue;
                }

                if (string.Equals(arg, "--ui-only", StringComparison.OrdinalIgnoreCase))
                {
                    runSuite = false;
                    continue;
                }

                if (string.Equals(arg, "--hold-open-seconds", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length || !double.TryParse(args[++index], NumberStyles.Float, CultureInfo.InvariantCulture, out holdOpenSeconds) || holdOpenSeconds < 0)
                    {
                        throw new ArgumentException("--hold-open-seconds requires a numeric value greater than or equal to zero.");
                    }

                    continue;
                }

                if (string.Equals(arg, "--verify-server-switch", StringComparison.OrdinalIgnoreCase))
                {
                    verifyServerSwitch = true;
                    runSuite = false;
                    holdOpenSeconds = 0;
                    continue;
                }

                throw new ArgumentException($"Unknown argument '{arg}'. Supported flags: --reuse-running, --timeout-seconds <value>, --ui-only, --hold-open-seconds <value>, --verify-server-switch.");
            }

            return new Options(reuseRunning, TimeSpan.FromSeconds(timeoutSeconds), runSuite, TimeSpan.FromSeconds(holdOpenSeconds), verifyServerSwitch);
        }
    }
}