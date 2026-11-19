// Run with Playwright installed: node scripts/test-web-demo.cjs [base URL]
// A separate, temporary Chrome profile is used; no signed-in browser is touched.
const {chromium}=require('playwright');
const assert=require('node:assert/strict');
const fs=require('node:fs/promises');
const path=require('node:path');
const base=process.argv[2]||'http://127.0.0.1:8771';
const output=process.env.NASHI_WEB_TEST_OUTPUT||'build/web-demo-proof';
const ids=['compress','removeMetadata','editImage','annotateImage','frameImage','cropImage','normalizeAudio','muteVideo','trimVideo','splitPDF','organizePDF','extractArchive','createPDF','mergePDF','cropVideo','changeVideoSpeed','joinVideos','videoSnapshots','splitVideo','audioToVideo','trimAudio','audioChannels','createCollage','redactImage','redactVideo','redactAudio','readQRCode'];
(async()=>{
 await fs.mkdir(output,{recursive:true});
 const browser=await chromium.launch({channel:'chrome',headless:true});
 const page=await browser.newPage({viewport:{width:1440,height:1000},acceptDownloads:true,reducedMotion:'reduce'});
 const failures=[],errors=[],passed=[];
 page.on('pageerror',e=>errors.push(e.message));
 page.on('console',m=>{if(m.type()==='error')errors.push(m.text());});
 const test=async(name,fn)=>{try{await fn();passed.push(name);console.log('PASS',name);}catch(e){failures.push({name,error:e.message});console.log('FAIL',name,e.message);await page.screenshot({path:path.join(output,'failure-'+failures.length+'.png')});await page.locator('.close-editor').click().catch(()=>{});await page.keyboard.press('Escape');}};
 // Open editors through the same sample, selection and tool wheel as visitors.
 const toolSamples={
  pdf:['splitPDF','organizePDF','mergePDF'],
  audio:['normalizeAudio','audioToVideo','trimAudio','audioChannels','redactAudio'],
  video:['muteVideo','trimVideo','cropVideo','changeVideoSpeed','joinVideos','videoSnapshots','splitVideo','redactVideo'],
  archive:['extractArchive']
 };
 const batchTools=new Set(['createPDF','createCollage','mergePDF','joinVideos']);
 const openTool=async id=>{
  const group=Object.keys(toolSamples).find(key=>toolSamples[key].includes(id))??'image';
  await pickSample(group);
  if(batchTools.has(id)){
   const files=page.locator('.demo-file');
   for(let i=1;i<await files.count();i++)await files.nth(i).click({modifiers:['Shift']});
  }
  await page.locator('[data-mode="tools"]').click();
  await page.locator(`.wheel-option[data-option="${id}"]`).click();
  const exportButton=page.locator('dialog .export-button');
  await exportButton.waitFor();
  if(id==='cropImage'){
   await page.locator('dialog .editor-filename').waitFor();
   assert.ok(await exportButton.isDisabled(),'Crop must not save before an area is selected');
  }else await page.waitForFunction(()=>!document.querySelector('dialog .export-button')?.disabled);
 };
 const pickSample=async group=>{
  await page.locator('#demo-samples summary').click();
  await page.locator(`[data-demo-choice="${group}"]`).click();
 };
 const demoAction=async selector=>{
  if(selector==='#demo-reset')await page.locator('#nashi-menu summary').click();
  await page.locator(selector).click();
 };
 const save=async()=>{await page.locator('dialog .export-button').click();await page.locator('dialog').waitFor({state:'detached'});};
 await page.goto(base+'/tr/');await page.locator('.demo-file').first().waitFor();
 await test('Keyboard Enter opens a wheel without applying; arrows and Escape work',async()=>{
  await page.locator('.demo-file').first().press('Enter');assert.equal(await page.locator('.demo-wheel').count(),1);assert.equal(await page.locator('#library-count').textContent(),'0');assert.equal(await page.locator('.wheel-option.active').count(),0);await page.keyboard.press('ArrowRight');assert.equal(await page.locator('.wheel-option.active').getAttribute('data-index'),'0');await page.keyboard.press('ArrowRight');assert.equal(await page.locator('.wheel-option.active').getAttribute('data-index'),'1');await page.keyboard.press('Escape');assert.equal(await page.locator('.demo-wheel').count(),0);
  assert.ok(await page.locator('.demo-file').first().evaluate(file=>file===document.activeElement));
  assert.equal(await page.locator('#features .tool').count(),27);
  assert.equal(await page.locator('#features .tool :is(a,button)').count(),0);
 });
 await test('Real drag + Shift + hover + drop creates a PNG',async()=>{
  await page.locator('#playground').scrollIntoViewIfNeeded();
  const r=await page.locator('.demo-file').first().boundingBox();
  const pointer={x:r.x+r.width/2+10,y:r.y+45};
  await page.mouse.move(pointer.x-10,pointer.y-5);
  await page.mouse.down();
  await page.mouse.move(pointer.x,pointer.y,{steps:5});
  await page.keyboard.press('Shift');
  const box=await page.locator('.demo-wheel[data-pointer-wheel]').boundingBox();
  const anchored=[box.x+box.width/2,box.y+box.height/2];
  assert.ok(Math.abs(anchored[0]-pointer.x)<1&&Math.abs(anchored[1]-pointer.y)<1,String(anchored));
  assert.ok(await page.locator('.demo-wheel').evaluate(wheel=>wheel.closest('.scene')!==null),'The wheel opens over the folder scene');
  const option=page.getByRole('menuitem',{name:'PNG',exact:true});
  const p=await option.boundingBox();
  await page.mouse.move(p.x+p.width/2,p.y+p.height/2,{steps:12});
  await page.mouse.up();
  await page.waitForFunction(()=>Number(document.querySelector('#library-count').textContent)>0);
  assert.equal(await page.locator('.demo-wheel').count(),0);
  await demoAction('#demo-library');const link=page.getByRole('link',{name:'Dosyayı kaydet',exact:true}).first();const url=await link.getAttribute('href');const header=await page.evaluate(async u=>[...new Uint8Array(await(await fetch(u)).arrayBuffer())].slice(0,8),url);assert.deepEqual(header,[137,80,78,71,13,10,26,10]);await page.locator('.close-editor').click();
 });
 await test('Dragging shows a Finder-style drag image that follows the pointer and goes away on drop and cancel',async()=>{
  await pickSample('image');
  const tile=page.locator('.demo-file').first(),r=await tile.boundingBox();
  const name=await tile.getAttribute('aria-label');
  await page.mouse.move(r.x+r.width/2,r.y+30);await page.mouse.down();
  await page.mouse.move(r.x+r.width/2+80,r.y+90,{steps:6});
  const ghost=page.locator('.drag-ghost');
  assert.equal(await ghost.count(),1);
  assert.equal(await ghost.locator('.ghost-name').textContent(),name);
  assert.equal(await tile.evaluate(t=>t.classList.contains('dragging')),true,'the original tile stays dimmed in place');
  const first=await ghost.boundingBox();
  await page.mouse.move(r.x+r.width/2+140,r.y+120,{steps:4});
  const second=await ghost.boundingBox();
  assert.ok(Math.abs(second.x-first.x-60)<2&&Math.abs(second.y-first.y-30)<2,'the drag image follows the pointer');
  await page.keyboard.press('Shift');
  const wheel=await page.locator('.demo-wheel').boundingBox();
  assert.ok(Math.abs(wheel.x+wheel.width/2-(r.x+r.width/2+140))<1&&Math.abs(wheel.y+wheel.height/2-(r.y+120))<1,'the wheel opens around the dragged file');
  await page.mouse.move(r.x+r.width/2+140,r.y+120);
  await page.mouse.up();
  assert.equal(await page.locator('.drag-ghost').count(),0,'drop removes the drag image');
  assert.equal(await page.locator('.demo-file.dragging').count(),0);
  await page.mouse.move(r.x+r.width/2,r.y+30);await page.mouse.down();
  await page.mouse.move(r.x+r.width/2+60,r.y+60,{steps:5});
  assert.equal(await page.locator('.drag-ghost').count(),1);
  await page.keyboard.press('Escape');
  assert.equal(await page.locator('.drag-ghost').count(),0,'Escape cancels the drag image');
  await page.mouse.up();
  assert.equal(await page.locator('.demo-file.dragging').count(),0);
 });
 await test('All 46 sample inputs expose all 276 catalog conversion routes',async()=>{
  let routes=0,inputs=0;
  for(const g of ['image','video','audio','pdf','document','sheet','slides','epub','subtitle','archive']){
   await pickSample(g);
   assert.equal(await page.locator('.demo-file[aria-pressed=true]').getAttribute('data-family'),g);
   const formats=await page.locator('.sample-format-row option').evaluateAll(os=>os.map(o=>o.value));
   for(const ext of formats){
    await page.locator('#demo-samples summary').click();
    await page.getByLabel('Örnek girdi biçimi').selectOption(ext);
    await page.keyboard.press('Escape');
    await page.locator('[data-mode="convert"]').click();
    const options=await page.locator('.wheel-option').evaluateAll(buttons=>buttons.map(button=>button.getAttribute('aria-label').toLowerCase()));
    assert.ok(options.length>0,ext);
    routes+=options.length;
    inputs++;
    await page.keyboard.press('Escape');
   }
  }
  assert.equal(inputs,46);
  assert.equal(routes,276);
 });
 await test('Multiple PDFs use the batch tool wheel including Merge PDFs',async()=>{await pickSample('pdf');await page.locator('.demo-file').nth(1).click({modifiers:['Shift']});await demoAction('[data-mode="tools"]');assert.ok(await page.getByRole('menuitem',{name:"PDF'leri Birleştir",exact:true}).isVisible());const wheel=await page.locator('.demo-wheel').boundingBox(),center=await page.locator('.demo-wheel .wheel-center').boundingBox();assert.ok(Math.abs(center.x+center.width/2-wheel.x-wheel.width/2)<1);assert.ok(Math.abs(center.y+center.height/2-wheel.y-wheel.height/2)<1);assert.ok(!(await page.getByRole('menuitem',{name:"PDF'yi Düzenle",exact:true}).count()));await page.keyboard.press('Escape');});
 await test('Crop drag + Shift exports a square with real pixel dimensions',async()=>{await openTool('cropImage');const canvas=page.locator('dialog canvas'),r=await canvas.boundingBox();await page.mouse.move(r.x+r.width*.04,r.y+r.height*.05);await page.mouse.down();await page.keyboard.down('Shift');await page.mouse.move(r.x+r.width*.35,r.y+r.height*.55,{steps:12});await page.mouse.up();await page.keyboard.up('Shift');const dimensions=await page.locator('.editor-dialog-status').textContent();const nums=dimensions.match(/\d+/g).map(Number);assert.ok(Math.abs(nums[0]-nums[1])<=1,dimensions);await canvas.press('Enter');await page.locator('dialog').waitFor({state:'detached'});await demoAction('#demo-library');const u=await page.locator('.library-item a').first().getAttribute('href');const actual=await page.evaluate(async u=>{const b=await createImageBitmap(await(await fetch(u)).blob());return[b.width,b.height];},u);assert.ok(Math.abs(actual[0]-actual[1])<=1,actual);await page.locator('.close-editor').click();});
 await test('Annotation has exactly one active tool and Undo affects the export',async()=>{await openTool('annotateImage');await page.locator('[data-draw-tool="rectangle"]').click();assert.equal(await page.locator('[data-draw-tool][aria-pressed="true"]').count(),1);const r=await page.locator('dialog canvas').boundingBox();await page.mouse.move(r.x+30,r.y+30);await page.mouse.down();await page.mouse.move(r.x+160,r.y+120,{steps:8});await page.mouse.up();const before=await page.locator('dialog canvas').evaluate(c=>c.toDataURL());await page.getByRole('button',{name:'Son çizimi geri al',exact:true}).click();const after=await page.locator('dialog canvas').evaluate(c=>c.toDataURL());assert.notEqual(before,after);await page.locator('.close-editor').click();});
 await test('Video playback shortcuts, In/Out drag and field synchronization',async()=>{await openTool('trimVideo');const out=page.getByRole('button',{name:'Bitiş tutamacı'}),r=await out.boundingBox(),track=await page.locator('.trim-track').boundingBox();await page.mouse.move(r.x+r.width/2,r.y+r.height/2);await page.mouse.down();await page.mouse.move(track.x+track.width*.7,r.y+r.height/2,{steps:18});await page.mouse.up();const end=Number(await page.getByLabel('Bitiş (saniye)',{exact:true}).inputValue());assert.ok(end>7&&end<10,end);const start=page.getByRole('button',{name:'Başlangıç tutamacı'}),s=await start.boundingBox();await page.mouse.move(s.x+s.width/2,s.y+20);await page.mouse.down();await page.mouse.move(track.x+track.width*.2,s.y+20,{steps:18});await page.mouse.up();assert.ok(Number(await page.getByLabel('Başlangıç (saniye)',{exact:true}).inputValue())>2);await page.locator('dialog video').press('ArrowRight');const t=await page.locator('dialog video').evaluate(v=>v.currentTime);assert.ok(t>=.98&&t<=1.1,t);await page.keyboard.press('Space');await page.waitForFunction(()=>!document.querySelector('dialog video').paused);await page.keyboard.press('Space');assert.equal(await page.locator('dialog video').evaluate(v=>v.paused),true);await page.keyboard.press('o');assert.ok(Number(await page.getByLabel('Bitiş (saniye)',{exact:true}).inputValue())>2);await page.locator('.close-editor').click();});
 for(const id of ids)await test('Tool '+id+' opens, applies settings and records its result',async()=>{const before=Number(await page.locator('#library-count').textContent());await openTool(id);if(id==='cropImage')await page.getByRole('button',{name:'Tümünü Seç',exact:true}).click();await save();assert.ok(Number(await page.locator('#library-count').textContent())>before);});
 await test('PDF output has valid page objects and merge keeps six pages',async()=>{await demoAction('#demo-library');const row=page.locator('.library-item').filter({hasText:'field-notes-mergePDF.pdf'}).first();const u=await row.locator('a').getAttribute('href');const count=await page.evaluate(async u=>{const b=await(await fetch(u)).arrayBuffer();return(await PDFLib.PDFDocument.load(b)).getPageCount();},u);assert.equal(count,6);await page.locator('.close-editor').click();});
 await test('Real audio trim exports WAV with the selected duration',async()=>{await openTool('trimAudio');await page.getByLabel('Başlangıç (saniye)',{exact:true}).fill('2');await page.getByLabel('Bitiş (saniye)',{exact:true}).fill('5');await save();await demoAction('#demo-library');const u=await page.locator('.library-item a').first().getAttribute('href');const result=await page.evaluate(async u=>{const v=new DataView(await(await fetch(u)).arrayBuffer());return{header:String.fromCharCode(...new Uint8Array(v.buffer,0,4)),duration:v.getUint32(40,true)/v.getUint32(28,true)};},u);assert.equal(result.header,'RIFF');assert.equal(result.duration,3);await page.locator('.close-editor').click();});
 await test('Input validation rejects an invalid PDF without creating a result',async()=>{await page.locator('input[type=file]').setInputFiles({name:'broken.pdf',mimeType:'application/pdf',buffer:Buffer.from('broken PDF fixture')});await demoAction('[data-mode="tools"]');await page.getByRole('menuitem',{name:"PDF'yi Böl",exact:true}).click();await page.waitForFunction(()=>document.querySelector('dialog .editor-dialog-status'));assert.equal(await page.locator('dialog .export-button').isEnabled(),false);await page.locator('.close-editor').click();await demoAction('#demo-reset');});
 await test('Instagram Reels omit thumbnails; photo posts offer a carousel',async()=>{await page.locator('#try-download').click();await page.getByLabel('Medya bağlantısı').fill('https://www.instagram.com/reel/example/');assert.equal(await page.locator('dialog .annotation-bar button').count(),2);assert.ok(!(await page.getByRole('button',{name:'Thumbnail',exact:true}).count()));await page.getByLabel('Medya bağlantısı').fill('https://www.instagram.com/p/example/');assert.equal(await page.locator('dialog .annotation-bar button').count(),1);assert.ok(await page.getByRole('button',{name:'Carousel görselleri',exact:true}).isVisible());await page.locator('.close-editor').click();});
 await test('Web media preview ignores provider names in unrelated URL text',async()=>{
  await page.locator('#try-download').click();
  const link=page.getByLabel('Medya bağlantısı');
  for(const url of [
    'https://notyoutube.com/watch?v=demo',
    'https://example.net/?next=https://youtube.com/watch?v=demo',
    'https://www.instagram.com.evil.example/reel/demo',
    'javascript:alert("youtube.com")'
  ]){
    await link.fill(url);
    assert.equal(await page.locator('dialog .annotation-bar button').count(),1,url);
    assert.ok(await page.getByRole('button',{name:'Orijinal medya',exact:true}).isVisible(),url);
  }
  await link.fill('https://www.youtube.com/watch?v=demo');
  assert.equal(await page.locator('dialog .annotation-bar button').count(),3);
  assert.ok(await page.getByRole('button',{name:'Thumbnail',exact:true}).isVisible());
  await page.locator('.close-editor').click();
 });
 await test('All six file actions open the right preview or shelf',async()=>{await pickSample('image');for(const id of ['airdrop','copy','move','terminal']){await demoAction('[data-mode="actions"]');const names={airdrop:/^(AirDrop|Paylaş)$/i,copy:'Kopyala',move:'Taşı',terminal:'Terminal'};await page.getByRole('menuitem',{name:names[id],exact:true}).click();assert.equal(await page.locator('dialog').count(),1);await page.locator('.close-editor').click();}await demoAction('[data-mode="actions"]');await page.getByRole('menuitem',{name:'Yüzen Pencere',exact:true}).click();assert.equal(await page.locator('.float-shelf').count(),1);});
 await test('Mixed sample folder supports navigation, search, list view and the Crop shortcut',async()=>{
  await page.goto(base+'/');await page.locator('.demo-file').first().waitFor();
  assert.equal(await page.locator('.demo-file').count(),10);
  assert.equal(await page.locator('#folder-count').textContent(),'1 of 10 selected');
  const families=await page.locator('.demo-file').evaluateAll(files=>new Set(files.map(file=>file.dataset.family)).size);
  assert.equal(families,8);
  assert.equal(await page.locator('.nashi-hud,#demo-convert,#format-chips').count(),0,'Nashi has no panel of its own');
  for(const name of ['Formats (Shift)','Tools (Command)','Actions (Control)'])assert.ok(await page.getByRole('button',{name,exact:true}).isVisible(),name);
  await page.getByRole('button',{name:'List view',exact:true}).click();
  assert.equal(await page.getByRole('button',{name:'List view',exact:true}).getAttribute('aria-pressed'),'true');
  await page.getByRole('searchbox',{name:'Search folder'}).fill('BUDGET');
  assert.equal(await page.locator('.demo-file:visible').count(),1);
  await page.getByRole('button',{name:'budget.xlsx',exact:true}).click();
  assert.equal(await page.getByRole('button',{name:'budget.xlsx',exact:true}).getAttribute('aria-pressed'),'true');
  await page.getByRole('button',{name:'Documents',exact:true}).click();
  assert.equal(await page.locator('#folder-title').textContent(),'Documents');
  assert.equal(await page.locator('.demo-file:visible').count(),4);
  await page.getByRole('button',{name:'Back',exact:true}).click();
  assert.equal(await page.locator('#folder-title').textContent(),'Sample files');
  await page.getByRole('button',{name:'Forward',exact:true}).click();
  assert.equal(await page.locator('#folder-title').textContent(),'Documents');
  await page.getByRole('button',{name:'Sample files',exact:true}).click();
  await page.getByRole('button',{name:'Icon view',exact:true}).click();
  await page.getByRole('button',{name:'Tools (Command)',exact:true}).click();
  await page.getByRole('menuitem',{name:'Crop Image',exact:true}).click();
  await page.locator('dialog canvas').waitFor();
  await page.locator('.close-editor').click();
  await page.getByRole('button',{name:'trip.mov',exact:true}).dblclick();
  await page.getByRole('menu',{name:'Output formats'}).waitFor();
  await page.getByRole('menuitem',{name:'MP4',exact:true}).click();
  assert.equal(await page.locator('dialog h2').textContent(),'Convert to MP4');
  await page.locator('.close-editor').click();
 });
 // Switching folders or views must never resize the desktop and move the page.
 const sceneHeight=()=>page.locator('.scene').evaluate(scene=>scene.getBoundingClientRect().height);
 await test('Folder and view switches keep the desktop the same height (1440px and 390px)',async()=>{
  await page.goto(base+'/');await page.locator('.demo-file').first().waitFor();
  const start=await sceneHeight(),stageHeight=await page.locator('.hero-stage').evaluate(e=>e.getBoundingClientRect().height);
  for(const name of ['Desktop','Documents','Downloads','Sample files']){
   await page.getByRole('button',{name,exact:true}).click();
   assert.equal(await sceneHeight(),start,name);
  }
  for(const name of ['List view','Icon view']){
   await page.getByRole('button',{name,exact:true}).click();
   assert.equal(await sceneHeight(),start,name);
  }
  assert.equal(await page.locator('.hero-stage').evaluate(e=>e.getBoundingClientRect().height),stageHeight);
  await page.setViewportSize({width:390,height:844});
  const mobile=await sceneHeight();
  for(const name of ['Documents','Downloads','Desktop','Sample files']){
   await page.locator('.folder-sidebar summary').click();
   await page.getByRole('button',{name,exact:true}).click();
   assert.equal(await sceneHeight(),mobile,'390px '+name);
  }
  await page.getByRole('button',{name:'List view',exact:true}).click();
  assert.equal(await sceneHeight(),mobile,'390px list view');
  await page.getByRole('button',{name:'Icon view',exact:true}).click();
  await page.setViewportSize({width:1440,height:1000});
 });
 await test('The Shift wheel saves a real PNG next to the original and offers it in the toast',async()=>{
  await page.goto(base+'/');await page.locator('.demo-file').first().waitFor();
  await page.getByRole('button',{name:'Formats (Shift)',exact:true}).click();
  const pngSlice=page.getByRole('menuitem',{name:'PNG',exact:true});
  const pngKey=await pngSlice.getAttribute('aria-keyshortcuts');
  assert.match(pngKey,/^[1-9]$/);
  await page.keyboard.press(pngKey);
  assert.equal(await page.locator('.demo-wheel').count(),0);
  await page.locator('.demo-toast a[download="orchard.png"]').waitFor();
  assert.match(await page.locator('.receipt-size .sr-only').textContent(),/^Before: .+ KB\. After: .+ KB\.$/);
  const fresh=page.getByRole('button',{name:'orchard.png',exact:true});
  assert.ok(await fresh.isVisible(),'the copy appears in the folder');
  assert.equal(await page.getByRole('button',{name:'orchard.jpg',exact:true}).getAttribute('aria-pressed'),'true');
  assert.equal(await page.locator('#library-count').textContent(),'1');
  assert.equal(await page.locator('#folder-count').textContent(),'1 of 11 selected');
  const header=await page.evaluate(async u=>[...new Uint8Array(await(await fetch(u)).arrayBuffer())].slice(0,8),await page.locator('.demo-toast a').getAttribute('href'));
  assert.deepEqual(header,[137,80,78,71,13,10,26,10]);
  await page.getByRole('button',{name:'orchard.jpg',exact:true}).click();
  await page.getByRole('button',{name:'Formats (Shift)',exact:true}).click();
  await page.getByRole('menuitem',{name:'PNG',exact:true}).click();
  await page.getByRole('button',{name:'orchard 2.png',exact:true}).waitFor();
  assert.equal(await page.locator('html').getAttribute('lang'),'en');
 });
 await test('Show me plays the wheel gesture and any key hands control back',async()=>{
  await page.getByRole('button',{name:'Show me',exact:true}).click();
  await page.locator('.tour-layer .demo-wheel').waitFor();
  assert.equal(await page.locator('.keycap[data-mode="convert"].pressed').count(),1);
  assert.equal(await page.locator('.tour-layer [role=menuitem]').count(),0,'the tour wheel is not a second menu');
  await page.keyboard.press('Escape');
  assert.equal(await page.locator('.tour-layer').count(),0);
  assert.equal(await page.locator('.keycap.pressed').count(),0);
  await page.keyboard.down('Shift');
  assert.equal(await page.locator('.keycap[data-mode="convert"].pressed').count(),1,'a held key lights its keycap');
  await page.keyboard.up('Shift');
 });
 await test('The Nashi menu bar item opens Recent Conversions and the appearance switch',async()=>{
  await page.locator('#nashi-menu summary').click();
  await page.getByRole('button',{name:'Recent Conversions',exact:true}).click();
  assert.ok(await page.locator('dialog .library-item').count()>=1);
  await page.locator('.close-editor').click();
  await page.locator('#nashi-menu summary').click();
  await page.locator('#demo-appearance').click();
  assert.equal(await page.locator('html').getAttribute('data-demo-theme'),'dark');
  await page.locator('#demo-appearance').click();
  await page.keyboard.press('Escape');
 });
 await page.goto(base+'/tr/');await page.locator('.demo-file').first().waitFor();await page.screenshot({path:path.join(output,'desktop-hero.png')});await page.locator('#playground').scrollIntoViewIfNeeded();await demoAction('[data-mode="tools"]');await page.screenshot({path:path.join(output,'desktop-wheel.png')});await page.keyboard.press('Escape');
 await test('390px viewport fits; a key opens the wheel over the folder and converts',async()=>{
  await page.setViewportSize({width:390,height:844});
  await pickSample('image');
  assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
  await page.locator('.demo-file').nth(1).click();
  await page.locator('[data-mode="convert"]').click();
  const r=await page.locator('.demo-wheel').boundingBox();
  assert.ok(r.x>=0&&r.x+r.width<=390,String(r.x));
  const scene=await page.locator('.scene').boundingBox();
  const hint=await page.locator('.wheel-hint').boundingBox();
  assert.ok(r.y+r.height<=hint.y&&hint.x>=scene.x&&hint.x+hint.width<=scene.x+scene.width);
  for(const key of await page.locator('.keycap').all()){const k=await key.boundingBox();assert.ok(k.height>=44&&k.width>=44);}
  await page.getByRole('menuitem',{name:'JPG',exact:true}).click();
  await page.waitForFunction(()=>Number(document.querySelector('#library-count').textContent)>0);
  await page.screenshot({path:path.join(output,'mobile-playground.png')});
  await openTool('cropImage');
  assert.ok(await page.evaluate(()=>document.querySelector('dialog').scrollWidth<=innerWidth));
  await page.screenshot({path:path.join(output,'mobile-crop.png')});
  await page.locator('.close-editor').click();
 });
 await test('Reset clears Library, Float pins and returns to sample files',async()=>{
  await pickSample('image');
  await page.locator('[data-mode="convert"]').click();
  await page.getByRole('menuitem',{name:'PNG',exact:true}).click();
  await page.waitForFunction(()=>Number(document.querySelector('#library-count').textContent)>0);
  await demoAction('[data-mode="actions"]');
  await page.getByRole('menuitem',{name:'Yüzen Pencere',exact:true}).click();
  assert.equal(await page.locator('.float-shelf').count(),1);
  await demoAction('#demo-reset');
  assert.equal(await page.locator('#library-count').textContent(),'0');
  assert.equal(await page.locator('.float-shelf').count(),0);
  assert.equal(await page.locator('.demo-file').count(),10);
 });
 await test('Keyboard and modifier wheel actions preserve multi-selection',async()=>{
  await pickSample('image');
  await page.locator('.demo-file').nth(1).click({modifiers:['Shift']});
  assert.equal(await page.locator('.demo-file[aria-pressed="true"]').count(),2);
  await page.locator('.demo-file').first().focus();
  await page.keyboard.press('Enter');
  assert.equal(await page.locator('.demo-file[aria-pressed="true"]').count(),2);
  await page.keyboard.press('Escape');
  await page.locator('.demo-file').first().click({modifiers:['Control']});
  assert.equal(await page.locator('.demo-file[aria-pressed="true"]').count(),2);
  await page.keyboard.press('Escape');
 });
 await test('Clicking one selected file collapses Shift multi-selection',async()=>{
  await pickSample('image');
  await page.locator('.demo-file').nth(1).click({modifiers:['Shift']});
  assert.equal(await page.locator('.demo-file[aria-pressed="true"]').count(),2);
  await page.locator('.demo-file').first().click();
  assert.equal(await page.locator('.demo-file[aria-pressed="true"]').count(),1);
  assert.equal(await page.locator('.demo-file').first().getAttribute('aria-pressed'),'true');
 });
 await test('Keyboard Enter on a file applies the slice chosen with the arrows',async()=>{
  await pickSample('pdf');
  await page.locator('.demo-file').first().focus();
  await page.keyboard.press('Enter');
  await page.keyboard.press('ArrowRight');
  assert.equal(await page.locator('.wheel-option.active').getAttribute('data-index'),'0');
  await page.keyboard.press('Enter');
  assert.equal(await page.locator('.demo-wheel').count(),0);
  assert.equal(await page.locator('dialog').count(),1);
  await page.locator('.close-editor').click();
  await pickSample('image');
 });
 await test('Enter and Space on a focused editor button activate it instead of saving or playing',async()=>{
  const before=await page.locator('#library-count').textContent();
  await openTool('cropImage');
  await page.getByRole('button',{name:'Tümünü Seç',exact:true}).click();
  await page.getByRole('button',{name:'Seçimi Temizle',exact:true}).press('Enter');
  assert.equal(await page.locator('dialog').count(),1);
  assert.ok(await page.locator('dialog .export-button').isDisabled());
  await page.getByRole('button',{name:'Tümünü Seç',exact:true}).click();
  await page.locator('.close-editor').press('Enter');
  await page.locator('dialog').waitFor({state:'detached'});
  assert.equal(await page.locator('#library-count').textContent(),before);
  await openTool('trimVideo');
  await page.locator('.close-editor').press('Space');
  await page.locator('dialog').waitFor({state:'detached'});
  assert.equal(await page.locator('#library-count').textContent(),before);
 });
 await test('A closed image editor that finishes decoding late leaves the next editor alone',async()=>{
  await pickSample('image');
  await page.locator('.demo-file').first().click();
  // Hold every image decode until the test releases it, so the closed editor finishes last.
  await page.evaluate(()=>{const Real=window.Image;window.__realImage=Real;window.__heldDecodes=[];window.Image=function(){const image=new Real();let onload=null;Object.defineProperty(image,'onload',{get:()=>onload,set:f=>{onload=f;}});image.addEventListener('load',()=>window.__heldDecodes.push(()=>onload?.()));return image;};});
  const held=n=>page.waitForFunction(n=>window.__heldDecodes.length>=n,n);
  const release=i=>page.evaluate(i=>window.__heldDecodes.splice(i,1)[0](),i);
  try{
   await demoAction('[data-mode="tools"]');await page.getByRole('menuitem',{name:'Arka Plan Ekle',exact:true}).click();
   await held(1);await release(0);await held(1);
   await page.locator('.close-editor').click();
   await page.locator('.demo-file').nth(1).click({modifiers:['Shift']});
   await demoAction('[data-mode="tools"]');await page.getByRole('menuitem',{name:'Kolaj Oluştur',exact:true}).click();
   await held(2);await release(1);await held(3);await release(1);await release(1);
   await page.waitForFunction(()=>document.querySelector('dialog .editor-filename'));
   await release(0);
   assert.equal(await page.locator('dialog input[type=color]').inputValue(),'#e6dfc8');
  }finally{await page.evaluate(()=>{window.Image=window.__realImage;});}
  await save();
  await demoAction('#demo-library');
  const u=await page.locator('.library-item').filter({hasText:'orchard-createCollage.png'}).first().locator('a').getAttribute('href');
  const pixel=await page.evaluate(async u=>{const bitmap=await createImageBitmap(await(await fetch(u)).blob()),c=document.createElement('canvas');c.width=bitmap.width;c.height=bitmap.height;const ctx=c.getContext('2d');ctx.drawImage(bitmap,0,0);return[...ctx.getImageData(1,1,1,1).data.slice(0,3)];},u);
  assert.deepEqual(pixel,[0xe6,0xdf,0xc8],'the collage background must stay the collage editor\'s own colour');
  await page.locator('.close-editor').click();
 });
 if(errors.length)failures.push({name:'No browser errors',error:errors.join('\n')});
 const report={url:base,passed:passed.length,checks:passed,failures,consoleErrors:errors};await fs.writeFile(path.join(output,'report.json'),JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify({passed:passed.length,failed:failures.length,errors:errors.length}));await browser.close();if(failures.length)process.exitCode=1;
})().catch(e=>{console.error(e);process.exitCode=1;});
