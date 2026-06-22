import { createHash } from 'node:crypto';
import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

const allowedBootstrapXaml = {
  'app/DemoBoards.WinUI/App.xaml': '63d16d773fd860eb24c72276ba85164a92b90b1bed6f65ebaee0b39e41cb92f2',
};

function walk(relativeDir) {
  const absoluteDir = path.join(repoRoot, relativeDir);
  const entries = readdirSync(absoluteDir, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    if (entry.name === 'bin' || entry.name === 'obj') {
      continue;
    }

    const relativePath = path.posix.join(relativeDir.replaceAll('\\', '/'), entry.name);
    if (entry.isDirectory()) {
      files.push(...walk(relativePath));
      continue;
    }

    if (entry.isFile() && entry.name.toLowerCase().endsWith('.xaml')) {
      files.push(relativePath);
    }
  }

  return files;
}

function sha256(relativePath) {
  const absolutePath = path.join(repoRoot, relativePath);
  const content = readFileSync(absolutePath);
  return createHash('sha256').update(content).digest('hex');
}

const actualFiles = walk('app/DemoBoards.WinUI').sort();
const expectedFiles = Object.keys(allowedBootstrapXaml).sort();
const missingFiles = expectedFiles.filter((file) => !actualFiles.includes(file));
const unexpectedFiles = actualFiles.filter((file) => !(file in allowedBootstrapXaml));
const modifiedFiles = actualFiles.filter((file) => allowedBootstrapXaml[file] && sha256(file) !== allowedBootstrapXaml[file]);

if (missingFiles.length > 0 || unexpectedFiles.length > 0 || modifiedFiles.length > 0) {
  if (missingFiles.length > 0) {
    console.error('Required bootstrap XAML files are missing:');
    for (const file of missingFiles) {
      console.error(`  ${file}`);
    }
  }

  if (unexpectedFiles.length > 0) {
    console.error('Unexpected app-authored XAML files detected:');
    for (const file of unexpectedFiles) {
      console.error(`  ${file}`);
    }
  }

  if (modifiedFiles.length > 0) {
    console.error('Pinned bootstrap XAML files were modified:');
    for (const file of modifiedFiles) {
      console.error(`  ${file}`);
    }
  }

  console.error('Reactor guard failed: the only allowed authored XAML residue is the minimal App.xaml bootstrap stub.');
  process.exit(1);
}

console.log(`XAML drift check passed: only ${expectedFiles[0]} remains as the pinned bootstrap stub.`);