// The browser demo is a local preview, not a media fetcher. Only actual
// provider hosts may select provider-specific controls.
export function classifyMediaPreviewURL(value) {
  let url;
  try { url = new URL(value); } catch { return 'original'; }
  if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password)
    return 'original';

  const host = url.hostname.toLowerCase().replace(/\.$/, '');
  if (['youtube.com', 'www.youtube.com', 'm.youtube.com', 'music.youtube.com',
       'youtu.be', 'www.youtu.be'].includes(host)) return 'youtube';
  if (host === 'img.youtube.com' || host === 'ytimg.com' || host.endsWith('.ytimg.com')) {
    const parts = url.pathname.split('/').filter(Boolean);
    if (parts.length >= 3 && ['vi', 'vi_webp', 'an_webp'].includes(parts[0]) &&
        /^[A-Za-z0-9_-]{11}$/.test(parts[1])) return 'youtube';
    return 'original';
  }

  if (!['instagram.com', 'www.instagram.com', 'm.instagram.com'].includes(host)) {
    const name = url.pathname.split('/').pop() ?? '';
    const ext = name.lastIndexOf('.') > 0 ? name.slice(name.lastIndexOf('.')).toLowerCase() : '';
    if (['.jpg', '.jpeg', '.png', '.webp', '.avif', '.gif', '.heic', '.heif', '.svg', '.bmp', '.tiff', '.tif'].includes(ext))
      return 'image';
    // Mac directKind video and audio extensions for links without a media type.
    return ['.mp4', '.mov', '.webm', '.mkv', '.m4v', '.mp3', '.m4a', '.wav', '.flac', '.ogg', '.opus', '.aac', '.aiff'].includes(ext)
      ? 'media' : 'original';
  }
  // Native WebMediaDragPayload / WebMediaPayload allow an optional username
  // before /p/, /reel/, /reels/, or /tv/ (Instagram profile-grid links).
  if (/^\/(?:[A-Za-z0-9._]{1,30}\/)?(?:reel|reels|tv)\/[^/]+/i.test(url.pathname))
    return 'instagram-video';
  if (/^\/(?:[A-Za-z0-9._]{1,30}\/)?p\/[^/]+/i.test(url.pathname))
    return 'instagram-photos';
  return 'original';
}

// Buttons the demo shows for a pasted link, as [English, Turkish] pairs.
export function mediaPreviewOptions(value) {
  switch (classifyMediaPreviewURL(value)) {
    case 'instagram-video': return [['Video', 'Video'], ['Audio', 'Ses']];
    case 'instagram-photos': return [['Carousel photos', 'Carousel görselleri']];
    case 'youtube': return [['Video', 'Video'], ['Audio', 'Ses'], ['Thumbnail', 'Thumbnail']];
    case 'image': return [['Image', 'Görsel']];
    case 'media': return [['Media file', 'Medya dosyası']];
    default: return [['Original media', 'Orijinal medya']];
  }
}
