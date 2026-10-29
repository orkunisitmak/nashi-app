// Node.js twin of pages-windows.js for Linux desktops without Deno. Nashi starts
// it with --permission, so it can read only the bundle and input and write only the output.
import { readFile, writeFile } from 'node:fs/promises';
const [bundle, mode, input, output] = process.argv.slice(2);
if (!['read', 'write'].includes(mode)) throw new Error('Invalid Pages operation');
const runtime = new Function((await readFile(bundle, 'utf8')) + ';return NashiPages;')();
if (mode === 'read') {
  const bytes = await readFile(input);
  await writeFile(output, runtime.read(bytes.toString('base64')));
} else {
  const binary = runtime.write(await readFile(input, 'utf8'));
  await writeFile(output, Buffer.from(binary, 'base64'));
}
