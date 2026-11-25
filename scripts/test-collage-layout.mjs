import assert from 'node:assert/strict';
import { collageLabelText, collageGrid, collageCanvasSize, paintCollage } from '../docs/assets/collage-layout.mjs';

assert.equal(collageLabelText('numbers', 0, 1), '1');
assert.equal(collageLabelText('letters', 0, 1), 'A');
assert.equal(collageLabelText('letters', 25, 1), 'Z');
assert.equal(collageLabelText('letters', 26, 1), 'AA');
assert.deepEqual(Array.from({ length: 10 }, (_, i) => collageLabelText('letters', i, 1)), ['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J']);
assert.equal(collageLabelText('numbers', 0, 5), '5');
assert.equal(collageLabelText('none', 0, 1), null);

const tight = collageGrid(4, 'grid', 2, 900, 400, 80);
assert.ok(tight.cells.length === 4 && tight.cells.every(cell => cell.w >= 8 && cell.h >= 8), 'a large gap must keep every cell drawable');

const wide = { width: 200, height: 50 };
const canvas = collageCanvasSize(1, 'row', 2, 0, 200);
const grid = collageGrid(1, 'row', 2, canvas.width, canvas.height, 0);
const cell = grid.cells[0];
const fitScale = Math.min(cell.w / wide.width, cell.h / wide.height);
const fillScale = Math.max(cell.w / wide.width, cell.h / wide.height);
assert.ok(wide.width * fitScale <= cell.w + 0.01 && wide.height * fitScale <= cell.h + 0.01, 'fit stays inside the cell');
assert.ok(wide.width * fillScale >= cell.w - 0.01 && wide.height * fillScale >= cell.h - 0.01, 'fill covers the cell');

function context(width, height) {
  return { canvas: { width, height }, fillStyle: '', font: '', textAlign: '', textBaseline: '', save() {}, restore() {}, beginPath() {}, rect() {}, clip() {}, fillRect() {}, drawImage() {}, arc() {}, fill() {}, fillText() {} };
}
const fitMarks = paintCollage(context(canvas.width, canvas.height), [wide], { layout: 'row', fit: 'fit', gap: 0, labels: 'numbers', labelPosition: 'topLeft', labelStart: 1, background: '#e6dfc8' });
const fillMarks = paintCollage(context(canvas.width, canvas.height), [wide], { layout: 'row', fit: 'fill', gap: 0, labels: 'numbers', labelPosition: 'topLeft', labelStart: 1 });
assert.equal(fitMarks[0].text, '1');
assert.ok(fitMarks[0].y > fillMarks[0].y, 'a fitted badge sits on the image, below the top letterbox');

const size = collageCanvasSize(2, 'row', 2, 20, 900);
const topLeft = paintCollage(context(size.width, size.height), [wide, wide], { layout: 'row', fit: 'fill', gap: 20, labels: 'letters', labelPosition: 'topLeft', labelStart: 1, background: '#e6dfc8' });
const bottomRight = paintCollage(context(size.width, size.height), [wide, wide], { layout: 'row', fit: 'fill', gap: 20, labels: 'letters', labelPosition: 'bottomRight', labelStart: 1, background: '#e6dfc8' });
assert.deepEqual(topLeft.map(mark => mark.text), ['A', 'B']);
assert.ok(bottomRight[0].x > topLeft[0].x && bottomRight[0].y > topLeft[0].y, 'bottom right is a different corner from top left');
assert.equal(paintCollage(context(size.width, size.height), [wide, { width: 0, height: 10 }], { layout: 'row', gap: 80, labels: 'numbers' }).length, 1);

const demo = collageCanvasSize(2, 'row', 2, 20, 900);
assert.ok(demo.width === 900 && demo.height > 40, 'the demo collage keeps padding around the images');
console.log('collage layout ok');
