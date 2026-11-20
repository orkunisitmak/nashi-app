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
      if (!/^utf-?8$/i.test(label)) throw new Error("Only UTF-8 text is supported.");
    }
    decode(bytes = new Uint8Array()) {
      const view = bytes instanceof ArrayBuffer ? new Uint8Array(bytes)
        : ArrayBuffer.isView(bytes) ? new Uint8Array(bytes.buffer, bytes.byteOffset, bytes.byteLength) : null;
      if (!view) throw new TypeError("TextDecoder requires a buffer or buffer view.");
      let binary = "";
      for (let i = 0; i < view.length; i += 8192) binary += String.fromCharCode(...view.subarray(i, i + 8192));
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
