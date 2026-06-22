import { createHash } from 'node:crypto';
import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

const grandfatheredXaml = {
  'app/DemoBoards.WinUI/App.xaml': '63d16d773fd860eb24c72276ba85164a92b90b1bed6f65ebaee0b39e41cb92f2',
  'app/DemoBoards.WinUI/Controls/BoardCanvas.xaml': 'b5ebbc157a52c929da2899d68569f42e403b58c2b05dc8bbe0e89781abaf3521',
  'app/DemoBoards.WinUI/Controls/BoardMarkdown.xaml': 'b0640dddbb2cf44e7c4d01697e7338ecf02dbb1e5c8e2e81566f0eb770733f01',
  'app/DemoBoards.WinUI/Controls/CardBackface.xaml': 'd2a87272468afbdb237d7ade2fcd15c13fff2dc005c3cfbcf7133bb4e80e236c',
  'app/DemoBoards.WinUI/Controls/CardCore.xaml': 'a82b6d9c999029609ae6b127a5b9ba301dae05ccd0baa1ee5964cb3c9dec72fa',
  'app/DemoBoards.WinUI/Controls/CardCoreView.xaml': '14e5e220f6b8da5eedda0abc26620698f1c09f9bbfdbf39b830df91696d0e746',
  'app/DemoBoards.WinUI/Controls/CardRenderer.xaml': 'e57c431ca469726e5ea874b0df4366cf3430f394975effb43c914fa1181e917d',
  'app/DemoBoards.WinUI/Controls/CardShell.xaml': '191959e48101511246062f5a1d21e53b07aeb6e7c4dfca35f10d78c639ac93f7',
  'app/DemoBoards.WinUI/Controls/CentrePane.xaml': 'de499ec64c5eb4c6d1f0a60528af63253d45afdd69b0df2d6137affc62bcb4c7',
  'app/DemoBoards.WinUI/Controls/ChallengeConfirmModal.xaml': '4a4246462fa47adf98579a2698e2e4127c56e7cfa2c3e30a7779139206aa169d',
  'app/DemoBoards.WinUI/Controls/ChatPane.xaml': '0e2d06089dbd248193109ab1a9bed4ef3dc8a16c5b8da4d0f899b34ba1a1d87d',
  'app/DemoBoards.WinUI/Controls/GandalfChatPane.xaml': '2f177c04acea3f4fd6979381c722d6d0b5cc40735c27a3bb0a91d9da32791ae9',
  'app/DemoBoards.WinUI/Controls/GandalfPane.xaml': 'a5cccf0349f28c4d16e7189181ccafc7e75a6cc1db36ca52d998a57f0c4df9cb',
  'app/DemoBoards.WinUI/Controls/GlobalModal.xaml': 'cd228ca54727490329b2b00449561f0c5d88d087fb470697777cec31c2add3eb',
  'app/DemoBoards.WinUI/Controls/IngestCard.xaml': '0638cbfa72488a96ff9a9fd10c813f96abb5cca7240a6ea2f95c007ed33ce643',
  'app/DemoBoards.WinUI/Controls/InspectCard.xaml': '7c2c1048ff21e58c1a218446a3ea1cdf21011515556335c30c0576fdbf56ae2f',
  'app/DemoBoards.WinUI/Controls/MainBoard.xaml': '52b65ed2513ccd5571dc234b9da530516c321f4b682e42210a595b12674130ab',
  'app/DemoBoards.WinUI/Controls/MiniChatPane.xaml': '4ef255b22f35af299ff895772d319b9ca307ac7b492fcb21042de7575836eafd',
  'app/DemoBoards.WinUI/Controls/PostboxCard.xaml': '1ed86892be1dcaeaffe1b0e2850ee46a6fb86ce907733c88342055e2d46b453b',
  'app/DemoBoards.WinUI/Controls/SmokeRunner.xaml': '0cfba1725d96fc5aadd3ce73c13240d0a2ab37458512c200db6977731aac8695',
  'app/DemoBoards.WinUI/Controls/StrategistCard.xaml': '90f1e3980e0cd36411e59bdb70a4acd7873146d963b1c20dfae34ad8b317068e',
  'app/DemoBoards.WinUI/Controls/TimerButton.xaml': '2282f36e3ac7a1dcd956c0e4ba72107dc10e8004741d514a7c0834db9d10ea61',
  'app/DemoBoards.WinUI/Controls/TruthsetExplorePane.xaml': '1d2b54e666c9a223ae1fadb6686bd60dfc3cef3a9da7fe9bf4b75a270bd46a1d',
  'app/DemoBoards.WinUI/FrePage.xaml': '5ec9e72819b7e6ee0dcbf61d63a120b675ff31c7f62684477d61ec111cc6c5dc',
  'app/DemoBoards.WinUI/MainPage.xaml': 'f8a7598e75fa1c8edd9fa5294833d5240c5ea0ba4b0d8d8ccf3caed990ef1041',
  'app/DemoBoards.WinUI/MainWindow.xaml': '9ad4d08f72fdabd121625525fc9f2d2e1b690dd02965d7c5b8c4982e23689664',
  'app/DemoBoards.WinUI/Themes/MistOps.xaml': '3d8b4c1b00d3200b68f7c15c7c48c3b85c6dafbeb2084666eef3e15d1cb303de',
  'app/DemoBoards.WinUI/Themes/SignalRoom.xaml': 'c7eddc5d2170978da236c3dbf4c5133598e816681ed7d514912a9affc22d606b',
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
const unexpectedFiles = actualFiles.filter((file) => !(file in grandfatheredXaml));
const modifiedFiles = actualFiles.filter((file) => grandfatheredXaml[file] && sha256(file) !== grandfatheredXaml[file]);

if (unexpectedFiles.length > 0 || modifiedFiles.length > 0) {
  if (unexpectedFiles.length > 0) {
    console.error('Unexpected XAML files detected:');
    for (const file of unexpectedFiles) {
      console.error(`  ${file}`);
    }
  }

  if (modifiedFiles.length > 0) {
    console.error('Grandfathered XAML files were modified:');
    for (const file of modifiedFiles) {
      console.error(`  ${file}`);
    }
  }

  console.error('Reactor guard failed: do not add or edit app-authored XAML without intentionally updating the migration baseline.');
  process.exit(1);
}

console.log(`XAML drift check passed for ${actualFiles.length} grandfathered files.`);