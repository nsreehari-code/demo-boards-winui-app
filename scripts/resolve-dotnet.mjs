import { existsSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';

/**
 * Resolves the dotnet executable, preferring the .NET 10 SDK used by the WinUI build.
 *
 * Mirrors `Get-WinUiDotnetPath` in scripts/winui-common.ps1: prefer an explicit
 * `DOTNET_ROOT`, then the per-user `~/.dotnet` install (where the .NET 10 SDK lives),
 * and finally fall back to a bare `dotnet` on `PATH`. Bare `dotnet` on this machine
 * resolves to the system 9.0.x SDK, which cannot build the net10.0 projects.
 *
 * @returns {string} Absolute path to a dotnet executable, or `dotnet`/`dotnet.exe` as a fallback.
 */
export function resolveDotnet() {
  const exe = process.platform === 'win32' ? 'dotnet.exe' : 'dotnet';

  const roots = [];
  if (process.env.DOTNET_ROOT) {
    roots.push(process.env.DOTNET_ROOT);
  }
  const home = process.env.USERPROFILE || os.homedir();
  if (home) {
    roots.push(path.join(home, '.dotnet'));
  }

  for (const root of roots) {
    if (!root) {
      continue;
    }
    const candidate = path.join(root, exe);
    if (existsSync(candidate)) {
      // Ensure spawned children resolve the matching runtime root too.
      process.env.DOTNET_ROOT = root;
      return candidate;
    }
  }

  return exe;
}
