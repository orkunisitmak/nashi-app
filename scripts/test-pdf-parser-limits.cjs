const assert = require("node:assert/strict");
const { readFileSync } = require("node:fs");
const { resolve } = require("node:path");
const { createContext, runInContext } = require("node:vm");
const { deflateSync } = require("node:zlib");

const root = resolve(__dirname, "..");
const macBundle = resolve(root, "Resources/PDFLib.js");
const webBundle = resolve(root, "docs/assets/vendor/pdf-lib.min.js");
const { PDFDocument } = require(macBundle);

/** Wrap attacker-controlled stream declarations in a synthetic one-page PDF. */
function fixture(type, fields, contents = Buffer.alloc(0), streamCount = 1) {
    const parts = [
        Buffer.from("%PDF-1.5\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"
            + "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n"
            + "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n"),
    ];
    for (let index = 0; index < streamCount; index++) {
        parts.push(Buffer.from(`${4 + index} 0 obj\n<< /Type /${type} ${fields} /Length ${contents.length} >>\nstream\n`),
            contents, Buffer.from("\nendstream\nendobj\n"));
    }
    parts.push(Buffer.from("trailer\n<< /Size 101 /Root 1 0 R >>\n%%EOF\n"));
    return Buffer.concat(parts);
}

/** Reuse one small compressed payload to exercise allocation across PDF streams. */
function compressedObjectStream(size, streamCount = 1) {
    const contents = Buffer.alloc(size, 32);
    contents.write("100 0 << /Audit true >>");
    return fixture("ObjStm", "/N 1 /First 6 /Filter /FlateDecode", deflateSync(contents), streamCount);
}

/** Check ordinary page editing and the resource limits shared by both runtimes. */
async function main() {
    const normalize = path => readFileSync(path, "utf8").replaceAll("\r\n", "\n");
    assert.ok(normalize(macBundle) === normalize(webBundle), "The Mac and web PDF parser bundles differ.");
    const macRuntime = createContext({});
    runInContext(normalize(macBundle), macRuntime);
    runInContext(readFileSync(resolve(root, "Resources/PDFInterop.js"), "utf8"), macRuntime);

    const source = await PDFDocument.create();
    source.addPage();
    const sourceBytes = await source.save();
    const loaded = await PDFDocument.load(sourceBytes);
    const output = await PDFDocument.create();
    for (const page of await output.copyPages(loaded, [0])) output.addPage(page);
    assert.equal((await PDFDocument.load(await output.save())).getPageCount(), 1);
    const edit = input => macRuntime.NashiPDF.transform({
        sources: [Buffer.from(input).toString("base64")],
        selections: [[0]],
        rotations: { 0: 90 },
    });
    const rotated = await PDFDocument.load(await edit(sourceBytes));
    assert.equal(rotated.getPage(0).getRotation().angle, 90);

    const ordinaryStream = compressedObjectStream(8 * 1024 * 1024);
    assert.equal((await PDFDocument.load(ordinaryStream)).getPageCount(), 1);
    const totalDecoded = compressedObjectStream(64 * 1024 * 1024, 5);
    await assert.rejects(PDFDocument.load(totalDecoded),
        /PDF resource limit exceeded: decoded streams are too large in total/);
    const filterChain = Array(17).fill("/FlateDecode").join(" ");
    await assert.rejects(PDFDocument.load(fixture("ObjStm", `/N 0 /First 0 /Filter [${filterChain}]`)),
        /PDF resource limit exceeded: too many stream filters/);

    const excessiveEntries = fixture("XRef", "/Root 1 0 R /Size 2147483647 /W [0 0 0]");
    await assert.rejects(PDFDocument.load(excessiveEntries), /PDF resource limit exceeded: too many PDF entries/);
    await assert.rejects(edit(excessiveEntries), /PDF resource limit exceeded: too many PDF entries/);
    await assert.rejects(PDFDocument.load(fixture("XRef", "/Root 1 0 R /Size 1000001 /Index [0 500000 500000 500001] /W [0 0 0]")),
        /PDF resource limit exceeded: too many PDF entries/);
    await assert.rejects(PDFDocument.load(fixture("XRef", "/Root 1 0 R /Size 1 /W [2147483647 0 0]")),
        /PDF resource limit exceeded: invalid cross-reference field widths/);
    await assert.rejects(PDFDocument.load(fixture("ObjStm", "/N 2147483647 /First 0")),
        /PDF resource limit exceeded: too many PDF entries/);

    const oversizedStream = compressedObjectStream(64 * 1024 * 1024 + 1);
    assert.ok(oversizedStream.length < 100_000);
    await assert.rejects(PDFDocument.load(oversizedStream),
        /PDF resource limit exceeded: a decoded stream is too large/);
    console.log("PDF parser resource limits passed for the shared macOS and web bundle.");
}

main().catch(error => {
    console.error(error);
    process.exitCode = 1;
});
