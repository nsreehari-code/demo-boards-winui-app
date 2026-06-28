// Node oracle generator for the PRODUCER golden.
//
// Uses the reference localstorage-backed board adapter emitted by yaml-flow's
// browser build, drives the producer through the shared producer-driver.js, and
// freezes the canonical published payload to fixtures/producer-payload.expected.json.
//
// The C# harness runs the SAME producer-driver.js but swaps only the storage
// backend: real host-provided KV/Journal/Queue/Blob objects. Byte-identical
// output proves the host storage seam is a faithful drop-in.

import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const harnessRoot = path.resolve(here, '..');
// ai-tool-evolver/  (sibling repos live here)
const repoRoot = path.resolve(harnessRoot, '..', '..', '..');
const browserDir = path.join(repoRoot, 'demo-boards-ns-code', 'node_modules', 'yaml-flow', 'browser');

// ---- Map-backed in-memory Web Storage shim (the Node-side localStorage) ------
function createMemoryLocalStorage() {
  const map = new Map();
  return {
    getItem(k) { return map.has(k) ? map.get(k) : null; },
    setItem(k, v) { map.set(String(k), String(v)); },
    removeItem(k) { map.delete(k); },
    key(i) {
      const keys = Array.from(map.keys());
      return i >= 0 && i < keys.length ? keys[i] : null;
    },
    get length() { return map.size; },
    clear() { map.clear(); },
  };
}
globalThis.localStorage = createMemoryLocalStorage();

// ---- Load the bundles into the global scope via indirect eval ----------------
// Indirect eval runs in global scope, so the IIFE `var X = (...)()` globals
// (LocalStorageStorage, ServerRuntimeControlface, ...) attach to globalThis,
// exactly like ClearScript's engine.Execute in V8.
const indirectEval = eval;
function loadBundle(relPath) {
  const code = readFileSync(path.join(browserDir, relPath), 'utf8');
  indirectEval(code);
}

loadBundle('compute-jsonata.js'); // sets globalThis.jsonataSync
loadBundle(path.join('adapters', 'localstorage-storage.js')); // globalThis.LocalStorageStorage
loadBundle('server-runtime-controlface.js'); // globalThis.ServerRuntimeControlface

// ---- Load the shared drivers (canonical + producer sequence) -----------------
indirectEval(readFileSync(path.join(harnessRoot, 'js', 'golden-driver.js'), 'utf8'));
indirectEval(readFileSync(path.join(harnessRoot, 'js', 'producer-driver.js'), 'utf8'));

// ---- Drive the producer and freeze the payload -------------------------------
const fixture = JSON.parse(
  readFileSync(path.join(harnessRoot, 'fixtures', 'producer-cards.json'), 'utf8'),
);

const out = await runProducerPayload(fixture.boardId, JSON.stringify(fixture.cards));
const expectedPath = path.join(harnessRoot, 'fixtures', 'producer-payload.expected.json');
writeFileSync(expectedPath, out, 'utf8');
console.log(`[gen-producer-expected] wrote producer golden -> ${expectedPath}`);
console.log(out);
