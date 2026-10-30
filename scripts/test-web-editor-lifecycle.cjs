// Exercise the actual saveEditor function with deterministic, synthetic exports.
// No browser, network, image/PDF dependencies or user files are needed.
const assert = require('node:assert/strict');
const {readFileSync} = require('node:fs');
const {join} = require('node:path');
const {runInNewContext} = require('node:vm');
const {test} = require('node:test');
const source = readFileSync(join(__dirname, '../docs/assets/playground.js'), 'utf8');
const saveSource = source.slice(source.indexOf('async function saveEditor()'), source.indexOf('\nfunction editorKey('));
const deferred = () => { let resolve, reject; const promise = new Promise((a, b) => {resolve = a; reject = b;}); return {promise, resolve, reject}; };
function harness(overrides = {}) {
  const editor = {id: 'cropImage', ready: true, save: {disabled: false}, real: true, image: {}, files: [{name: 'synthetic.png'}], values: {}};
  const state = {editor}, results = [], errors = [];
  const context = {state, Blob, PDFLib: {}, Number, Math, setTimeout,
    renderImage: () => ({}), canvasBlob: async () => new Blob(['image']), imagesPDF: async () => new Uint8Array([1]),
    addResult: (...args) => results.push(args), toast: (...args) => errors.push(args),
    closeEditor: () => {state.editor = null;}, L: en => en, toolTitle: id => id,
    parsePages: () => [0, 1, 2], ...overrides};
  const save = runInNewContext(`${saveSource}\nsaveEditor`, context);
  return {editor, state, results, errors, save};
}
for (const replacement of [null, {id: 'new editor', save: {disabled: true}}]) {
  test(`Closing/replacing the editor discards a pending image export (${replacement ? 'new editor' : 'close'})`, async () => {
    const pending = deferred();
    const h = harness({canvasBlob: () => pending.promise});
    const saving = h.save();
    h.state.editor = replacement;
    pending.resolve(new Blob(['image']));
    await saving;
    assert.equal(h.results.length, 0);
    assert.equal(h.state.editor, replacement);
    assert.equal(h.errors.length, 0);
    if (replacement) assert.equal(replacement.save.disabled, true);
  });
}
test('An abandoned export failure does not report an error in a new editor', async () => {
  const pending = deferred(), h = harness({canvasBlob: () => pending.promise});
  const saving = h.save(); h.state.editor = {id: 'new editor'};
  pending.reject(Error('old image error')); await saving;
  assert.equal(h.errors.length, 0); assert.equal(h.results.length, 0);
});
test('Repeated save clicks encode and publish only one successful image', async () => {
  const pending = deferred(); let encodes = 0;
  const h = harness({canvasBlob: () => {encodes++; return pending.promise;}});
  const saving = h.save(); await h.save();
  assert.equal(h.editor.save.disabled, true); assert.equal(encodes, 1);
  pending.resolve(new Blob(['image'])); await saving;
  assert.equal(h.results.length, 1); assert.equal(h.results[0][0], 'synthetic-cropImage.png');
  assert.equal(h.state.editor, null);
});
test('An active export failure leaves the editor retryable', async () => {
  let failed = false;
  const h = harness({canvasBlob: async () => {if (!failed) {failed = true; throw Error('synthetic failure');} return new Blob(['image']);}});
  await h.save(); assert.equal(h.errors.length, 1); assert.equal(h.results.length, 0);
  assert.equal(h.state.editor, h.editor); assert.equal(h.editor.save.disabled, false); assert.equal(h.editor.saving, false);
  await h.save(); assert.equal(h.results.length, 1); assert.equal(h.state.editor, null);
});
function pdfHarness(savePart) {
  let part = 0;
  const h = harness({PDFLib: {PDFDocument: {create: async () => {
    const number = ++part;
    return {copyPages: async (_source, indices) => indices, addPage: () => {}, save: () => savePart(number)};
  }}}});
  Object.assign(h.editor, {id: 'splitPDF', image: null, pdfs: [{getPageCount: () => 3}], values: {pages: '1-3', perFile: 1}});
  return h;
}
test('A later split-PDF failure publishes none of the earlier parts', async () => {
  const h = pdfHarness(async part => {if (part === 2) throw Error('synthetic PDF failure'); return new Uint8Array([part]);});
  await h.save(); assert.equal(h.results.length, 0); assert.equal(h.errors.length, 1);
  assert.equal(h.state.editor, h.editor); assert.equal(h.editor.save.disabled, false);
});
test('Closing during the second PDF part discards the whole batch', async () => {
  const pending = deferred(), started = deferred();
  const h = pdfHarness(part => {if (part === 2) {started.resolve(); return pending.promise;} return new Uint8Array([part]);});
  const saving = h.save(); await started.promise;
  assert.equal(h.results.length, 0); h.state.editor = null;
  pending.resolve(new Uint8Array([2])); await saving;
  assert.equal(h.results.length, 0); assert.equal(h.errors.length, 0);
});
test('A successful PDF split publishes all three parts in order', async () => {
  const h = pdfHarness(async part => new Uint8Array([part]));
  await h.save(); assert.deepEqual(h.results.map(r => r[0]), ['synthetic-part-1.pdf', 'synthetic-part-2.pdf', 'synthetic-part-3.pdf']);
  assert.equal(h.errors.length, 0); assert.equal(h.state.editor, null);
});

test('Closing during images-to-PDF export discards its result', async () => {
  const pending = deferred(), h = harness({imagesPDF: () => pending.promise});
  h.editor.id = 'createPDF';
  const saving = h.save(); h.state.editor = null;
  pending.resolve(new Uint8Array([1])); await saving;
  assert.equal(h.results.length, 0); assert.equal(h.errors.length, 0);
});
for (const id of ['mergePDF', 'organizePDF']) {
  test(`Replacing the editor during ${id} serialization leaves the new editor alone`, async () => {
    const pending = deferred(), started = deferred();
    const page = {getRotation: () => ({angle: 0}), setRotation: () => {}};
    const h = harness({PDFLib: {degrees: value => value, PDFDocument: {create: async () => ({
      copyPages: async () => [page], addPage: () => {}, save: () => {started.resolve(); return pending.promise;}
    })}}});
    Object.assign(h.editor, {id, image: null, pdfs: [{getPageIndices: () => [0]}], pages: [{index: 0, rotation: 90}]});
    const saving = h.save(); await started.promise;
    const nextEditor = {id: 'next editor', save: {disabled: true}}; h.state.editor = nextEditor;
    pending.resolve(new Uint8Array([1])); await saving;
    assert.equal(h.results.length, 0); assert.equal(h.errors.length, 0);
    assert.equal(h.state.editor, nextEditor); assert.equal(nextEditor.save.disabled, true);
  });
}
test('Closing a video snapshot during seeking never touches the discarded media', async () => {
  let seeked;
  const h = harness({setTimeout: () => {}, document: {createElement: () => {throw Error('discarded media was accessed');}}});
  Object.assign(h.editor, {id: 'videoSnapshots', image: null, duration: 10, values: {timestamp: 2},
    media: {currentTime: 0, addEventListener: (_event, listener) => {seeked = listener;}}});
  const saving = h.save(); h.state.editor = null; seeked(); await saving;
  assert.equal(h.results.length, 0); assert.equal(h.errors.length, 0);
});
