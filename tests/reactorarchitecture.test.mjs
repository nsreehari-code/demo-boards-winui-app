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
const componentsRoot = path.join(appRoot, 'Components');
const mainShellPath = path.join(componentsRoot, 'AppRoot.cs');
const allowedXamlResidue = new Set(['App.xaml']);
const disallowedBindingPattern = /x:Bind|\{Binding\b/;
const requiredReactorSeams = [
  'AppRoot',
  'BoardRenderer',
  'PaneRenderer',
  'CentrePane',
  'GandalfPane',
  'TruthsetExplorePane',
  'GlobalModal',
  'ReactorAppConfigModalComponent',
  'ChallengeConfirmModal',
  'ReactorSmokeRunnerComponent',
];
const requiredComponentScaffolds = [
  'Components/Shared/Chat/ChatPane.cs',
  'Components/Shared/GlobalModal.cs',
  'Components/Shared/ChallengeConfirmModal.cs',
  'Components/Registry/ComponentRegistry.cs',
  'Components/Registry/Engine/NodeRenderer.cs',
  'Components/Registry/Engine/NodeResolver.cs',
  'Components/Registry/Pane/Sub/InfiniteCanvasPane.cs',
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
    const controlSourceFiles = collectFiles(componentsRoot, fullPath => fullPath.endsWith('.cs'));

    expect(csprojText).toContain('PackageReference Include="Microsoft.UI.Reactor"');
    expect(programText).toContain('ReactorApp.Run<DemoBoardsRoot>(');
    expect(programText).toContain('Component<Controls.AppRoot>();');
    expect(mainShellText).toContain('Component<BoardRenderer, BoardRendererProps>(');
    expect(mainShellText).toContain('Component<GlobalModal, GlobalModalProps>(');
    expect(mainShellText).toContain('Component<ReactorSmokeRunnerComponent>()');
    expect(mainShellText).toContain('Action onRunTests = () =>');

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