import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

const src=fs.readFileSync(new URL('../docs/assets/playground.js',import.meta.url),'utf8');
const start=src.indexOf('function canvasBlob(');
let depth=0,i=src.indexOf('{',start);
for(;i<src.length;i++){if(src[i]==='{')depth++;else if(src[i]==='}'&&--depth===0)break;}
const canvasBlob=new Function('L','document',src.slice(start,i+1)+';return canvasBlob;');

class Canvas{
  constructor(){this.width=0;this.height=0;this.px=[0,0,0,0];this.fillStyle='#000';}
  getContext(){return this;}
  fillRect(){this.px=this.fillStyle==='#fff'?[255,255,255,255]:[0,0,0,255];}
  drawImage(img){if(img.px[3]===255)this.px=img.px;}
  toBlob(cb,type){const p=this.px;cb({type,px:type==='image/jpeg'?p.slice(0,3).map(v=>Math.round(v*p[3]/255)):p});}
}
const document={createElement:()=>new Canvas()};
const load=()=>canvasBlob(a=>a,document);

test('a transparent image exported as JPEG is flattened onto white, like the apps',async()=>{
  const c=new Canvas();c.width=4;c.height=4;
  const blob=await load()(c,'image/jpeg',.9);
  assert.deepEqual(blob.px,[255,255,255]);
});

test('PNG export keeps transparency',async()=>{
  const c=new Canvas();c.width=4;c.height=4;
  const blob=await load()(c,'image/png');
  assert.deepEqual(blob.px,[0,0,0,0]);
});
