import { spawn } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { resolveDotnet } from '../scripts/resolve-dotnet.mjs';

import { describe, expect, test } from 'vitest';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..');
const t0HarnessProject = path.join('app', 'engines', 'RuntimeHostT0Harness', 'RuntimeHostT0Harness.csproj');

function runT0Harness() {
  return new Promise((resolve, reject) => {
    const child = spawn(resolveDotnet(), ['run', '--project', t0HarnessProject], {
      cwd: repoRoot,
      stdio: ['ignore', 'pipe', 'pipe'],
    });

    let stdout = '';
    let stderr = '';

    child.stdout.on('data', (chunk) => {
      stdout += String(chunk);
    });

    child.stderr.on('data', (chunk) => {
      stderr += String(chunk);
    });

    child.on('error', (error) => {
      reject(error);
    });

    child.on('close', (code) => {
      resolve({ code: code ?? -1, stdout, stderr });
    });
  });
}

describe('RuntimeHost T0 Harness', () => {
  test('ports backend T0 (upsert/read/completed) to WinUI RuntimeHost', async () => {
    const result = await runT0Harness();
    const combinedOutput = [result.stdout.trim(), result.stderr.trim()].filter(Boolean).join('\n');

    expect(result.code, combinedOutput).toBe(0);
    expect(combinedOutput).toContain('[harness] T0 PASS');
    expect(combinedOutput).toContain('[T0] stored card matches seeded card');
  });
});
