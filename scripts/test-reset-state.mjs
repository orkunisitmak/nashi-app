import test from 'node:test';
import assert from 'node:assert/strict';
import {resetDemoHistory,isCurrentDemoGeneration} from '../docs/assets/reset-state.mjs';
test('reset clears exported results, pinned items and their object URLs',()=>{
  const released=[];
  const state={urls:new Set(['blob:one','blob:two']),library:[{name:'one'}],shelf:[{name:'pinned'}],float:{open:true,expanded:true,list:true,selected:['one'],x:100,y:10},files:[{id:'sample'}],selected:['sample']};
  resetDemoHistory(state,url=>released.push(url));
  assert.deepEqual(released,['blob:one','blob:two']);
  assert.equal(state.urls.size,0);
  assert.deepEqual(state.library,[]);
  assert.deepEqual(state.shelf,[]);
  assert.deepEqual(state.float,{open:false,expanded:false,list:false,selected:[]});
  assert.deepEqual(state.files,[{id:'sample'}]);
  assert.deepEqual(state.selected,['sample']);
});
