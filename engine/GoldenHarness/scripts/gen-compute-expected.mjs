// Generate the cross-engine compute oracle.
//
// Loads the SAME vendored jsonata bundle (yaml-flow/browser/compute-jsonata.js)
// the embedded V8 harness loads, evaluates every case in Node's V8, and writes
// the serialized results to compute-cases.expected.json. The C# harness then
// evaluates the same cases in ClearScript V8 and asserts byte-identical results,
// proving the compute brain is engine-portable (no rounding-mode divergence).
//
// Run from anywhere:  node scripts/gen-compute-expected.mjs

import fs from 'node:fs';
import vm from 'node:vm';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..', '..', '..', '..'); // -> ai-tool-evolver/
const bundlePath = path.join(repoRoot, 'yaml-flow', 'browser', 'compute-jsonata.js');
const casesPath = path.resolve(here, '..', 'fixtures', 'compute-cases.json');
const expectedPath = path.resolve(here, '..', 'fixtures', 'compute-cases.expected.json');

const bundleSource = fs.readFileSync(bundlePath, 'utf8');
// The IIFE sets globalThis.jsonataSync as a side effect.
vm.runInThisContext(bundleSource);
const jsonataSync = globalThis.jsonataSync;
if (typeof jsonataSync !== 'function') {
  throw new Error('compute-jsonata bundle did not set globalThis.jsonataSync');
}

function serialize(result) {
  return JSON.stringify(result === undefined ? null : result);
}

const cases = JSON.parse(fs.readFileSync(casesPath, 'utf8'));
const expected = cases.map((c) => ({
  name: c.name,
  result: serialize(jsonataSync(c.expr).evaluate(c.data)),
}));

fs.writeFileSync(expectedPath, `${JSON.stringify(expected, null, 2)}\n`);
console.log(`[gen-compute-expected] wrote ${expected.length} cases -> ${expectedPath}`);
