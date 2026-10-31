import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import {readFileSync} from 'node:fs';
const source=readFileSync(new URL('./pages-runtime.js',import.meta.url),'utf8');
const context=vm.createContext({
  _nashiBase64Decode:value=>Buffer.from(value,'base64').toString('latin1'),
  _nashiBase64Encode:value=>Buffer.from(value,'latin1').toString('base64'),
  _nashiUTF8Encode:value=>Buffer.from(value,'utf8').toString('base64'),
  _nashiUTF8Decode:value=>Buffer.from(value,'base64').toString('utf8'),
  _nashiRandom:size=>Buffer.alloc(size,7).toString('base64')
});
vm.runInContext(source,context);

test('Pages TextDecoder accepts ArrayBuffer, typed-array views and DataView byte offsets',()=>{
  assert.equal(vm.runInContext("new TextDecoder().decode(new Uint8Array([65,195,169]).buffer)",context),'Aé');
  assert.equal(vm.runInContext("new TextDecoder().decode(new DataView(new Uint8Array([0,65,195,169,0]).buffer,1,3))",context),'Aé');
  assert.equal(vm.runInContext("new TextDecoder().decode(new Uint8Array([0,65,195,169,0]).subarray(1,4))",context),'Aé');
});

test('Pages fallback TextDecoder keeps existing Uint8Array and empty-input behavior',()=>{
  assert.equal(vm.runInContext("new TextDecoder().decode(new Uint8Array([84,82]))",context),'TR');
  assert.equal(vm.runInContext("new TextDecoder().decode()",context),'');
});
