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

test('playground clock carries a rounded 60 seconds into the minute', () => {
  const src = fs.readFileSync(new URL('../docs/assets/playground.js', import.meta.url), 'utf8').split('\n').find(l => l.startsWith('function time('));
  const time = new Function(src + '; return time;')();
  assert.equal(time(59.9996), '01:00.000');
  assert.equal(time(119.9999), '02:00.000');
  assert.equal(time(61.5), '01:01.500');
  assert.equal(time(0), '00:00.000');
});
