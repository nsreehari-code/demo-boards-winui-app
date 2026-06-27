import { spawn } from 'node:child_process';

import { resolveDotnet } from './resolve-dotnet.mjs';

// Thin wrapper so npm scripts invoke the .NET 10 SDK (see resolve-dotnet.mjs)
// instead of a bare `dotnet`, which resolves to the system 9.0.x SDK.
const dotnet = resolveDotnet();
const child = spawn(dotnet, process.argv.slice(2), { stdio: 'inherit' });

child.on('error', (error) => {
  console.error(error);
  process.exit(1);
});

child.on('exit', (code, signal) => {
  if (signal) {
    process.kill(process.pid, signal);
    return;
  }
  process.exit(code ?? 0);
});
