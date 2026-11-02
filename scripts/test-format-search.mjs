import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { filterFormatRows, foldSearchText } from '../docs/assets/format-search.mjs';

test('the Turkish uppercase dotted İ matches ASCII format names', () => {
  const rows = [
    {textContent: 'AVIF — JPG, PNG', hidden: false},
    {textContent: 'TIFF — JPG, PDF', hidden: false},
    {textContent: 'MP4 — AVI, MKV', hidden: false}
  ];
  filterFormatRows(rows, 'AVİF');
  assert.deepEqual(rows.map(row => row.hidden), [false, true, true]);

  filterFormatRows(rows, 'tİff');
  assert.deepEqual(rows.map(row => row.hidden), [true, false, true]);

  filterFormatRows(rows, '  ');
  assert.deepEqual(rows.map(row => row.hidden), [false, false, false]);
});

test('the plain ASCII search and no-match filtering stay unchanged', () => {
  const rows = [{textContent: 'PNG — JPG', hidden: true}, {textContent: 'MOV — MP4', hidden: true}];
  filterFormatRows(rows, 'png');
  assert.deepEqual(rows.map(row => row.hidden), [false, true]);
  filterFormatRows(rows, 'NO SUCH FORMAT');
  assert.deepEqual(rows.map(row => row.hidden), [true, true]);
});

test('playground Library search NFD-folds Turkish dotted İ like Formats', () => {
  const src = fs.readFileSync(new URL('../docs/assets/playground.js', import.meta.url), 'utf8');
  assert.match(src, /foldSearchText\(r\.name\)\.includes\(foldSearchText\(search\.value\)\)/);
  const names = ['AVIF-export.jpg', 'weekend.mp4'];
  assert.deepEqual(names.filter(name => foldSearchText(name).includes(foldSearchText('AVİF'))), ['AVIF-export.jpg']);
  assert.equal('AVIF-export.jpg'.toLowerCase().includes('AVİF'.toLowerCase()), false);
});

test('playground Library search ignores spaces around the query', () => {
  const names = ['AVIF-export.jpg', 'weekend.mp4'];
  assert.deepEqual(names.filter(name => foldSearchText(name).includes(foldSearchText(' AVİF'))), ['AVIF-export.jpg']);
  assert.equal(foldSearchText('  '), '');
});

test('playground Edit Image applies Width to the rotated image', () => {
  const lines = fs.readFileSync(new URL('../docs/assets/playground.js', import.meta.url), 'utf8').split('\n');
  const start = lines.findIndex(line => line.startsWith('function renderImage('));
  let end = start;
  while (!lines[end].includes('return c;}')) end++;
  const noop = new Proxy(function () {}, {get: () => noop, apply: () => noop, set: () => true});
  globalThis.document = {createElement: () => ({width: 0, height: 0, getContext: () => noop})};
  const renderImage = new Function(`${lines.slice(start, end + 1).join('\n')}; return renderImage;`)();
  const size = (width, rotation) => {
    const c = renderImage({image: {width: 400, height: 200}, id: 'editImage', values: {width, rotation, brightness: 100, contrast: 100, saturation: 100}}, true);
    return [c.width, c.height];
  };
  assert.deepEqual(size(400, 0), [400, 200]);
  assert.deepEqual(size(100, 90), [100, 200]);
  assert.deepEqual(size(100, 270), [100, 200]);
  assert.deepEqual(size(200, 90), [200, 400]);
  assert.deepEqual(size(400, 90), [400, 800]);
  assert.deepEqual(size(400, 180), [400, 200]);
});
