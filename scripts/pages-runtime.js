// Web platform primitives supplied by JavaScriptCore's native host. This file
// is independently written; it contains no code from Nashi.app.
if (typeof globalThis.atob !== "function") globalThis.atob = value => _nashiBase64Decode(String(value));
if (typeof globalThis.btoa !== "function") globalThis.btoa = value => _nashiBase64Encode(String(value));
if (typeof globalThis.TextEncoder !== "function") {
  globalThis.TextEncoder = class {
    encode(value = "") {
      const bytes = atob(_nashiUTF8Encode(String(value)));
      return Uint8Array.from(bytes, character => character.charCodeAt(0));
    }
  };
}
if (typeof globalThis.TextDecoder !== "function") {
  globalThis.TextDecoder = class {
    constructor(label = "utf-8") {
      // The Pages library sizes PDF images with TextDecoder("latin1"); the
      // WHATWG Encoding Standard maps that label family to windows-1252.
      this.latin1 = /^(latin1|l1|iso-?8859-1|iso_8859-1|ascii|us-ascii|windows-1252|cp1252)$/i.test(String(label).trim());
      if (!this.latin1 && !/^utf-?8$/i.test(label)) throw new Error("Only UTF-8 text is supported.");
    }
    decode(bytes = new Uint8Array()) {
      let binary = "";
      if (this.latin1) {
        const high = "\u20AC\x81\u201A\u0192\u201E\u2026\u2020\u2021\u02C6\u2030\u0160\u2039\u0152\x8D\u017D\x8F\x90\u2018\u2019\u201C\u201D\u2022\u2013\u2014\u02DC\u2122\u0161\u203A\u0153\x9D\u017E\u0178";
        for (let i = 0; i < bytes.length; i++) binary += bytes[i] >= 0x80 && bytes[i] < 0xA0 ? high[bytes[i] - 0x80] : String.fromCharCode(bytes[i]);
        return binary;
      }
      for (let i = 0; i < bytes.length; i += 8192) binary += String.fromCharCode(...bytes.subarray(i, i + 8192));
      return _nashiUTF8Decode(btoa(binary));
    }
  };
}
if (typeof globalThis.crypto === "undefined") globalThis.crypto = {};
if (typeof globalThis.crypto.getRandomValues !== "function") {
  globalThis.crypto.getRandomValues = array => {
    const binary = atob(_nashiRandom(array.byteLength));
    const bytes = new Uint8Array(array.buffer, array.byteOffset, array.byteLength);
    for (let i = 0; i < bytes.length; i++) bytes[i] = binary.charCodeAt(i);
    return array;
  };
}
if (typeof globalThis.crypto.randomUUID !== "function") {
  globalThis.crypto.randomUUID = () => {
    const bytes = crypto.getRandomValues(new Uint8Array(16));
    bytes[6] = (bytes[6] & 15) | 64; bytes[8] = (bytes[8] & 63) | 128;
    const hex = Array.from(bytes, byte => byte.toString(16).padStart(2, "0")).join("");
    return `${hex.slice(0,8)}-${hex.slice(8,12)}-${hex.slice(12,16)}-${hex.slice(16,20)}-${hex.slice(20)}`;
  };
}
