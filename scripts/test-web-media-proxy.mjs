import assert from "node:assert/strict";
import { test } from "node:test";
import { handleConnection, isPublicIP, publicAddresses } from "../Sources/NashiCore/Resources/WebMediaProxy.mjs";

/** Build one authenticated SOCKS exchange with a hostname target. */
function request(host, password = "test", payload = []) {
  const name = Buffer.from(host);
  return Buffer.concat([
    Buffer.of(5, 1, 2, 1, 5), Buffer.from("nashi"), Buffer.of(password.length), Buffer.from(password),
    Buffer.of(5, 1, 0, 3, name.length), name, Buffer.of(1, 187), Buffer.from(payload),
  ]);
}

/** Simulate short reads and writes so message boundaries do not depend on packet boundaries. */
function socket(input) {
  let offset = 0;
  const output = [];
  return {
    output,
    closed: false,
    async read(buffer) {
      const count = Math.min(2, buffer.length, input.length - offset);
      if (!count) return null;
      buffer.set(input.subarray(offset, offset + count));
      offset += count;
      return count;
    },
    async write(buffer) {
      const part = buffer.subarray(0, 2);
      output.push(...part);
      return part.length;
    },
    async closeWrite() {},
    close() { this.closed = true; },
  };
}

test("rejects local and special-purpose IP addresses, including IPv4-mapped IPv6", () => {
  for (const address of ["127.0.0.1", "10.1.2.3", "100.64.1.2", "169.254.1.2", "172.31.1.2", "192.168.1.2",
    "::1", "::ffff:192.168.1.2", "fe80::1%3", "fc00::1", "ff01::1", "2002::1", "2001:db8::1", "3fff::1"]) {
    assert.equal(isPublicIP(address), false, address);
  }
  for (const address of ["93.184.216.34", "2606:4700::1111", "::ffff:93.184.216.34"]) {
    assert.equal(isPublicIP(address), true, address);
  }
});

test("rejects mixed DNS answers and local names without selecting a safe-looking member", async () => {
  const resolve = async () => [{ address: "93.184.216.34" }, { address: "192.168.1.2" }];
  assert.deepEqual(await publicAddresses("mixed.example.org", resolve), []);
  assert.deepEqual(await publicAddresses("private.local.", () => assert.fail("local names must not resolve")), []);
});

test("pins each connection to its checked IP and blocks a later request to the same name", async () => {
  let lookups = 0;
  const resolve = async () => [{ address: ++lookups === 1 ? "93.184.216.34" : "127.0.0.1" }];
  const connections = [];
  const remote = socket(Buffer.from([21, 22]));
  const connect = async target => { connections.push(target); return remote; };
  const first = socket(request("media.example.org", "test", [11, 12]));
  await handleConnection(first, "test", connect, resolve);
  assert.deepEqual(connections, [{ hostname: "93.184.216.34", port: 443 }]);
  assert.deepEqual(first.output.slice(-2), [21, 22]);
  assert.deepEqual(remote.output, [11, 12]);
  assert.equal(first.closed && remote.closed, true);

  const redirected = socket(request("media.example.org"));
  await handleConnection(redirected, "test", connect, resolve);
  assert.deepEqual(redirected.output.slice(4, 6), [5, 2]);
  assert.equal(connections.length, 1);
});

test("requires the per-run password before resolving a destination", async () => {
  const client = socket(request("media.example.org", "wrong"));
  await handleConnection(client, "test", () => assert.fail("unexpected connection"),
    () => assert.fail("unexpected lookup"));
  assert.deepEqual(client.output, [5, 2, 1, 1]);
});
