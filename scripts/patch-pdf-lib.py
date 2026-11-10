#!/usr/bin/env python3
"""Apply the Nashi parser resource limits to the pinned pdf-lib browser bundle."""
import hashlib
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
UPSTREAM_SHA256 = "0f9a5cad07941f0826586c94e089d89b918c46e5c17cf2d5a3c6f666e3bc694f"
OUTPUTS = ("Resources/PDFLib.js", "docs/assets/vendor/pdf-lib.min.js")

GUARDS = """
// Nashi resource limits for untrusted PDF input. See scripts/patch-pdf-lib.py.
const nashiPDFStreamLimit = 64 * 1024 * 1024;
const nashiPDFDecodeLimit = 256 * 1024 * 1024;
const nashiPDFEntryLimit = 1000000;
const nashiPDFBudgets = new WeakMap();
class NashiPDFResourceLimitError extends Error {
    constructor(resource) {
        super("PDF resource limit exceeded: " + resource + ".");
    }
}
function nashiPDFBudget(context) {
    let budget = nashiPDFBudgets.get(context);
    if (!budget) {
        budget = {decoded: 0, entries: 0};
        nashiPDFBudgets.set(context, budget);
    }
    return budget;
}
function nashiTakePDFEntries(context, count) {
    const budget = nashiPDFBudget(context);
    if (!Number.isSafeInteger(count) || count < 0 || count > nashiPDFEntryLimit - budget.entries) {
        throw new NashiPDFResourceLimitError("too many PDF entries");
    }
    budget.entries += count;
}
"""

REPLACEMENTS = (
    ('"use strict";', '"use strict";\n' + GUARDS),
    (
        "var e=this.buffer;if(t<=e.byteLength)return e;for(var n=this.minBufferLength;n<t;)n*=2;var r=new Uint8Array(n);return r.set(e),this.buffer=r",
        """
    if (!Number.isSafeInteger(t) || t < 0 || t > nashiPDFStreamLimit) {
        throw new NashiPDFResourceLimitError("a decoded stream is too large");
    }
    const previous = this.buffer;
    if (t <= previous.byteLength) return previous;
    let capacity = this.minBufferLength;
    while (capacity < t) capacity *= 2;
    capacity = Math.min(capacity, nashiPDFStreamLimit);
    if (this.nashiPDFBudget) {
        const growth = capacity - previous.byteLength;
        if (growth > nashiPDFDecodeLimit - this.nashiPDFBudget.decoded) {
            throw new NashiPDFResourceLimitError("decoded streams are too large in total");
        }
        this.nashiPDFBudget.decoded += growth;
    }
    const buffer = new Uint8Array(capacity);
    buffer.set(previous);
    return this.buffer = buffer;
""",
    ),
    (
        'if(i instanceof Fo)r=Cl(r,i,o);else if(i instanceof fo)for(var a=0,s=i.size();a<s;a++)r=Cl(r,i.lookup(a,Fo),o&&o.lookupMaybe(a,Co));else if(i)throw new Hn([Fo,fo],i);return r',
        """
    if (i instanceof Fo) {
        r = Cl(r, i, o);
        r.nashiPDFBudget = nashiPDFBudget(e.context);
    } else if (i instanceof fo) {
        if (i.size() > 16) throw new NashiPDFResourceLimitError("too many stream filters");
        for (let index = 0; index < i.size(); index++) {
            r = Cl(r, i.lookup(index, Fo), o && o.lookupMaybe(index, Co));
            r.nashiPDFBudget = nashiPDFBudget(e.context);
        }
    } else if (i) {
        throw new Hn([Fo,fo], i);
    }
    return r;
""",
    ),
    (
        "prototype.parseOffsetsAndObjectNumbers=function(){for(",
        "prototype.parseOffsetsAndObjectNumbers=function(){\n"
        "    nashiTakePDFEntries(this.context, this.objectCount);\n"
        "    for(",
    ),
    (
        "prototype.parseEntries=function(){for(",
        """
prototype.parseEntries=function(){
    if (this.byteWidths.length !== 3 || this.byteWidths.some(width =>
        !Number.isSafeInteger(width) || width < 0 || width > 8)) {
        throw new NashiPDFResourceLimitError("invalid cross-reference field widths");
    }
    for (const subsection of this.subsections) {
        nashiTakePDFEntries(this.context, subsection.length);
    }
    for(""",
    ),
    (
        "case 4:return e.sent(),this.bytes.moveTo(t),this.tryToParseInvalidIndirectObject(),[3,5]",
        """
case 4: {
    const error = e.sent();
    if (error instanceof NashiPDFResourceLimitError) throw error;
    return this.bytes.moveTo(t),this.tryToParseInvalidIndirectObject(),[3,5];
}
""",
    ),
)


def main(upstream):
    source = Path(upstream).read_bytes().replace(b"\r\n", b"\n")
    if hashlib.sha256(source).hexdigest() != UPSTREAM_SHA256:
        raise SystemExit("Expected the unmodified pdf-lib 1.17.1 dist/pdf-lib.min.js bundle.")
    bundle = source.decode("utf-8")
    for original, replacement in REPLACEMENTS:
        if bundle.count(original) != 1:
            raise SystemExit("Unable to locate a unique pdf-lib patch point.")
        bundle = bundle.replace(original, replacement)
    for output in OUTPUTS:
        (ROOT / output).write_bytes(bundle.encode("utf-8"))


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: patch-pdf-lib.py UPSTREAM_PDF_LIB_MIN_JS")
    main(sys.argv[1])
