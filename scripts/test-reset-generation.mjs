import test from 'node:test';
import assert from 'node:assert/strict';
import {resetDemoHistory,isCurrentDemoGeneration} from '../docs/assets/reset-state.mjs';

test('reset invalidates a pending conversion, but permits a new conversion',()=>{
  const state={generation:3,urls:new Set(),library:[],shelf:[],float:{}};
  const before=state.generation;
  assert.equal(isCurrentDemoGeneration(state,before),true);
  resetDemoHistory(state,()=>{});
  assert.equal(state.generation,4);
  assert.equal(isCurrentDemoGeneration(state,before),false);
  assert.equal(isCurrentDemoGeneration(state,state.generation),true);
});
