import test from 'node:test';
import assert from 'node:assert/strict';
import {retainDemoResult} from '../docs/assets/library-limit.mjs';
test('browser demo keeps 50 newest results like the native Library',()=>{
 const state={library:Array.from({length:50},(_,i)=>({id:i,url:'blob:old'+i})),urls:new Set(Array.from({length:50},(_,i)=>'blob:old'+i))};
 const freed=[];
 retainDemoResult(state,{id:50,url:'blob:new'},u=>freed.push(u));
 assert.equal(state.library.length,50);
 assert.equal(state.library[0].id,50);
 assert.equal(state.library.at(-1).id,48);
 assert.deepEqual(freed,['blob:old49']);
 assert.equal(state.urls.has('blob:old49'),false);
});
test('preview-only results never revoke a URL, and small history stays untouched',()=>{
 const state={library:[{id:1,url:null}],urls:new Set(['blob:held'])};
 const freed=[];
 retainDemoResult(state,{id:2,url:null},u=>freed.push(u));
 assert.deepEqual(state.library.map(r=>r.id),[2,1]);
 assert.deepEqual(freed,[]);
 assert.equal(state.urls.has('blob:held'),true);
});
