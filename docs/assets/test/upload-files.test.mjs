import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { runInNewContext } from 'node:vm';

const source = readFileSync(new URL('../playground.js', import.meta.url), 'utf8');
const implementation = source.split(/\r?\n/).find(line => line.startsWith('async function importFiles(files){'));
assert.ok(implementation, 'playground file import implementation must be present');

function setup() {
  const state = { files: [], selected: [] };
  const alerts = [];
  const context = {
    state,
    closeWheel: () => {},
    L: text => text,
    toast: (...message) => alerts.push(message),
    fileModel: (name, kind, index) => ({id: 'file-' + index, name, kind, ext: name.split('.').at(-1).toLowerCase()}),
    objectURL: () => 'blob:synthetic',
    renderFiles: () => {},
  };
  const importFiles = runInNewContext(implementation + '\nimportFiles', context);
  return { state, alerts, importFiles };
}

function file(name, type = '', size = 128) {
  return { name, type, size };
}

test('accepts a PNG with no reported MIME type', async () => {
  const { state, importFiles } = setup();
  await importFiles([file('drawing.PNG')]);
  assert.equal(state.files.length, 1);
  assert.equal(state.files[0].kind, 'image');
  assert.equal(state.files[0].ext, 'png');
});

test('accepts a PDF with no reported MIME type', async () => {
  const { state, importFiles } = setup();
  await importFiles([file('handbook.PDF')]);
  assert.equal(state.files.length, 1);
  assert.equal(state.files[0].kind, 'file');
  assert.equal(state.files[0].ext, 'pdf');
});

test('normalizes JPEG extension to its supported jpg format', async () => {
  const { state, importFiles } = setup();
  await importFiles([file('photo.JPEG')]);
  assert.equal(state.files.length, 1);
  assert.equal(state.files[0].ext, 'jpg');
});

test('still accepts explicit supported MIME types', async () => {
  const { state, importFiles } = setup();
  await importFiles([file('asset.png', 'image/png')]);
  assert.equal(state.files.length, 1);
  assert.equal(state.files[0].ext, 'png');
});

test('does not trust filename when MIME explicitly reports unsupported content', async () => {
  const { state, importFiles } = setup();
  await importFiles([file('not-an-image.png', 'text/plain')]);
  assert.equal(state.files.length, 0);
});

test('does not accept arbitrary unknown-extension files', async () => {
  const { state, importFiles } = setup();
  await importFiles([file('notes.exe')]);
  assert.equal(state.files.length, 0);
});

test('still rejects an oversized supported upload', async () => {
  const { state, importFiles } = setup();
  await importFiles([file('large.png', '', 26 * 1024 * 1024)]);
  assert.equal(state.files.length, 0);
});
