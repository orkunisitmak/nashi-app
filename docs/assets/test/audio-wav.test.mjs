import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { runInNewContext } from 'node:vm';

// Exercise the WAV encoder shipped in the browser demo without a DOM.
const source = readFileSync(new URL('../playground.js', import.meta.url), 'utf8');
const implementation = source.split(/\r?\n/).find(line => line.startsWith('function audioWav(e){'));
assert.ok(implementation, 'playground audioWav implementation was not found');
const audioWav = runInNewContext(implementation + '\naudioWav', { Blob });

const stereo = (left, right) => ({
  length: 1,
  sampleRate: 48000,
  numberOfChannels: 2,
  getChannelData: channel => Float32Array.of(channel === 0 ? left : right),
});

async function samples(values, buffer = stereo(0.6, -0.2)) {
  const blob = audioWav({ id: 'audioChannels', audioBuffer: buffer, values });
  const view = new DataView(await blob.arrayBuffer());
  const channels = view.getUint16(22, true);
  const pcm = Array.from({length: channels}, (_, index) => view.getInt16(44 + index * 2, true) / 32767);
  return {channels, pcm};
}

test('mono channel mix respects left and right gains independently', async () => {
  const {channels, pcm} = await samples({channels: 'mono', left: 0, right: 100});
  assert.equal(channels, 1);
  assert.ok(Math.abs(pcm[0] + 0.1) < 0.001, 'only the right channel should contribute');
});

test('mono channel mix respects zero right gain', async () => {
  const {pcm} = await samples({channels: 'mono', left: 100, right: 0});
  assert.ok(Math.abs(pcm[0] - 0.3) < 0.001, 'only the left channel should contribute');
});

test('mono downmix remains an equal average at full gain', async () => {
  const {pcm} = await samples({channels: 'mono', left: 100, right: 100});
  assert.ok(Math.abs(pcm[0] - 0.2) < 0.001);
});

test('stereo keeps independent output channel gains', async () => {
  const {channels, pcm} = await samples({channels: 'stereo', left: 0, right: 100});
  assert.equal(channels, 2);
  assert.ok(Math.abs(pcm[0]) < 0.001);
  assert.ok(Math.abs(pcm[1] + 0.2) < 0.001);
});

const timeImplementation = source.split(/\r?\n/).find(line => line.startsWith('function time(v){'));
assert.ok(timeImplementation, 'media time formatter was not found');
const formatTime = runInNewContext(timeImplementation + '\ntime', {});

test('rounded seconds carry into the next minute', () => {
  assert.equal(formatTime(59.9996), '01:00.000');
  assert.equal(formatTime(119.9996), '02:00.000');
  assert.equal(formatTime(3599.9996), '60:00.000');
});

test('media time preserves ordinary millisecond precision and invalid-value handling', () => {
  assert.equal(formatTime(59.9994), '00:59.999');
  assert.equal(formatTime(60), '01:00.000');
  assert.equal(formatTime(0.001), '00:00.001');
  assert.equal(formatTime(-1), '—');
  assert.equal(formatTime(Infinity), '—');
});
