import test from 'node:test';
import assert from 'node:assert/strict';
import {classifyMediaPreviewURL} from '../docs/assets/download-preview.mjs';

test('only a real YouTube hostname unlocks video, audio and thumbnail previews',()=>{
  for(const url of [
    'https://youtube.com/watch?v=demo',
    'https://www.youtube.com/watch?v=demo',
    'https://m.youtube.com/shorts/abc',
    'https://music.youtube.com/watch?v=demo',
    'https://youtu.be/demo'
  ])assert.equal(classifyMediaPreviewURL(url),'youtube',url);
});

test('Instagram video and photo preview actions follow the actual URL pathname',()=>{
  for(const url of [
    'https://instagram.com/reel/demo/',
    'https://www.instagram.com/reels/demo/',
    'https://m.instagram.com/reel/demo/',
    'https://www.instagram.com/natgeo/reel/C1a2B3c4D5e/',
    'https://www.instagram.com/tv/C1a2B3c4D5e/'
  ])assert.equal(classifyMediaPreviewURL(url),'instagram-video',url);
  assert.equal(classifyMediaPreviewURL('https://www.instagram.com/p/demo/?utm_source=test'),'instagram-photos');
  assert.equal(classifyMediaPreviewURL('https://instagram.com/some.user_1/p/C1a2B3c4D5e/'),'instagram-photos');
  assert.equal(classifyMediaPreviewURL('https://www.instagram.com/stories/demo'),'original');
});

test('lookalike domains and query/fragment text cannot impersonate supported providers',()=>{
  for(const url of [
    'https://notyoutube.com/watch?v=demo',
    'https://youtube.com.evil.example/watch?v=demo',
    'https://example.net/?next=https://youtube.com/watch?v=demo',
    'https://example.net/#instagram.com/reel/demo',
    'https://notinstagram.com/reel/demo',
    'https://www.instagram.com.evil.example/reel/demo',
    'https://example.net/instagram.com/p/demo'
  ])assert.equal(classifyMediaPreviewURL(url),'original',url);
});

test('invalid or non-HTTP(S) links have only the generic preview',()=>{
  for(const url of [
    'not a URL','', 'javascript:alert("youtube.com")',
    'file:///youtube.com/watch?v=demo',
    'https://user:secret@youtube.com/watch?v=demo',
    'https://unknown.example/reel/demo'
  ])assert.equal(classifyMediaPreviewURL(url),'original',url);
});
