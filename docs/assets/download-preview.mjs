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

  if (!['instagram.com', 'www.instagram.com', 'm.instagram.com'].includes(host))
    return 'original';
  // Native WebMediaDragPayload / WebMediaPayload allow an optional username
  // before /p/, /reel/, /reels/, or /tv/ (Instagram profile-grid links).
  if (/^\/(?:[A-Za-z0-9._]{1,30}\/)?(?:reel|reels|tv)\/[^/]+/i.test(url.pathname))
    return 'instagram-video';
  if (/^\/(?:[A-Za-z0-9._]{1,30}\/)?p\/[^/]+/i.test(url.pathname))
    return 'instagram-photos';
  return 'original';
}
