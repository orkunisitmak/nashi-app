import test from 'node:test';
import assert from 'node:assert/strict';
import { filterFormatRows } from '../docs/assets/format-search.mjs';

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
