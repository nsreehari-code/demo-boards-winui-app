import { spawn } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, test } from 'vitest';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..');
const goldenHarnessProject = path.join('engine', 'GoldenHarness', 'GoldenHarness.csproj');

function runGoldenHarness() {
  return new Promise((resolve, reject) => {
    const child = spawn('dotnet', ['run', '--project', goldenHarnessProject], {
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

describe('GoldenHarness', () => {
  test('passes the embedded V8 and localhost agentface checks', async () => {
    const result = await runGoldenHarness();
    const combinedOutput = [result.stdout.trim(), result.stderr.trim()].filter(Boolean).join('\n');

    expect(result.code, combinedOutput).toBe(0);
    expect(combinedOutput).toContain('[6/6] embedded localhost PASS');
    expect(combinedOutput).toContain('[harness] ALL CHECKS PASSED');
  });
});