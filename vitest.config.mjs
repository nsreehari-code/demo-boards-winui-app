import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    testTimeout: 300000,
    hookTimeout: 300000,
    // These specs each spawn `dotnet run` against projects that share the
    // DemoBoards.RuntimeHost reference. Running them in parallel races on the
    // same build outputs (sourcelink.json lock), so force sequential execution.
    fileParallelism: false,
  },
});