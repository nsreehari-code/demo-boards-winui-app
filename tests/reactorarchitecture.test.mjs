import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, test } from 'vitest';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..');
const appRoot = path.join(repoRoot, 'app', 'DemoBoards.WinUI');
const csprojPath = path.join(appRoot, 'DemoBoards.WinUI.csproj');
const programPath = path.join(appRoot, 'Program.cs');
const controlsRoot = path.join(appRoot, 'Controls');
const mainShellPath = path.join(controlsRoot, 'ReactorMainShellComponent.cs');
const allowedXamlResidue = new Set(['App.xaml']);
const disallowedBindingPattern = /x:Bind|\{Binding\b/;
const requiredReactorSeams = [
  'ReactorMainShellComponent',
  'BoardRenderer',
  'PaneRenderer',
  'CentrePane',
  'GandalfPane',
  'TruthsetExplorePane',
  'ReactorGlobalModalComponent',
  'ReactorAppConfigModalComponent',
  'ReactorChallengeConfirmModalComponent',
  'ReactorSmokeRunnerComponent',
  'ReactorShellBridge',
];
const requiredComponentScaffolds = [
  'Controls/shared/ChatPane.cs',
  'Controls/shared/GlobalModal.cs',
  'Controls/shared/ChallengeConfirmModal.cs',
  'Controls/Registry/ComponentRegistry.cs',
  'Controls/Registry/NodeRenderer.cs',
  'Controls/Registry/NodeResolver.cs',
  'Controls/Registry/pane/sub/InfiniteCanvasPane.cs',
];

function walkFiles(rootDir, visitor) {
  const pending = [rootDir];
  while (pending.length > 0) {
    const current = pending.pop();
    for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
      if (entry.name === 'bin' || entry.name === 'obj') {
        continue;
      }

      const fullPath = path.join(current, entry.name);
      if (entry.isDirectory()) {
        pending.push(fullPath);
        continue;
      }

      visitor(fullPath);
    }
  }
}

function collectFiles(rootDir, predicate) {
  const results = [];
  walkFiles(rootDir, fullPath => {
    if (predicate(fullPath)) {
      results.push(fullPath);
    }
  });
  return results;
}

function fileContainsPattern(filePath, pattern) {
  return pattern.test(fs.readFileSync(filePath, 'utf8'));
}

describe('ReactorArchitecture', () => {
  test('WinUI app stays Reactor-first with no app-authored XAML UI surface', () => {
    const csprojText = fs.readFileSync(csprojPath, 'utf8');
    const programText = fs.readFileSync(programPath, 'utf8');
    const mainShellText = fs.readFileSync(mainShellPath, 'utf8');
    const controlSourceFiles = collectFiles(controlsRoot, fullPath => fullPath.endsWith('.cs'));

    expect(csprojText).toContain('PackageReference Include="Microsoft.UI.Reactor"');
    expect(programText).toContain('ReactorApp.Run<DemoBoardsRoot>(');
    expect(programText).toContain('Component<Controls.ReactorMainShellComponent>()');
    expect(mainShellText).toContain('Component<BoardRenderer, BoardRendererProps>(');
    expect(mainShellText).toContain('Component<ReactorGlobalModalComponent, ReactorGlobalModalProps>(');
    expect(mainShellText).toContain('Component<ReactorSmokeRunnerComponent>()');
    expect(mainShellText).toContain('ReactorShellBridge.SmokeRunnerRequested += onSmokeRequested;');

    const missingSeams = requiredReactorSeams.filter(seamName => {
      const declarationPattern = new RegExp(`\\b${seamName}\\b`);
      return !controlSourceFiles.some(filePath => fileContainsPattern(filePath, declarationPattern));
    });
    const missingScaffolds = requiredComponentScaffolds.filter(relativePath => !fs.existsSync(path.join(appRoot, relativePath)));

    const xamlFiles = [];
    const bindingMatches = [];

    walkFiles(appRoot, fullPath => {
      const relativePath = path.relative(appRoot, fullPath).replaceAll('\\', '/');
      if (relativePath.endsWith('.xaml')) {
        xamlFiles.push(relativePath);
      }

      if (!relativePath.endsWith('.xaml') && !relativePath.endsWith('.cs')) {
        return;
      }

      const text = fs.readFileSync(fullPath, 'utf8');
      if (disallowedBindingPattern.test(text)) {
        bindingMatches.push(relativePath);
      }
    });

    const unexpectedXamlFiles = xamlFiles.filter(relativePath => !allowedXamlResidue.has(path.basename(relativePath)));

    expect(missingSeams).toEqual([]);
    expect(missingScaffolds).toEqual([]);
    expect(unexpectedXamlFiles).toEqual([]);
    expect(bindingMatches).toEqual([]);
  });
});