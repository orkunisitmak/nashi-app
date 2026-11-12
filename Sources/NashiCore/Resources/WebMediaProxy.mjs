import { lookup } from "node:dns/promises";
import { isIP } from "node:net";
import { fileURLToPath } from "node:url";

/** Limit outbound media sockets to globally routed unicast addresses. */
export function isPublicIP(address) {
  if (isIP(address) === 4) {
    const [a, b, c] = address.split(".").map(Number);
    return !(a === 0 || a === 10 || a === 127 || a >= 224
      || a === 100 && b >= 64 && b <= 127
      || a === 169 && b === 254 || a === 172 && b >= 16 && b <= 31
      || a === 192 && (b === 168 || b === 0 && (c === 0 || c === 2) || b === 88 && c === 99)
      || a === 198 && (b === 18 || b === 19 || b === 51 && c === 100)
      || a === 203 && b === 0 && c === 113);
  }
  if (isIP(address) !== 6 || address.includes("%")) return false;
  const canonical = new URL(`http://[${address}]`).hostname.slice(1, -1);
  const [a, b = 0] = canonical.split(":").map(part => Number.parseInt(part || "0", 16));
  if (canonical.startsWith("::ffff:")) {
    const [, high, low] = canonical.slice(2).split(":").map(part => Number.parseInt(part, 16));
    return isPublicIP(`${high >> 8}.${high & 255}.${low >> 8}.${low & 255}`);
  }
  return (a & 0xe000) === 0x2000
    && !(a === 0x2001 && (b < 0x0200 || b === 0x0db8))
    && a !== 0x2002 && !(a === 0x3fff && b < 0x1000);
}

/** Resolve each SOCKS request and keep only numeric destinations that passed the check. */
export async function publicAddresses(host, resolve = lookup) {
  const name = host.toLowerCase().replace(/\.$/, "");
  if (!isIP(name) && (!/^[a-z0-9_.-]+$/.test(name) || !name.includes(".")
      || [".localhost", ".local", ".home.arpa"].some(suffix => name.endsWith(suffix)))) return [];
  const results = isIP(name) ? [{ address: name }] : await resolve(host, { all: true });
  return results.length && results.every(({ address }) => isPublicIP(address))
    ? results.map(({ address }) => address) : [];
}

/** Read a bounded protocol field without consuming the following payload. */
async function readExactly(connection, length) {
  const bytes = new Uint8Array(length);
  for (let offset = 0; offset < length;) {
    const count = await connection.read(bytes.subarray(offset));
    if (count === null) throw new Error("SOCKS client disconnected.");
    offset += count;
  }
  return bytes;
}

/** Write a complete field even if the socket accepts only part of it. */
async function writeAll(connection, bytes) {
  for (let offset = 0; offset < bytes.length;) {
    offset += await connection.write(bytes.subarray(offset));
  }
}

/** Require per-process credentials before accepting any connection request. */
async function authenticate(client, password) {
  const [version, count] = await readExactly(client, 2);
  if (version !== 5 || !(await readExactly(client, count)).includes(2)) {
    await writeAll(client, Uint8Array.of(5, 255));
    return false;
  }
  await writeAll(client, Uint8Array.of(5, 2));
  const [authVersion, userLength] = await readExactly(client, 2);
  const decoder = new TextDecoder("utf-8", { fatal: true });
  const user = decoder.decode(await readExactly(client, userLength));
  const [passwordLength] = await readExactly(client, 1);
  const supplied = decoder.decode(await readExactly(client, passwordLength));
  const accepted = authVersion === 1 && user === "nashi" && supplied === password;
  await writeAll(client, Uint8Array.of(1, accepted ? 0 : 1));
  return accepted;
}

/** Accept only SOCKS5 TCP CONNECT and decode its IPv4, IPv6, or hostname target. */
async function destination(client) {
  const [version, command, reserved, type] = await readExactly(client, 4);
  if (version !== 5 || command !== 1 || reserved !== 0) return null;
  let host;
  if (type === 1) host = (await readExactly(client, 4)).join(".");
  else if (type === 3) {
    const [length] = await readExactly(client, 1);
    host = new TextDecoder("utf-8", { fatal: true }).decode(await readExactly(client, length));
  } else if (type === 4) {
    const bytes = await readExactly(client, 16);
    const words = new DataView(bytes.buffer);
    host = Array.from({ length: 8 }, (_, i) => words.getUint16(i * 2).toString(16)).join(":");
  } else return null;
  const [high, low] = await readExactly(client, 2);
  return { host, port: high * 256 + low };
}

/** Forward buffered data with backpressure and preserve TCP half-close semantics. */
async function relay(source, target) {
  const buffer = new Uint8Array(32 * 1024);
  for (;;) {
    const count = await source.read(buffer);
    if (count === null) break;
    await writeAll(target, buffer.subarray(0, count));
  }
  await target.closeWrite();
}

/** Close a socket even if its peer or the handshake deadline closed it first. */
function close(socket) {
  try { socket?.close(); } catch {}
}

/** Check and pin the destination of every connection, including extractor subrequests. */
export async function handleConnection(client, password, connect, resolve = lookup) {
  let remote;
  const timer = setTimeout(() => close(client), 10_000);
  const reply = code => writeAll(client, Uint8Array.of(5, code, 0, 1, 0, 0, 0, 0, 0, 0));
  try {
    if (!await authenticate(client, password)) return;
    const target = await destination(client);
    if (!target?.port) {
      await reply(7);
      return;
    }
    const addresses = await publicAddresses(target.host, resolve).catch(() => []);
    if (!addresses.length) {
      await reply(2);
      return;
    }
    for (const address of addresses) {
      try {
        remote = await connect({ hostname: address, port: target.port });
        break;
      } catch {}
    }
    await reply(remote ? 0 : 5);
    if (!remote) return;
    clearTimeout(timer);
    await Promise.all([relay(client, remote), relay(remote, client)]);
  } catch {
    // Malformed handshakes and closed sockets affect only the requesting connection.
  } finally {
    clearTimeout(timer);
    close(remote);
    close(client);
  }
}

/** Run one downloader with an authenticated, per-run loopback proxy and local-only FFmpeg inputs. */
async function runDownloader() {
  const [executable, ffmpeg, ffprobe, deno, scratch, ...args] = Deno.args;
  const separator = args.indexOf("--");
  if (!executable || !ffmpeg || !ffprobe || !deno || !scratch || separator < 0 || separator !== args.length - 2) {
    throw new Error("The media downloader configuration is incomplete.");
  }
  const options = args.slice(0, separator);
  const windowsStage = options.indexOf("-P");
  const staging = windowsStage >= 0 ? options[windowsStage + 1]
    : options.find((arg, index) => options[index - 1] === "--paths" && arg.startsWith("home:"))?.slice(5);
  // yt-dlp loads plugins from a child of the supplied directory. Copy just our
  // policy into scratch so Python cannot add bytecode to the signed app bundle.
  const pluginDirectory = `${scratch}/plugins`;
  const modulePath = "yt_dlp_plugins/postprocessor";
  const policyPath = `${pluginDirectory}/nashi/${modulePath}`;
  await Deno.mkdir(policyPath, { recursive: true });
  for (const module of ["nashi_media_policy.py", "_nashi_media_network.py"]) {
    const source = fileURLToPath(new URL(`./yt-dlp-policy/${modulePath}/${module}`, import.meta.url));
    await Deno.copyFile(source, `${policyPath}/${module}`);
  }
  const password = crypto.randomUUID();
  const listener = Deno.listen({ hostname: "127.0.0.1", port: 0 });
  const clients = new Set();
  const accept = (async () => {
    for await (const client of listener) {
      if (clients.size >= 48) { close(client); continue; }
      clients.add(client);
      void handleConnection(client, password, options => Deno.connect(options))
        .finally(() => clients.delete(client));
    }
  })();
  const proxy = `socks5h://nashi:${password}@127.0.0.1:${listener.addr.port}`;
  try {
    const child = new Deno.Command(executable, {
      args: ["--ignore-config", "--no-remote-components", "--no-update", ...options,
        "--no-plugin-dirs", "--plugin-dirs", pluginDirectory,
        "--use-postprocessor", "NashiMediaPolicy:when=pre_process",
        "--proxy", proxy, "--geo-verification-proxy", proxy, "--downloader", "native",
        "--ffmpeg-location", ffmpeg, "--", args[separator + 1]],
      env: {
        NO_PROXY: "", no_proxy: "", PYTHONDONTWRITEBYTECODE: "1",
        NASHI_MEDIA_PROXY: proxy, NASHI_MEDIA_FFMPEG: ffmpeg, NASHI_MEDIA_FFPROBE: ffprobe,
        NASHI_MEDIA_DENO: deno, NASHI_MEDIA_STAGING: staging ?? "",
      },
      cwd: scratch, stdin: "null", stdout: "inherit", stderr: "inherit",
    }).spawn();
    return (await child.status).code;
  } finally {
    close(listener);
    for (const client of clients) close(client);
    await accept;
  }
}

if (import.meta.main) Deno.exit(await runDownloader());
