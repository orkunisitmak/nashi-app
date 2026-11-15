import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import {readFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
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

const bundleBytes=readFileSync(new URL('../Resources/PagesInterop.js',import.meta.url));
const bundle=bundleBytes.toString('utf8');

test('shipped PagesInterop bundle handles TextDecoder buffer and view inputs',()=>{
  const start=bundle.indexOf('  if (typeof globalThis.TextDecoder !== "function") {');
  const end=bundle.indexOf('  if (typeof globalThis.crypto === "undefined")',start);
  assert.ok(start>=0 && end>start,'the shipped bundle must contain the fallback TextDecoder');
  const runtime=vm.createContext({
    btoa:value=>Buffer.from(value,'latin1').toString('base64'),
    _nashiUTF8Decode:value=>Buffer.from(value,'base64').toString('utf8')
  });
  vm.runInContext(bundle.slice(start,end),runtime);
  assert.equal(vm.runInContext("new TextDecoder().decode(new Uint8Array([65,195,169]).buffer)",runtime),'Aé');
  assert.equal(vm.runInContext("new TextDecoder().decode(new DataView(new Uint8Array([0,65,195,169,0]).buffer,1,3))",runtime),'Aé');
  assert.equal(vm.runInContext("new TextDecoder().decode(new Uint8Array([0,65,195,169,0]).subarray(1,4))",runtime),'Aé');
  assert.equal(vm.runInContext("new TextDecoder().decode()",runtime),'');
  assert.equal(vm.runInContext("new TextDecoder().decode(new Uint8Array([84,82]))",runtime),'TR');
  assert.throws(()=>vm.runInContext("new TextDecoder().decode('bad')",runtime),/buffer or buffer view/);
});

test('PagesInterop bundle bytes and checksum match pinned provenance',()=>{
  const provenance=JSON.parse(readFileSync(new URL('../Resources/licenses/cupertino-files/provenance.json',import.meta.url),'utf8'));
  assert.equal(provenance.bundle.path,'Resources/PagesInterop.js');
  // Normalize checkout line endings to the committed LF form for byte integrity.
  const committedBytes=Buffer.from(bundle.replace(/\r\n/g,'\n'),'utf8');
  assert.equal(provenance.bundle.bytes,committedBytes.length);
  assert.equal(provenance.bundle.sha256,createHash('sha256').update(committedBytes).digest('hex'));
});
