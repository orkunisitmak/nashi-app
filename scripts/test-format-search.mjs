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

test('playground Convert Audio Channels Mono mixes both levels like the app', async () => {
  const src = fs.readFileSync(new URL('../docs/assets/playground.js', import.meta.url), 'utf8').split('\n').find(line => line.startsWith('function audioWav('));
  globalThis.Blob = class { constructor(parts) { this.buf = parts[0]; } };
  const audioWav = new Function(`${src}; return audioWav;`)();
  const left = new Float32Array(4).fill(0.8), right = new Float32Array(4).fill(0.8);
  const audioBuffer = {sampleRate: 8000, length: 4, numberOfChannels: 2, getChannelData: c => c ? right : left};
  const mono = (l, r) => new DataView(audioWav({id: 'audioChannels', audioBuffer, values: {channels: 'mono', left: l, right: r}}).buf).getInt16(44, true) / 32767;
  for (const [l, r, want] of [[100, 100, 0.8], [0, 100, 0.4], [100, 50, 0.6], [0, 0, 0]]) assert.ok(Math.abs(mono(l, r) - want) < 0.01);
});
