import test from 'node:test';
import assert from 'node:assert/strict';
import {nextFileSelection} from '../docs/assets/file-selection.mjs';
test('plain click on an already-selected item collapses a multi-selection',()=>{
 assert.deepEqual(nextFileSelection(['first','second'],'first'),['first']);
 assert.deepEqual(nextFileSelection(['first','second'],'second'),['second']);
});
test('dragging a selected item preserves a batch selection',()=>{
 assert.deepEqual(nextFileSelection(['first','second'],'first',{preserveBatch:true}),['first','second']);
});
test('keyboard activation and modifier wheel clicks preserve already-selected batches',()=>{
  assert.deepEqual(nextFileSelection(['first','second'],'first',{preserveBatch:true}),['first','second']);
  assert.deepEqual(nextFileSelection(['first','second'],'second',{preserveBatch:true}),['first','second']);
});
test('Shift click toggles without losing other selections',()=>{
 assert.deepEqual(nextFileSelection(['first','second'],'second',{toggle:true}),['first']);
 assert.deepEqual(nextFileSelection(['first'],'second',{toggle:true}),['first','second']);
});
