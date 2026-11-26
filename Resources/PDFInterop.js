/* Independent Nashi PDF object-graph adapter over the MIT pdf-lib 1.17.1.
   No PDF JavaScript, remote references, subprocesses or original-app code run here. */
(function (global) {
"use strict";
const {PDFDocument,PDFName,PDFDict,PDFArray,PDFRef,PDFStream,PDFPage,PDFObjectCopier,PDFString,PDFHexString,PDFBool,degrees}=PDFLib;
const name=PDFName.of;
function check() { if (typeof _nashiPDFCancelled==="function" && _nashiPDFCancelled()) throw new Error("The operation was cancelled."); }
function unsupported(feature) { throw new Error("This PDF contains "+feature+". This page operation cannot safely preserve that feature. No copy was exported."); }
function fieldTree(doc) {
 const form=doc.catalog.lookupMaybe(name("AcroForm"),PDFDict),all=new Set(),refs=new Set(),visiting=new Set();
 if(!form)return {all,refs};
 function visit(ref,depth) {
  if(depth>128)unsupported("an unusually deep form field tree");
  const dict=doc.context.lookup(ref,PDFDict);if(visiting.has(dict))unsupported("a cyclic form field tree");
  if(all.has(dict))return;visiting.add(dict);all.add(dict);if(ref instanceof PDFRef)refs.add(ref.toString());
  const kids=dict.lookupMaybe(name("Kids"),PDFArray);if(kids)for(const child of kids.asArray())visit(child,depth+1);
  visiting.delete(dict);
 }
 const fields=form.lookupMaybe(name("Fields"),PDFArray);if(fields)for(const ref of fields.asArray())visit(ref,0);
 return {all,refs};
}
function preflight(doc,selection,willNamespace) {
 check();
 for(const feature of ["StructTreeRoot","OCProperties","Collection","Perms"])if(doc.catalog.has(name(feature)))unsupported({StructTreeRoot:"accessibility tags",OCProperties:"optional content layers",Collection:"a portfolio collection",Perms:"document signatures"}[feature]);
 if(doc.isEncrypted)unsupported("encryption");
 if(doc.catalog.lookupMaybe(name("MarkInfo"),PDFDict)?.lookupMaybe(name("Marked"),PDFBool)?.asBoolean())unsupported("accessibility tags");
 if(doc.catalog.has(name("PageLabels"))&&(selection.length!==doc.getPageCount()||selection.some((value,index)=>value!==index)))unsupported("custom page labels while changing page order");
 const form=doc.catalog.lookupMaybe(name("AcroForm"),PDFDict);
 if(form) {
  if(form.has(name("XFA")))unsupported("XFA forms");
  if(form.has(name("CO")))unsupported("calculated form fields");
  if(form.lookupMaybe(name("NeedAppearances"),PDFBool)?.asBoolean())unsupported("form appearances that still need regeneration");
  const dr=form.lookupMaybe(name("DR"),PDFDict);
  if(willNamespace&&dr)for(const key of dr.keys())if(!["/Font","/Encoding","/ProcSet"].includes(key.toString()))unsupported("non-font form appearance resources");
  const tree=fieldTree(doc);if(willNamespace)for(const field of tree.all)if(field.has(name('A')))unsupported('form submission or button actions while copying fields');
 }
 // Signature dictionaries may be present outside the form registry.
 for(const [,obj]of doc.context.enumerateIndirectObjects())if(obj instanceof PDFDict) {
  if(obj.has(name("ByteRange"))||obj.get(name("FT"))===name("Sig")||obj.get(name("Type"))===name("Sig"))unsupported("digital signatures");
  if(obj.get(name("Type"))===name("OCG")||obj.get(name("Type"))===name("OCMD"))unsupported("optional content layers");
  if(willNamespace&&obj.has(name("AA")))unsupported("form actions that reference field names");
 }
 const reduced=new Set(selection).size<doc.getPageCount();
 if(reduced) {
  const names=doc.catalog.lookupMaybe(name("Names"),PDFDict);
  if(doc.catalog.has(name("Outlines"))||doc.catalog.has(name("Dests"))||names?.has(name("Dests")))unsupported("bookmarks or named destinations while removing pages");
  if(doc.catalog.has(name("OpenAction"))||doc.catalog.has(name("AA")))unsupported("document actions while removing pages");
 }
 // Cross-document catalog structures need deliberate remapping rather than page copying.
 if(willNamespace) {
  const names=doc.catalog.lookupMaybe(name("Names"),PDFDict);
  if(doc.catalog.has(name("Outlines"))||doc.catalog.has(name("Dests")))unsupported("bookmarks or destinations while copying pages between documents");
  if(names)for(const key of names.keys())if(key!==name("EmbeddedFiles"))unsupported("document name trees while copying pages between documents");
 }
}
function validate(doc) {
 check();
 const registry=fieldTree(doc),pageWidgets=new Set(),fieldNames=new Set();
 if(doc.catalog.has(name("AcroForm")))for(const field of doc.getForm().getFields()) {
  const key=field.getName();if(fieldNames.has(key))unsupported("ambiguous duplicate form field names");fieldNames.add(key);
 }
 for(const page of doc.getPages()) {
  const annots=page.node.lookupMaybe(name("Annots"),PDFArray);
  if(annots)for(const ref of annots.asArray()) {
   const dict=doc.context.lookup(ref,PDFDict);
   if(dict.get(name("Subtype"))===name("Widget")) {
    if(!registry.all.has(dict))throw new Error("A form widget would lose its field registry. No copy was exported.");
    const p=dict.get(name("P"));if(p&&p.toString()!==page.ref.toString())throw new Error("A form widget points to another page. No copy was exported.");
    pageWidgets.add(dict);
   }
  }
 }
 for(const dict of registry.all)if(dict.get(name("Subtype"))===name("Widget")&&!pageWidgets.has(dict))throw new Error("A form field would point to a removed page. No copy was exported.");
}
function validateNoRemovedPages(doc) {
 const retained=pageRefs(doc);
 for(const [ref,obj]of doc.context.enumerateIndirectObjects()) {
  if(obj instanceof PDFDict&&obj.get(name('Type'))===name('Page')&&!retained.has(ref.toString()))unsupported('annotation or document references to removed pages');
 }
}
function prunePageLinks(doc) {
 const retained=pageRefs(doc);
 for(const page of doc.getPages()) {
  const annots=page.node.lookupMaybe(name("Annots"),PDFArray);if(!annots)continue;
  for(let i=annots.size()-1;i>=0;i--) {
   const dict=doc.context.lookup(annots.get(i),PDFDict),action=dict.lookupMaybe(name("A"),PDFDict);
   let target=dict.get(name("Dest"));if(action?.get(name("S"))===name("GoTo"))target=action.get(name("D"));
   if(!target)continue;
   const resolved=doc.context.lookup(target);
   if(resolved instanceof PDFString||resolved instanceof PDFHexString||resolved instanceof PDFName)unsupported("named page links while removing pages");
   if(resolved instanceof PDFArray) {
    const first=resolved.get(0);
    if(first instanceof PDFRef&&!retained.has(first.toString())) {
     if(dict.get(name("Subtype"))===name("Link"))annots.remove(i);
     else {dict.delete(name("Dest"));if(action?.get(name("S"))===name("GoTo"))dict.delete(name("A"));}
    }
   }
  }
 }
}
function freshPrefix(doc,base) {
 const form=doc.catalog.lookupMaybe(name("AcroForm"),PDFDict),used=new Set();
 const fields=form?.lookupMaybe(name("Fields"),PDFArray);
 if(fields)for(const ref of fields.asArray()){const t=doc.context.lookup(ref,PDFDict).lookupMaybe(name("T"),PDFString,PDFHexString);if(t)used.add(t.decodeText());}
 let candidate=base,n=2;while(used.has(candidate))candidate=base+" "+n++;
 return candidate;
}
function pageRefs(doc){return new Set(doc.getPages().map(p=>p.ref.toString()));}
function pruneForms(doc){
 // removePage in 1.17.1 does not invalidate pageCache; rebuild it before reading retained pages.
 doc.pageCache.invalidate();
 if(!doc.catalog.has(name('AcroForm')))return [];
 const refSet=pageRefs(doc),form=doc.getForm(),record=[],widgetPages=new Map();
 for(const p of doc.getPages()){const annots=p.node.lookupMaybe(name('Annots'),PDFArray);if(annots)for(const ref of annots.asArray())widgetPages.set(doc.context.lookup(ref),p.ref);}
 for(const field of form.getFields()){
  const widgets=field.acroField.getWidgets();
  if(!widgets.some(w=>w.dict.get(name('Subtype'))===name('Widget')))continue;
  const remove=widgets.map((w,i)=>({i,p:widgetPages.get(w.dict)})).filter(w=>!w.p).map(x=>x.i);
  for(const w of widgets){const p=widgetPages.get(w.dict);if(p)w.setP(p);}
  if(remove.length===widgets.length){form.acroForm.removeField(field.acroField);doc.context.delete(field.ref);record.push({field:field.getName(),removed:'field'});}
  else {
   // A button's /Opt holds one export value per widget in /Kids order; remove the value with its widget.
   const opt=field instanceof PDFLib.PDFRadioGroup||field instanceof PDFLib.PDFCheckBox?field.acroField.dict.lookup(name('Opt')):undefined,aligned=opt instanceof PDFArray&&opt.size()===widgets.length;
   for(const i of remove.reverse()){field.acroField.removeWidget(i);if(aligned)opt.remove(i);}
   record.push({field:field.getName(),removedWidgets:remove.length});
  }
 }
 return record;
}
function gc(doc){
 const seenObj=new Set(),reachable=new Set();
 function visit(obj){
  if(!obj)return;
  if(obj instanceof PDFRef){const key=obj.toString();if(reachable.has(key))return;reachable.add(key);visit(doc.context.lookup(obj));return;}
  if(seenObj.has(obj))return;seenObj.add(obj);
  if(obj instanceof PDFDict){for(const [,v]of obj.entries())visit(v);}
  else if(obj instanceof PDFArray){for(const v of obj.asArray())visit(v);}
  else if(obj instanceof PDFStream){visit(obj.dict);}
 }
 for(const v of Object.values(doc.context.trailerInfo))visit(v);
 let deleted=0;for(const [ref]of doc.context.enumerateIndirectObjects())if(!reachable.has(ref.toString())){doc.context.delete(ref);deleted++;}
 return{reachable:reachable.size,deleted};
}
function attachPairs(doc){
 const names=doc.catalog.lookupMaybe(name('Names'),PDFDict),root=names?.lookupMaybe(name('EmbeddedFiles'),PDFDict),pairs=[],visited=new Set();
 function visit(d,depth=0){if(!d)return;if(depth>128||visited.has(d)||visited.size>10000)unsupported('an invalid embedded-file name tree');visited.add(d);let arr=d.lookupMaybe(name('Names'),PDFArray);if(arr){if(arr.size()%2)unsupported('an invalid embedded-file name tree');for(let i=0;i<arr.size();i+=2)pairs.push([arr.get(i),arr.get(i+1)]);}let kids=d.lookupMaybe(name('Kids'),PDFArray);if(kids)for(const kid of kids.asArray())visit(doc.context.lookup(kid,PDFDict),depth+1);}
 visit(root);return pairs;
}
function importFormResources(dest,dict,prefix){
 const aliases=new Map(),sourceDR=dict.lookupMaybe(name('DR'),PDFDict);
 if(sourceDR){
  const fonts=sourceDR.lookupMaybe(name('Font'),PDFDict);
  if(fonts){
   let destForm=dest.catalog.lookupMaybe(name('AcroForm'),PDFDict);if(!destForm){destForm=dest.context.obj({Fields:[]});dest.catalog.set(name('AcroForm'),destForm);}
   let dr=destForm.lookupMaybe(name('DR'),PDFDict);if(!dr){dr=dest.context.obj({});destForm.set(name('DR'),dr);}
   let df=dr.lookupMaybe(name('Font'),PDFDict);if(!df){df=dest.context.obj({});dr.set(name('Font'),df);}
   for(const[k,v]of fonts.entries()){let alias=name(prefix+'_'+k.decodeText());let i=1;while(df.has(alias))alias=name(prefix+'_'+(++i)+'_'+k.decodeText());df.set(alias,v);aliases.set(k.toString(),alias.toString());}
   const encodings=sourceDR.lookupMaybe(name('Encoding'),PDFDict);
   if(encodings){let target=dr.lookupMaybe(name('Encoding'),PDFDict);if(!target){target=dest.context.obj({});dr.set(name('Encoding'),target);}const mapped=new Map();for(const[k,v]of encodings.entries()){let alias=name(prefix+'_'+k.decodeText()),i=2;while(target.has(alias))alias=name(prefix+'_'+i++ +'_'+k.decodeText());target.set(alias,v);mapped.set(k,alias);}for(const[,v]of fonts.entries()){const font=dest.context.lookup(v,PDFDict),encoding=font.get(name('Encoding'));if(mapped.has(encoding))font.set(name('Encoding'),mapped.get(encoding));}}
  }
 }
 const visited=new Set();
 function rewrite(node){if(visited.has(node))return;visited.add(node);const da=node.lookupMaybe(name('DA'),PDFString,PDFHexString);if(da){const text=da.decodeText().replace(/\/[^\s<>{}()[\]\/\%]+/g,k=>aliases.get(k)||k);node.set(name('DA'),PDFString.of(text));}const kids=node.lookupMaybe(name('Kids'),PDFArray);if(kids)for(const kid of kids.asArray())rewrite(dest.context.lookup(kid,PDFDict));}
 rewrite(dict);const rootFields=dict.lookupMaybe(name('Fields'),PDFArray);if(rootFields)for(const ref of rootFields.asArray())rewrite(dest.context.lookup(ref,PDFDict));return dict.get(name('DA'));
}
function importRich(dest,src,prefix,includeAttachments=true){
 const copier=PDFObjectCopier.for(src.context,dest.context),pages=[];
 for(const p of src.getPages()){
  const ref=copier.copy(p.ref),leaf=dest.context.lookup(ref);
  // The copier keeps the attributes a page inherits; one it leaves at its default must
  // not pick up the destination's page-tree values (/Rotate 0, CropBox = MediaBox).
  if(!leaf.has(name('Rotate')))leaf.set(name('Rotate'),dest.context.obj(0));
  if(!leaf.has(name('CropBox'))&&leaf.has(name('MediaBox')))leaf.set(name('CropBox'),leaf.get(name('MediaBox')));
  const copy=PDFPage.of(leaf,ref,dest);dest.addPage(copy);pages.push(copy);
 }
 const sourceForm=src.catalog.get(name('AcroForm'));
 if(sourceForm){
  const copied=copier.copy(sourceForm),dict=dest.context.lookup(copied,PDFDict),fields=dict.lookup(name('Fields'),PDFArray);
  const da=importFormResources(dest,dict,prefix);
  if(!dest.catalog.has(name('AcroForm')))dest.catalog.set(name('AcroForm'),dest.context.obj({Fields:[]}));
  const group=dest.context.obj({T:PDFHexString.fromText(prefix),Kids:fields}),groupRef=dest.context.register(group);if(da)group.set(name('DA'),da);
  // A field without /Q would otherwise inherit the destination form's default alignment.
  group.set(name('Q'),dict.get(name('Q'))||dest.context.obj(0));
  for(const ref of fields.asArray())dest.context.lookup(ref,PDFDict).set(name('Parent'),groupRef);
  dest.catalog.lookup(name('AcroForm'),PDFDict).lookup(name('Fields'),PDFArray).push(groupRef);
 }
 const sourcePairs=attachPairs(src);
 if(includeAttachments&&sourcePairs.length){
  let names=dest.catalog.lookupMaybe(name('Names'),PDFDict);if(!names){names=dest.context.obj({});dest.catalog.set(name('Names'),names);}
  const previous=attachPairs(dest),existing=dest.context.obj(previous.flat()),used=new Set(previous.map(x=>x[0].decodeText()));
  names.set(name('EmbeddedFiles'),dest.context.obj({Names:existing}));
  for(const [n,v]of sourcePairs){let unique=prefix+'.'+n.decodeText(),index=2;while(used.has(unique))unique=prefix+'.'+index++ +'.'+n.decodeText();used.add(unique);const copied=copier.copy(v),spec=dest.context.lookup(copied,PDFDict);spec.set(name('F'),PDFHexString.fromText(unique));spec.set(name('UF'),PDFHexString.fromText(unique));existing.push(PDFHexString.fromText(unique));existing.push(copied);}
 }
 const sourceAF=src.catalog.lookupMaybe(name('AF'),PDFArray);
 if(includeAttachments&&sourceAF){let af=dest.catalog.lookupMaybe(name('AF'),PDFArray);if(!af){af=dest.context.obj([]);dest.catalog.set(name('AF'),af);}for(const ref of sourceAF.asArray())af.push(copier.copy(ref));}
 return pages;
}

function sortAttachmentNames(doc) {
 const pairs=attachPairs(doc);if(!pairs.length)return;
 const dictionary=doc.catalog.lookup(name("Names"),PDFDict);
 pairs.sort((a,b)=>{const x=PDFHexString.fromText(a[0].decodeText()).toString(),y=PDFHexString.fromText(b[0].decodeText()).toString();return x<y?-1:x>y?1:0;});
 const values=[];for(const [key,value]of pairs){values.push(PDFHexString.fromText(key.decodeText()));values.push(value);}
 dictionary.set(name("EmbeddedFiles"),doc.context.obj({Names:values}));
}
async function load(base64) {check();let d;try{d=await PDFDocument.load(base64,{parseSpeed:Infinity,updateMetadata:false,throwOnInvalidObject:true});}catch(error){if(error instanceof PDFLib.EncryptedPDFError||String(error.message||error).startsWith('Input document to `PDFDocument.load` is encrypted.'))unsupported("encryption");throw error;}check();return d;}
function setPageOrder(doc,pages) {
 // Re-added pages hang off the root page-tree node, and emptied intermediate
 // nodes are pruned. Keep what each page inherited from them, as pdf-lib's copier does.
 for(const page of pages)for(const key of ["Resources","MediaBox","CropBox","Rotate"]){const value=page.node.getInheritableAttribute(name(key));if(value&&!page.node.get(name(key)))page.node.set(name(key),value);}
 while(doc.getPageCount()){check();doc.removePage(0);}
 for(const page of pages){check();doc.addPage(page);}
}
async function selectDocument(bytes,selection) {
 const doc=await load(bytes),sourcePages=doc.getPages(),seen=new Set(),chosen=[];
 const duplicates=[];
 for(const index of selection){if(!seen.has(index)){seen.add(index);chosen.push(sourcePages[index]);}else duplicates.push(index);}
 preflight(doc,selection,false);
 setPageOrder(doc,chosen);pruneForms(doc);if(chosen.length<sourcePages.length)prunePageLinks(doc);
 // Duplicate a field subtree from a separate context, so values/widgets do not alias.
 const copies=[];
 for(const index of duplicates) {
  check();const one=await load(bytes);preflight(one,[index],true);
  const page=one.getPage(index),annots=page.node.lookupMaybe(name('Annots'),PDFArray);
  if(annots)for(const ref of annots.asArray()){const dict=one.context.lookup(ref,PDFDict),action=dict.lookupMaybe(name('A'),PDFDict);let target=dict.get(name('Dest'));if(action?.get(name('S'))===name('GoTo'))target=action.get(name('D'));const resolved=target?one.context.lookup(target):null;if(resolved instanceof PDFArray){const first=resolved.get(0);if(first instanceof PDFRef&&first.toString()!==page.ref.toString())unsupported('links to other pages while duplicating a page');}}
  setPageOrder(one,[one.getPage(index)]);pruneForms(one);prunePageLinks(one);gc(one);
  const imported=importRich(doc,one,freshPrefix(doc,"Nashi copy"),false);copies.push(imported[0]);
 }
 const byIndex=new Map(chosen.map((p,i)=>[[...seen][i],p])),emitted=new Set(),final=[];
 for(const index of selection) {if(!emitted.has(index)){emitted.add(index);final.push(byIndex.get(index));}else final.push(copies.shift());}
 setPageOrder(doc,final);validate(doc);gc(doc);validateNoRemovedPages(doc);return doc;
}
async function transform(request) {
 if(!request||!Array.isArray(request.sources)||!request.sources.length||!Array.isArray(request.selections)||request.sources.length!==request.selections.length)throw new Error("Choose PDF sources and a page selection for each source.");
 if(request.sources.length>1000)throw new Error("Choose no more than 1,000 PDF files at once.");
 const documents=[];let total=0;
 for(let i=0;i<request.sources.length;i++) {
  const first=await load(request.sources[i]),selection=request.selections[i];
  if(!Array.isArray(selection)||!selection.length||selection.length>10000||!selection.every(x=>Number.isSafeInteger(x)&&x>=0&&x<first.getPageCount()))throw new Error("A selected PDF page is outside the document.");
  total+=selection.length;if(total>10000)throw new Error("Choose no more than 10,000 output pages at once.");
  if(request.sources.length>1&&first.catalog.has(name('PageLabels')))unsupported('custom page labels while merging documents');
  preflight(first,selection,i>0);
  documents.push(await selectDocument(request.sources[i],selection));
 }
 const result=documents[0];
 for(let i=1;i<documents.length;i++){check();importRich(result,documents[i],freshPrefix(result,"Nashi document "+(i+1)));}
 for(const [key,value]of Object.entries(request.rotations||{})) {
  const index=Number(key);if(!Number.isSafeInteger(index)||index<0||index>=result.getPageCount()||!Number.isSafeInteger(value)||value%90!==0)throw new Error("PDF rotations must be whole quarter turns on valid pages.");
  result.getPage(index).setRotation(degrees(((value%360)+360)%360));
 }
 validate(result);sortAttachmentNames(result);gc(result);validateNoRemovedPages(result);check();
 const bytes=await result.saveAsBase64({objectsPerTick:Infinity,updateFieldAppearances:false,useObjectStreams:true});check();
 const reopened=await load(bytes);validate(reopened);validateNoRemovedPages(reopened);
 if(reopened.getPageCount()!==total)throw new Error("The PDF page count changed unexpectedly. No copy was exported.");
 return bytes;
}
global.NashiPDF={transform,run:function(json){transform(JSON.parse(json)).then(_nashiPDFComplete).catch(error=>_nashiPDFFailed(String(error.message||error)));}};
})(this);

