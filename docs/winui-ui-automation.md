# WinUI Desktop UI Automation

This repo now includes a native Windows desktop automation harness for the WinUI app.

## Entry point

Run the harness from the repo root:

```powershell
npm run winui:ui-smoke
```

Or invoke the script directly:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./scripts/run-winui-ui-smoke.ps1
```

## What it verifies

- launches the packaged WinUI app through the existing `start-winui.ps1` flow
- opens Board Settings from the main shell
- opens the Smoke Runner modal from Board Settings
- verifies the `Run Tests` textbox is present
- verifies all runnable smoke-case toggles are present
- toggles `MB1` off and back on, confirming the textbox stays synchronized with the toggle state
- runs the focused subset `MB1, T3u`
- waits for the Smoke Runner to report success and emit output
- leaves the Smoke Runner modal open for 20 seconds after success by default so you can watch the final state

## Notes

- The harness uses UI Automation directly through FlaUI UIA3, so it does not require WinAppDriver or Appium.
- Pass `--reuse-running` to attach to an already-running `DemoBoards.WinUI` process.
- Pass `--timeout-seconds 90` to raise the wait budget on slower machines.
- Pass `--ui-only` if you want the old behavior that checks modal/toggle rendering without executing the suite.
- Pass `--hold-open-seconds 60` to keep the success state visible longer, or `--hold-open-seconds 0` to disable the hold.