# Nashi showcase media

- `mac-home.png`, `mac-wheel.png`, `mac-crop.png`: genuine window captures from the public macOS 0.7.3 app, taken in an isolated demo session using Nashi's own brand artwork.
- `windows-home-preview.png`: native WPF UI capture from the tested development build at `0db59537dd4615fe37ed8153ad29d80de2a61587` ([PR #1](https://github.com/orkunisitmak/nashi-app/pull/1)). Empty black capture padding below the window was trimmed. This is ahead of the public Windows release.
- `mac-trim.png`: a window crop from the actual video trim recording, showing the selected In/Out range and playback controls.
- `nashi-demo.mp4`: a silent, 94.9-second real screen recording of the Mac 0.7.3 app. It shows a real Finder file drag with Shift, a wheel drop on JPG, image crop selection and Return export, a Command video drag into the Tools wheel, video playback, In/Out handle dragging, selected-range playback and export. Three desktop recordings were edited together; pauses and longer previews were shortened and all retained actions play at normal speed. A neutral desktop backdrop conceals unrelated windows. The app UI is the running app throughout.
- `nashi-demo.gif`: lightweight highlights cut from that recording for the README. The README links to the full video player for playback controls and captions.
- `demo-poster.jpg`: near-native-size crop of the conversion wheel at 2.65 seconds in the Nashi recording (`crop=640:360:6:85`). `demo-en.vtt` / `demo-tr.vtt`: English and Turkish captions.

No personal files, account information or reference-app artwork appear in these assets. The app captures contain test filenames and Nashi artwork only. Image metadata was stripped when preparing the public copies.

The MP4 uses H.264, YUV420P and fast-start metadata. Both language pages use a native HTML video player with controls, captions and a text transcript; playback never starts automatically. Keep the `media-src 'self'` content-security policy when rebuilding the pages with `scripts/build-site.py`.

The demo uses `Nashi poster.png` (Nashi's brand artwork) and a generated 12-second `Nashi motion.mp4`. The actual app created a 1280×640 JPG, a 410×410 PNG crop and a 7.066666-second trimmed video. Selected trim boundaries were 1.975 and 9.037 seconds; output duration follows the source frame rate. Both source files retained their original SHA-256 hashes. The capture session used separate app preferences, with no personal history or update credentials.
