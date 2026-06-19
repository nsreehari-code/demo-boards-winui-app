// Platform-free golden replay driver, executed INSIDE the embedded V8 engine.
//
// Mirrors the Node golden oracle in
//   demo-boards-frontend/tests/board-sse-state.golden.test.js
// (replay + canonical key ordering) so the embedded engine is held to the exact
// same contract. Depends only on the global `BoardSseState`, provided by the
// board-sse-state.js IIFE bundle loaded ahead of this file.

function canonical(value) {
  if (Array.isArray(value)) {
    return value.map(canonical);
  }
  if (value && typeof value === 'object') {
    const out = {};
    for (const key of Object.keys(value).sort()) {
      out[key] = canonical(value[key]);
    }
    return out;
  }
  return value;
}

function replay(frames, boardId) {
  let snapshot = BoardSseState.createEmptyBoardSnapshot(boardId);
  for (const frame of frames) {
    snapshot = BoardSseState.applyBoardSseFrame(snapshot, frame);
  }
  return snapshot;
}

// Entry point invoked by the C# host. Returns the canonical snapshot as a
// trailing-newline JSON string, matching the golden fixture's on-disk form.
function runGoldenReplay(framesJson) {
  const frames = JSON.parse(framesJson);
  const boardId = (frames[0] && frames[0].boardId) || null;
  const snapshot = canonical(replay(frames, boardId));
  return JSON.stringify(snapshot, null, 2) + '\n';
}
