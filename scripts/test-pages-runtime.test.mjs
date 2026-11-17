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

test('Pages fallback TextDecoder decodes the latin1 label family as windows-1252',()=>{
  assert.equal(vm.runInContext("new TextDecoder('latin1').decode(new Uint8Array([37,80,68,70,233,128,153]))",context),'%PDFé€™');
  assert.equal(vm.runInContext("new TextDecoder(' ISO-8859-1 ').decode(new DataView(new Uint8Array([0,65,255,0]).buffer,1,2))",context),'Aÿ');
  assert.throws(()=>vm.runInContext("new TextDecoder('utf-16')",context),/Only UTF-8/);
});

// The shipped bundle under a JavaScriptCore-like host: only the _nashi hooks,
// none of Node's web platform globals.
function shippedPages(cancelAfter=Infinity){
  let checks=0;
  const host=vm.createContext({
    _nashiBase64Decode:value=>Buffer.from(value,'base64').toString('latin1'),
    _nashiBase64Encode:value=>Buffer.from(value,'latin1').toString('base64'),
    _nashiUTF8Encode:value=>Buffer.from(value,'utf8').toString('base64'),
    _nashiUTF8Decode:value=>Buffer.from(value,'base64').toString('utf8'),
    _nashiRandom:size=>Buffer.alloc(size,7).toString('base64'),
    _nashiCheckCancelled:()=>++checks>cancelAfter
  });
  vm.runInContext(bundle+'\n;globalThis.NashiPages=NashiPages;',host);
  return {pages:host.NashiPages,checks:()=>checks};
}
const roundTrip=model=>{const {pages}=shippedPages();return JSON.parse(pages.read(pages.write(JSON.stringify(model))));};
const grid=(rows,columns)=>Array.from({length:rows},(_,row)=>Array.from({length:columns},(_,column)=>`r${row}c${column}`));
const texts=blocks=>blocks.map(block=>block.type==='table'?block.rows.map(row=>row.map(cell=>cell.text).join('|')):(block.runs??[]).map(run=>run.type==='image'?'<image>':run.text).join(''));

test('shipped PagesInterop bundle writes a PDF image instead of failing on its latin1 header',()=>{
  const pdf=Buffer.from('%PDF-1.4\n1 0 obj<</Type/Page/MediaBox [0 0 200 100]>>endobj\n\x80\x99\n%%EOF\n','latin1').toString('base64');
  assert.deepEqual(texts(roundTrip({blocks:[{runs:[{text:'a'},{type:'image',data:pdf,fileName:'x.pdf'}]}]}).blocks),['a<image>']);
});

test('shipped PagesInterop bundle keeps a trailing link out of later paragraphs',()=>{
  const blocks=roundTrip({blocks:[{runs:[{text:'see '},{text:'site',link:'https://example.com/'}]},{text:'next'},{text:'last'}]}).blocks;
  assert.deepEqual(blocks.map(block=>block.runs.map(run=>run.link??null)),[[null,'https://example.com/'],[null],[null]]);
});

test('shipped PagesInterop bundle keeps a blank or table second paragraph from restyling the first',()=>{
  const blank=roundTrip({blocks:[{text:'Title',character:{bold:true}},{text:''},{text:'body'}]}).blocks;
  assert.deepEqual(texts(blank),['Title','','body']);
  assert.deepEqual(blank.map(block=>block.character.bold),[true,false,false]);
  const table=roundTrip({blocks:[{text:'Title',character:{bold:true}},{type:'table',rows:grid(2,2)}]}).blocks;
  assert.equal(table[0].character.bold,true);
  assert.deepEqual(texts(table),['Title',['r0c0|r0c1','r1c0|r1c1']]);
});

test('shipped PagesInterop bundle reads page and section breaks as pageBreakBefore',()=>{
  const blocks=roundTrip({blocks:[{text:'one'},{text:'a\u000cb'},{text:'c\u0004d'}]}).blocks;
  assert.deepEqual(texts(blocks),['one','a','b','c','d']);
  assert.deepEqual(blocks.map(block=>block.paragraph.pageBreakBefore===true),[false,false,true,false,true]);
});

test('shipped PagesInterop bundle reads every table cell and gives each new table a clean copy of the seed',()=>{
  const blocks=roundTrip({blocks:[{type:'table',rows:grid(3,3),merges:[{row:0,column:0,rowCount:2,columnCount:2}]},{type:'table',rows:grid(3,3)},{type:'table',rows:grid(1,1)}]}).blocks.filter(block=>block.type==='table');
  assert.deepEqual(blocks.map(block=>block.merges),[[{row:0,column:0,rowCount:2,columnCount:2}],[],[]]);
  assert.deepEqual(texts(blocks.slice(1)),[['r0c0|r0c1|r0c2','r1c0|r1c1|r1c2','r2c0|r2c1|r2c2'],['r0c0']]);
  // The seed's tinted first row must not reach converted tables.
  for(const block of blocks)for(const row of block.rows)for(const cell of row)assert.equal(cell.formatting?.fill,undefined);
});

test('shipped PagesInterop bundle writes Word subscript as the Pages subscript type',()=>{
  const blocks=roundTrip({blocks:[{runs:[{text:'H'},{text:'2',character:{superscript:-1}},{text:'O'},{text:'x',character:{superscript:1}}]},{text:'sub',character:{superscript:-1}}]}).blocks;
  assert.deepEqual(blocks[0].runs.map(run=>run.character.superscript),[0,2,0,1]);
  assert.equal(blocks[1].character.superscript,2);
});

test('shipped PagesInterop bundle keeps headers and footers of documents without tables',()=>{
  const both=roundTrip({blocks:[{text:'body'}],header:'HEAD',footer:'FOOT'});
  assert.deepEqual([both.header,both.footer,texts(both.blocks)],['HEAD','FOOT',['body']]);
  assert.equal(roundTrip({blocks:[{text:'body'}],footer:'FOOT'}).footer,'FOOT');
  assert.deepEqual(texts(roundTrip({blocks:[{text:'body'}]}).blocks),['body']);
});

test('shipped PagesInterop bundle keeps the first of overlapping merges and stays cancellable while merging',()=>{
  const overlapping=roundTrip({blocks:[{type:'table',rows:grid(3,3),merges:[{row:0,column:0,rowCount:2,columnCount:2},{row:1,column:1,rowCount:2,columnCount:2},{row:2,column:0,rowCount:1,columnCount:2}]}]}).blocks[0];
  assert.deepEqual(overlapping.merges,[{row:0,column:0,rowCount:2,columnCount:2},{row:2,column:0,rowCount:1,columnCount:2}]);
  const model=rowCount=>JSON.stringify({blocks:[{type:'table',rows:grid(rowCount,2),merges:Array.from({length:rowCount},(_,row)=>({row,column:0,rowCount:1,columnCount:2}))}]});
  const plain=shippedPages();plain.pages.write(JSON.stringify({blocks:[{type:'table',rows:grid(12,2)}]}));
  const merged=shippedPages();merged.pages.write(model(12));
  assert.equal(merged.checks()-plain.checks(),12,'one cancellation check per merge');
  assert.throws(()=>{const run=shippedPages(merged.checks()-6);run.pages.write(model(12));},/Operation cancelled/);
});
