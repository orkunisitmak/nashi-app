import test from 'node:test';
import assert from 'node:assert/strict';
import {classifyMediaPreviewURL,mediaPreviewOptions} from '../docs/assets/download-preview.mjs';

test('only a real YouTube hostname unlocks video, audio and thumbnail previews',()=>{
  for(const url of [
    'https://youtube.com/watch?v=demo',
    'https://www.youtube.com/watch?v=demo',
    'https://m.youtube.com/shorts/abc',
    'https://music.youtube.com/watch?v=demo',
    'https://youtu.be/demo',
    'https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg',
    'https://img.youtube.com/vi/dQw4w9WgXcQ/0.jpg',
    'https://ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg',
    'https://i.ytimg.com/vi_webp/dQw4w9WgXcQ/hq720.webp',
    'https://i1.ytimg.com/an_webp/dQw4w9WgXcQ/hqdefault.webp'
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
    'https://i.ytimg.com.evil.example/vi/dQw4w9WgXcQ/hqdefault.jpg',
    'https://i.ytimg.com/vi/short/hqdefault.jpg',
    'https://i.ytimg.com/watch/dQw4w9WgXcQ/hqdefault.jpg',
    'https://example.net/?next=https://youtube.com/watch?v=demo',
    'https://example.net/#instagram.com/reel/demo',
    'https://notinstagram.com/reel/demo',
    'https://www.instagram.com.evil.example/reel/demo',
    'https://example.net/instagram.com/p/demo'
  ])assert.ok(['original','image'].includes(classifyMediaPreviewURL(url)),url);
});

test('direct image CDN URLs follow the Windows image-extension list',()=>{
  for(const url of [
    'https://pbs.twimg.com/media/Demo.jpg',
    'https://i.pinimg.com/originals/aa/bb/photo.png',
    'https://i.vimeocdn.com/video/123456.webp'
  ])assert.equal(classifyMediaPreviewURL(url),'image',url);
});

test('AVIF, BMP, TIFF, HEIC and HEIF file links unlock the image preview, provider links keep their kind',()=>{
  for(const url of [
    'https://cdn.example.org/photo.avif',
    'https://cdn.example.org/photo.bmp',
    'https://cdn.example.org/scan.tiff',
    'https://cdn.example.org/scan.tif',
    'https://cdn.example.org/photo.heic',
    'https://media.example.org/shot.heif',
    'https://cdn.example.org/photo.HEIC'
  ])assert.equal(classifyMediaPreviewURL(url),'image',url);
  assert.equal(classifyMediaPreviewURL('https://www.youtube.com/watch.heic'),'youtube');
  assert.equal(classifyMediaPreviewURL('https://www.instagram.com/p/demo.heic'),'instagram-photos');
});

test('a direct image link shows an Image option instead of Original media',()=>{
  assert.deepEqual(mediaPreviewOptions('https://pbs.twimg.com/media/Demo.jpg'),[['Image','Görsel']]);
  assert.deepEqual(mediaPreviewOptions('https://example.com/page'),[['Original media','Orijinal medya']]);
  assert.deepEqual(mediaPreviewOptions('https://youtu.be/demo').map(([en])=>en),['Video','Audio','Thumbnail']);
});

test('invalid or non-HTTP(S) links have only the generic preview',()=>{
  for(const url of [
    'not a URL','', 'javascript:alert("youtube.com")',
    'file:///youtube.com/watch?v=demo',
    'https://user:secret@youtube.com/watch?v=demo',
    'https://unknown.example/reel/demo'
  ])assert.equal(classifyMediaPreviewURL(url),'original',url);
});
