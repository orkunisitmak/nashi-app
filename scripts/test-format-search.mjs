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

test('the Turkish lowercase dotless ı matches ASCII I in format names', () => {
  const rows = [
    {textContent: 'TIFF — JPG, PDF', hidden: false},
    {textContent: 'AVIF — JPG, PNG', hidden: false},
    {textContent: 'MP4 — AVI, MKV', hidden: false}
  ];
  filterFormatRows(rows, 'tıff');
  assert.deepEqual(rows.map(row => row.hidden), [false, true, true]);
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

test('playground clock carries a rounded 60 seconds into the minute', () => {
  const src = fs.readFileSync(new URL('../docs/assets/playground.js', import.meta.url), 'utf8').split('\n').find(l => l.startsWith('function time('));
  const time = new Function(src + '; return time;')();
  assert.equal(time(59.9996), '01:00.000');
  assert.equal(time(119.9999), '02:00.000');
  assert.equal(time(61.5), '01:01.500');
  assert.equal(time(0), '00:00.000');
});

test('playground Library search matches Turkish dotless ı in file names', () => {
  const names = ['TIFF-export.tif', 'weekend.mp4'];
  assert.deepEqual(names.filter(name => foldSearchText(name).includes(foldSearchText('tıff'))), ['TIFF-export.tif']);
  assert.equal('TIFF-export.tif'.toLowerCase().includes('tıff'), false);
});

test('playground split keeps repeated pages like the app', () => {
  const src = fs.readFileSync(new URL('../docs/assets/playground.js', import.meta.url), 'utf8').split('\n').find(l => l.startsWith('function parsePages('));
  const parsePages = new Function('L', src + '; return parsePages;')(a => a);
  assert.deepEqual(parsePages('1,1,2', 5), [0, 0, 1]);
  assert.deepEqual(parsePages('1-3,2', 5), [0, 1, 2, 1]);
  assert.deepEqual(parsePages('', 3), [0, 1, 2]);
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
  const huge = renderImage({image: {width: 4000, height: 40}, id: 'editImage', values: {width: 6000, rotation: 90, brightness: 100, contrast: 100, saturation: 100}}, true);
  assert.ok(huge.width * huge.height <= 24000000);
});
