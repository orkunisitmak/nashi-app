// Clean native Pages adapter over the public MIT cupertino-files package.
// The package and donor are fetched independently; original app resources are
// neither build inputs nor runtime dependencies.
import "./pages-runtime.js";
import { PagesDocument } from "../Tools/PagesBuild/cupertino/package/dist/pages/document.js";
import { StylesheetModel } from "../Tools/PagesBuild/cupertino/package/dist/tss/stylesheet.js";
import { DrawableModel } from "../Tools/PagesBuild/cupertino/package/dist/tsd/drawables.js";
import seed from "../Tools/PagesBuild/table-seed.pages";

function check() { if (typeof _nashiCheckCancelled === "function" && _nashiCheckCancelled()) throw new Error("Operation cancelled."); }
function bytes(base64) { const text = atob(base64); return Uint8Array.from(text, c => c.charCodeAt(0)); }
function base64(data) {
  let text = "";
  for (let i = 0; i < data.length; i += 8192) text += String.fromCharCode(...data.subarray(i, i + 8192));
  return btoa(text);
}
function character(value = {}) {
  const allowed = ["bold", "italic", "fontSize", "fontName", "fontColor", "backgroundColor", "underline", "strikethru", "superscript", "tracking", "baselineShift", "language"];
  return Object.fromEntries(allowed.filter(key => value[key] !== undefined).map(key => [key,value[key]]));
}
function paragraphStyle(value = {}) {
  const allowed = ["alignment", "spaceBefore", "spaceAfter", "firstLineIndent", "leftIndent", "rightIndent", "lineSpacing", "keepLinesTogether", "keepWithNext", "pageBreakBefore", "widowControl"];
  return Object.fromEntries(allowed.filter(key => value[key] !== undefined).map(key => [key,value[key]]));
}

export function write(json) {
  check();
  const model = JSON.parse(json);
  if (!Array.isArray(model.blocks)) throw new Error("Missing document content.");
  const document = model.blocks.some(block => block.type === "table") ? PagesDocument.load(seed) : PagesDocument.blank();
  document.body.setText("");
  for (const section of document.sections()) { section.setHeaderText(""); section.setFooterText(""); }
  if (model.pageSetup) document.setPageSetup(model.pageSetup);
  if (model.header) document.sections()[0]?.setHeaderText(model.header);
  if (model.footer) document.sections()[0]?.setFooterText(model.footer);
  const bodyStyle = document.stylesheet.style("Body") ?? document.stylesheet.style("Normal") ?? document.stylesheet.styles().find(style => style.type === 2022);
  const baseId = typeof bodyStyle?.id === "bigint" ? bodyStyle.id : undefined;
  let ordinal = 0;
  const appendParagraph = block => {
    check();
    const runs = block.runs ?? [{text: block.text ?? ""}];
    const start = document.bodyText.length;
    // IWA paragraph terminators are LF; soft line breaks inside a paragraph
    // use Unicode LINE SEPARATOR so the writer keeps the paragraph editable.
    const text = runs.map(run => run.type === "image" ? "" : (run.text ?? "").replaceAll("\n", "\u2028")).join("");
    const style = document.stylesheet.createParagraphStyle({
      name: `Converted paragraph ${++ordinal}`, basedOn: baseId,
      character: {fontName: "Helvetica", fontSize: 12, ...character(block.character)}, paragraph: paragraphStyle(block.paragraph)
    });
    const index = document.appendParagraph(text, style);
    let offset = document.paragraphs()[index]?.start ?? start;
    for (const run of runs) {
      check();
      if (run.type === "image") {
        const maxWidth = (model.pageSetup?.pageWidth ?? 595.28) - (model.pageSetup?.leftMargin ?? 56.69) - (model.pageSetup?.rightMargin ?? 56.69);
        const inserted = document.insertInlineImage(offset, bytes(run.data), {fileName: run.fileName ?? "image.png", width: run.width, height: run.height, maxWidth: Math.max(36,maxWidth)});
        if (run.alt) new DrawableModel(document.store, document.object(inserted.imageId)).accessibilityDescription = run.alt;
        offset++;
      } else {
        const length = (run.text ?? "").length;
        if (length && run.character && Object.keys(run.character).length) document.applyCharacterFormatting(offset, offset + length, character(run.character));
        if (length && run.link) document.insertLink(offset, offset + length, run.link);
        offset += length;
      }
    }
  };
  for (const block of model.blocks) {
    check();
    if (block.type !== "table") { appendParagraph(block); continue; }
    const rows = block.rows ?? [];
    const rowCount = Math.max(1, rows.length), columnCount = Math.max(1,...rows.map(row => row.length));
    if (rowCount * columnCount > 100000) throw new Error("The table is too large.");
    // A paragraph anchor is necessary for an independently editable inline
    // table. The licensed seed's unused tables remain unanchored and empty.
    appendParagraph({runs: []});
    const pos = document.bodyText.length - 1;
    const table = document.insertInlineTable(Math.max(0,pos), {withContent: false, name: `Table ${ordinal}`});
    if (table.rowCount < rowCount) table.insertRows(table.rowCount, rowCount - table.rowCount);
    if (table.rowCount > rowCount) table.deleteRows(rowCount, table.rowCount - rowCount);
    if (table.columnCount < columnCount) table.insertColumns(table.columnCount, columnCount - table.columnCount);
    if (table.columnCount > columnCount) table.deleteColumns(columnCount, table.columnCount - columnCount);
    table.nameVisible = false;
    table.setBands({headerRows: block.headerRows ?? 0, headerColumns: 0, footerRows: 0});
    for (let row = 0; row < rowCount; row++) for (let column = 0; column < columnCount; column++) {
      check();
      const cell = rows[row]?.[column];
      table.setCell(row,column,typeof cell === "string" ? cell : cell?.text ?? "");
      if (cell?.formatting) table.setCellFormatting(row,column,cell.formatting);
    }
    if (block.columnWidths?.length) block.columnWidths.forEach((width,index) => { if (index < columnCount && width > 0) table.setColumnWidth(index,width); });
    else for (let column = 0; column < columnCount; column++) table.setColumnWidth(column,(block.width ?? 480) / columnCount);
    for (const merge of block.merges ?? []) { check(); if (merge.row + merge.rowCount <= rowCount && merge.column + merge.columnCount <= columnCount) table.mergeCells(merge.row,merge.column,merge.rowCount,merge.columnCount); }
  }
  check();
  const data = document.save();
  check();
  // Reopen as a structural integrity check before handing bytes to the host.
  const reread = PagesDocument.load(data);
  if (!reread.bodyOrUndefined) throw new Error("The generated document has no editable body.");
  return base64(data);
}

export function read(data) {
  check();
  const document = PagesDocument.load(bytes(data));
  const images = new Map(document.images().map(image => [String(image.id),image]));
  const tables = new Map(document.tables().filter(table => table.infoObject).map(table => [String(table.infoObject.identifier),table]));
  const used = new Set();
  const imageRun = image => {
    const geometry = image.geometry() ?? {};
    const raw = image.data();
    if (!raw) return {text: image.accessibilityDescription ?? ""};
    return {type: "image", data: base64(raw), fileName: image.fileName ?? "image.png", width: geometry.width, height: geometry.height, alt: image.accessibilityDescription};
  };
  const tableBlock = table => ({type: "table", headerRows: table.headerRowCount, merges: table.merges(), columnWidths: Array.from({length:table.columnCount},(_,column)=>table.columnWidth(column)), rows: Array.from({length:table.rowCount},(_,row)=>Array.from({length:table.columnCount},(_,column)=>({text: table.cellText(row,column), formatting: table.cellFormatting(row,column)})))});
  const storageBlocks = storage => {
    const result = [];
    const sheetObject = storage.stylesheetId !== undefined ? document.object(storage.stylesheetId) : undefined;
    const stylesheet = sheetObject ? new StylesheetModel(document.store,sheetObject) : document.stylesheet;
    const attachments = storage.attachments(), links = storage.links(), styleRuns = storage.characterStyleRuns();
    for (const paragraph of storage.paragraphs()) {
      check();
      const style = paragraph.styleId !== undefined ? stylesheet.style(paragraph.styleId)?.resolved() ?? {} : {};
      let block = {type: "paragraph", paragraph: style.paragraph ?? {}, character: character(style.character), runs: []};
      const stops = new Set([paragraph.start,paragraph.end]);
      for (const run of styleRuns) { if (run.start > paragraph.start && run.start < paragraph.end) stops.add(run.start); if (run.end > paragraph.start && run.end < paragraph.end) stops.add(run.end); }
      for (const link of links) { if (link.start > paragraph.start && link.start < paragraph.end) stops.add(link.start); if (link.end > paragraph.start && link.end < paragraph.end) stops.add(link.end); }
      for (const attachment of attachments) if (attachment.index >= paragraph.start && attachment.index < paragraph.end) { stops.add(attachment.index); stops.add(attachment.index+1); }
      const bounds = [...stops].sort((a,b)=>a-b);
      for (let i = 0; i + 1 < bounds.length; i++) {
        const start = bounds[i], end = bounds[i+1];
        const attachment = attachments.find(value => value.index === start && value.drawableId !== undefined);
        if (attachment) {
          const id = String(attachment.drawableId); used.add(id);
          if (images.has(id)) block.runs.push(imageRun(images.get(id)));
          else if (tables.has(id)) {
            if (block.runs.length) result.push(block);
            result.push(tableBlock(tables.get(id)));
            block = {type:"paragraph",paragraph:style.paragraph ?? {},character:character(style.character),runs:[]};
          }
          continue;
        }
        const text = storage.text.slice(start,end).replaceAll("\uFFFC", "").replaceAll("\u000E", "");
        if (text) block.runs.push({text, character:character(storage.characterFormattingAt(start)), link:links.find(link => link.start <= start && link.end >= end)?.url});
      }
      if (block.runs.length || bounds.length <= 1) result.push(block);
    }
    return result;
  };
  let blocks = document.bodyOrUndefined ? storageBlocks(document.body) : [];
  for (const box of document.textBoxes()) if (box.storage.text) blocks.push(...storageBlocks(box.storage));
  // Floating media in real Pages files is retained even though its precise
  // page wrap/stacking can only be approximated by the reflowed Office export.
  for (const image of images.values()) if (!used.has(String(image.id))) blocks.push({type:"paragraph",runs:[imageRun(image)]});
  if (document.isPageLayout) for (const table of tables.values()) if (!used.has(String(table.infoObject.identifier))) blocks.push(tableBlock(table));
  check();
  return JSON.stringify({blocks,pageSetup:document.pageSetup(),header:document.sections()[0]?.headerText(),footer:document.sections()[0]?.footerText(),isPageLayout:document.isPageLayout});
}
