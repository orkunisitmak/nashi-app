#!/usr/bin/env python3
"""Build both static language pages from the actual app catalog; no network or analytics."""
from pathlib import Path
import json,html,hashlib,re
R=Path(__file__).resolve().parent.parent
catalog=json.loads((R/'docs/assets/catalog.json').read_text())
css_revision=hashlib.sha256((R/'docs/assets/site.css').read_bytes()).hexdigest()[:12]
source=(R/'Sources/NashiCore/FeatureCatalog.swift').read_text()
demo_catalog=dict(catalog)
# The sample picker and format browser share the same family labels and line icons.
file_families=[
 ('image','Images','Görseller','jpg png webp heic tiff svg avif bmp gif','png jpg webp pdf','M4 5h16v14H4z M4 16l5-5 4 4 3-3 4 4 M15 8h.01'),
 ('video','Video','Video','mp4 mov mkv webm avi wmv','mp4 mov webm','M3 5h18v14H3z M10 9l5 3-5 3z'),
 ('audio','Audio','Ses','wav mp3 m4a flac ogg opus aiff wma','mp3 wav m4a','M4 10v4 M8 7v10 M12 4v16 M16 7v10 M20 10v4'),
 ('pdf','PDF','PDF','pdf','jpg png docx','M4 7h16l1 5-1 5H4l-1-5z M8 14c3-1 5-3 5-5 0 3 1 4 3 5'),
 ('document','Documents','Belgeler','docx pages md rtf rtfd textclipping html txt','docx pdf pages','M5 5h14 M5 10h14 M5 15h10 M5 19h10'),
 ('sheet','Spreadsheets','Tablolar','xlsx xls ods csv tsv','xlsx csv pdf','M4 5h16v14H4z M4 10h16 M4 14.5h16 M10 10v9 M15 10v9'),
 ('slides','Slides','Sunumlar','pptx ppt','pptx pdf png','M3 5h18v12H3z M12 17v4 M8 21h8 M7 13l3-3 3 2 4-3'),
 ('epub','EPUB','EPUB','epub','pdf docx txt','M12 6C9 4 6 4 3 5v13c3-1 6-1 9 1 3-2 6-2 9-1V5c-3-1-6-1-9 1z M12 6v13 M6 9h3 M15 9h3'),
 ('subtitle','Subtitles','Altyazılar','srt vtt','srt vtt txt','M4 5h16v14H4z M7 11h4 M14 11h3 M7 15h2 M12 15h5'),
 ('archive','Archives','Arşivler','zip tar gz rar','zip tar gz','M5 8h14v11H5z M4 5h16v3H4z M10 12h4')]
assert sorted(ext for family in file_families for ext in family[3].split())==sorted(catalog['conversionMatrix'])
demo_catalog['fileFamilies']=[
 {'id':key,'label':{'en':en,'tr':tr},'formats':formats.split(),'quickOutputs':outputs.split(),'glyph':glyph}
 for key,en,tr,formats,outputs,glyph in file_families]
for key in ['singleTools','multipleTools']:
 section=source.split('let '+key+':')[1].split('\n    ]')[0]
 demo_catalog[key]={k:re.findall(r'\.(\w+)',v) for k,v in re.findall(r'"(\w+)": \[([^\]]*)\]',section)}
# UI copy and palette come from the shipping apps, just like the tool matrix.
# Keep marketing copy independent; the interactive preview follows native UI.
wheel_titles=dict(re.findall(r'\.(\w+): "([^"]+)"',source.split('let toolNames:')[1].split('\n    ]')[0]))
models=(R/'Sources/NashiCore/Models.swift').read_text().split('public enum ToolKind:')[1].split('public struct EditRect:')[0]
editor_titles=dict(re.findall(r'case \.(\w+): return "([^"]+)"',models))
editor=(R/'Sources/NashiApp/ToolEditor.swift').read_text()
def simple_switch(section):
 result={}
 for cases,value in re.findall(r'case ([^:\n]+):\s*return "([^"\\]*)"\s*\n',section):
  for key in re.findall(r'\.(\w+)',cases): result[key]=value
 return result
hints=simple_switch(editor.split('var toolHint:String')[1].split('var primaryTitle:String')[0])
primary=simple_switch(editor.split('var primaryTitle:String')[1].split('var canApply:Bool')[0])
palette={key:{'light':light,'dark':dark} for key,light,dark in re.findall(r'\("(\w+)","(#[A-Fa-f0-9]+)","(#[A-Fa-f0-9]+)"\)',(R/'windows/Nashi.Windows/Theme.cs').read_text())}
translations=json.loads((R/'Sources/NashiCore/Resources/Turkish.json').read_text())
feedback_source=(R/'Sources/NashiApp/WheelFeedback.swift').read_text()
feedback={'voices':int(re.search(r'for _ in 0\.\.<(\d+)',feedback_source)[1]),'volume':float(re.search(r'player.volume = ([\d.]+)',feedback_source)[1]),'minimumInterval':float(re.search(r'minimumInterval: TimeInterval = ([\d.]+)',feedback_source)[1])}
ui_strings=set(wheel_titles.values())|set(editor_titles.values())|set(hints.values())|set(primary.values())|{'Fine-tune it. Save a fresh copy.','Convert','Tools','Actions','Float','Copy','Move','Copy Path','Terminal','Original stays safe','PREVIEW','MAKE IT YOURS','Save a fresh copy','Icons','List','Clear','Close','Appearance','Light','Dark'}
demo_catalog['presentation']={'wheelTitles':wheel_titles,'editorTitles':editor_titles,'hints':hints,'primaryTitles':primary,'palette':palette,'feedback':feedback,'translations':{text:translations[text] for text in sorted(ui_strings) if text in translations}}
(R/'docs/assets/demo/catalog.json').write_text(json.dumps(demo_catalog,ensure_ascii=False,separators=(',',':')))
play_revision=hashlib.sha256(b''.join((R/p).read_bytes() for p in ['docs/assets/playground.js','docs/assets/playground.css','docs/assets/wheel-geometry.mjs','docs/assets/format-search.mjs','docs/assets/download-preview.mjs','docs/assets/library-limit.mjs','docs/assets/file-selection.mjs','docs/assets/reset-state.mjs','docs/assets/demo/catalog.json','docs/assets/demo/wheel-selection.wav'])).hexdigest()[:12]
media_revision=hashlib.sha256((R/'docs/assets/showcase/nashi-demo.mp4').read_bytes()).hexdigest()[:12]
poster_revision=hashlib.sha256((R/'docs/assets/showcase/demo-poster.jpg').read_bytes()).hexdigest()[:12]
features=[
('compress','Compress','Sıkıştır','Image, video, audio and PDF compression. Choose quality, reduction percentage or target size where supported. An unreachable target returns an explanation rather than silently losing features.','Görsel, video, ses ve PDF sıkıştırma. Desteklenen dosya türleri için kaliteyi, küçültme oranını veya hedef boyutu seç. Nashi, hedefe ulaşılamadığında nedenini gösterir.'),
('removeMetadata','Metadata','Metadata','Inspect, edit or remove supported image, PDF and media metadata, including track and chapter fields. Export a new copy.','Desteklenen görsellerin, PDF’lerin ve medya dosyalarının parça ve bölüm bilgileri dahil üst verilerini incele, düzenle veya kaldır. Sonucu yeni bir kopyaya kaydet.'),
('editImage','Edit image','Görsel düzenle','Resize, rotate, flip and tune brightness, contrast and saturation, with an original/result preview.','Boyutlandır, döndür, yansıt; parlaklık, kontrast ve doygunluğu önizleyerek ayarla.'),
('annotateImage','Annotate','İşaretle','Independent freehand/marker and shape tools, text, arrows and Select / Move. Choose one drawing tool at a time.','Serbest çizim, işaretleme, şekil, metin ve ok araçlarını kullan. Çizdiklerini seçip taşı. Her seferinde bir araç kullanabilirsin.'),
('frameImage','Frame image','Çerçeve ekle','Create a polished image frame with configurable padding, background, corners and shadow.','Kenar boşluğu, arka plan, köşe ve gölge ayarlarıyla görsel çerçevesi oluştur.'),
('cropImage','Crop image','Görseli kırp','Drag to select, move or resize the crop. Hold Shift to lock its ratio; Return exports a cropped copy.','Sürükleyerek alan seç, taşı veya boyutlandır. Shift oranı kilitler; Enter kırpılmış kopyayı kaydeder.'),
('normalizeAudio','Normalize audio','Ses seviyesini eşitle','Analyze loudness, then export with integrated-loudness, loudness-range and true-peak targets.','Ses yüksekliğini analiz et. Hedef ses yüksekliğini, aralığını ve gerçek tepe değerini belirleyip yeni bir kopya kaydet.'),
('muteVideo','Remove audio','Video sesini kaldır','Create a silent video copy while keeping the video stream.','Video akışını koruyarak sessiz bir kopya oluştur.'),
('trimVideo','Trim video','Video kırp','Preview, drag In/Out handles, move the selection and export its range. Keyboard playback, seeking and I/O marks help refine cuts.','Videoyu önizle, başlangıç ve bitiş tutamaçlarını sürükle ve seçtiğin aralığı dışa aktar. Klavyeden oynatabilir, videoda gezinebilir veya I ve O ile aralığı belirleyebilirsin.'),
('splitPDF','Split PDF','PDF böl','Split selected pages into individual files or groups. Specify page ranges and pages per file.','Sayfa aralıklarını ve dosya başına sayfa sayısını seçerek PDF’yi tek tek veya gruplar halinde böl.'),
('organizePDF','Organize PDF','PDF düzenle','Reorder, remove and rotate pages. Optional uniform-width output is limited when document features cannot be safely preserved.','Sayfaları sırala, çıkar ve döndür. Eşit genişlik seçeneği, belge özelliklerinin güvenle korunabildiği dosyalarda kullanılır.'),
('extractArchive','Extract archives','Arşiv aç','Extract ZIP, TAR, GZIP and RAR into a new folder. Path and link validation keeps entries inside that folder.','ZIP, TAR, GZIP ve RAR arşivlerini yeni klasöre çıkar. Yol ve link denetimleri klasör dışına yazmayı engeller.'),
('createPDF','Images to PDF','Görsellerden PDF','Combine selected images into a new PDF in the chosen order.','Seçili görselleri istenen sırayla yeni bir PDF’de birleştir.'),
('mergePDF','Merge PDFs','PDF birleştir','Combine several PDFs while preserving supported page content and navigation. Originals remain unchanged.','Birden fazla PDF’yi desteklenen sayfa içeriği ve gezinme özellikleri korunarak birleştir; orijinaller değişmez.'),
('cropVideo','Crop video','Video alanını kırp','Select a region directly on the video preview. Drag, resize, use Shift ratio lock and Return to export.','Video önizlemesinde alan seç. Sürükle, boyutlandır; Shift ile oranı kilitle, Enter ile kopyayı dışa aktar.'),
('changeVideoSpeed','Change speed','Video hızını değiştir','Change video speed with matching audio tempo and preview controls.','Önizleme kontrolleriyle video hızını ve buna eşlik eden ses temposunu değiştir.'),
('joinVideos','Join videos','Videoları birleştir','Join multiple clips in order, adapting geometry, frame rate and audio layout to a consistent output.','Klipleri sırayla birleştir; boyut, kare hızı ve ses düzeni tutarlı bir çıktıya uyarlanır.'),
('videoSnapshots','Video snapshots','Videodan kare al','Choose timestamps and save still frames as new PNG files.','Zaman noktalarını seçip kareleri yeni PNG dosyaları olarak kaydet.'),
('splitVideo','Split video','Video böl','Split at markers, equal clip counts or intervals. Remove unwanted segments; save separately or join retained clips.','İşaretlerden, eşit klip sayısından veya aralıklardan böl. İstenmeyen parçaları çıkar; kalanları ayrı veya birleşik kaydet.'),
('audioToVideo','Audio to video','Sesten video','Create a waveform, spectrum, frequency-bar or still-image video from an audio file.','Ses dosyasından dalga biçimi, spektrum, frekans çubukları veya sabit görsel içeren bir video oluştur.'),
('trimAudio','Trim audio','Ses kırp','Waveform preview, In/Out handles and selected-range playback. Optional silence trimming and keyboard seeking.','Ses dalgasını önizle, tutamaçlarla aralığı belirle ve seçimini dinle. İstersen sessiz bölümleri kırp veya klavyeyle kayıtta gezin.'),
('audioChannels','Audio channels','Ses kanalları','Convert mono/stereo and set left/right levels, with channel previews.','Mono/stereo dönüşümü yap, sol/sağ seviyelerini ayarla ve kanalları önizle.'),
('createCollage','Create collage','Kolaj oluştur','Arrange several images in a configurable layout with gaps and background styling.','Birden fazla görseli aralık ve arka plan ayarlarıyla düzenlenebilir bir kolajda birleştir.'),
('redactImage','Redact image','Görselde gizle','Select areas and export with solid, blur or pixelation treatments. Always verify the resulting file before sharing.','Bir alan seç ve düz renkle kapat, bulanıklaştır veya pikselleştir. Paylaşmadan önce çıktıyı kontrol et.'),
('redactVideo','Redact video','Videoda gizle','Define rectangular regions and time ranges for solid, blur or pixelated redaction.','Dikdörtgen alanları ve zaman aralıklarını seç. Seçili alanları düz renkle kapat, bulanıklaştır veya pikselleştir.'),
('redactAudio','Redact audio','Seste gizle','Choose intervals to replace with a bleep or silence; preserve the rest of the audio.','Seçtiğin aralıkları bip sesiyle değiştir veya sessize al. Kaydın geri kalanı korunur.'),
('readQRCode','Read QR codes','QR kod oku','Scan supported images and PDF pages, review detected contents and export a report.','Desteklenen görsel ve PDF sayfalarını tara; bulunan içerikleri inceleyip rapor dışa aktar.')]
assert set(x[0] for x in features)==set(catalog['tools'])
shortcuts=[('⇧ + drag','Convert wheel','Dönüştürme çarkı'),('⌘ + drag','Tools wheel','Araç çarkı'),('⌃ + drag','File actions wheel','Dosya işlemleri çarkı'),('⌃ ⇧ D','Download a copied web link','Kopyalanan bağlantıdaki medyayı indir'),('← ↑ / → ↓','Previous / next wheel item','Önceki / sonraki çark öğesi'),('Home / End','First / last wheel item','İlk / son çark öğesi'),('Enter / Space','Choose highlighted wheel item','Çarktaki seçili öğeyi uygula'),('Esc / Tab','Close wheel','Çarkı kapat'),('Space','Play / pause in supported media editors','Medya editörlerinde oynat / duraklat'),('← / →','Seek backward / forward 1 second','1 saniye geri / ileri git'),('⇧ ← / ⇧ →','Video: 1 frame; audio: 0.1 second','Video: 1 kare; ses: 0,1 saniye'),('I / O','Set selection In / Out','Seçimin In / Out noktasını belirle'),('⇧ Space','Play the selected range','Seçili aralığı oynat'),('M','Add a split marker where supported','Desteklenen araçlarda bölme işareti ekle'),('Home / End','Go to beginning / end of preview','Önizlemenin başına / sonuna git'),('⇧ + crop drag','Lock crop aspect ratio','Kırpma oranını kilitle'),('Enter','Export crop in the crop editor','Kırpılmış kopyayı kaydet'),('⌘ S','Save from supported editors','Desteklenen editörlerde kaydet'),('⌘ ,','Open settings','Ayarları aç'),('⌘ Q','Quit Nashi','Nashi’den çık')]
E=html.escape
def icon(path):
 return f'<svg class="ui-icon" aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.65" stroke-linecap="round" stroke-linejoin="round"><path d="{path}"/></svg>'

download_icon=icon('M12 3v12 M7 10l5 5 5-5 M4 17v4h16v-4')
check_icon=icon('M4 12l5 5L20 6')
terminal_icon=icon('M3 4h18v16H3z M6 8l4 4-4 4 M13 16h5')
external_icon=icon('M13 5h6v6m0-6-9 9 M19 13v6H5V5h6')
forward_icon=icon('m9 6 6 6-6 6')
down_icon=icon('m6 9 6 6 6-6')
info_icon=icon('M21 12a9 9 0 1 1-18 0 9 9 0 1 1 18 0Z M12 11v5 M12 8h.01')

def format_families(tr):
 cards=[]
 for family,en,tt,formats,_,glyph in file_families:
  chips=[]
  for ext in formats.split():
   name=E(ext.upper())
   route_id=f'route-{ext}'
   outputs=', '.join(E(out.upper()) for out in catalog['conversionMatrix'][ext])
   route=f'<strong>{name} →</strong> {outputs}'
   if not chips: example=route
   chips.append(f'<li><details name="routes-{family}"><summary aria-describedby="{route_id}">{name}</summary><span class="format-route" id="{route_id}">{route}</span></details></li>')
  cards.append(f'<li class="format-family" data-family="{family}"><h3><span class="family-icon">{icon(glyph)}</span>{tt if tr else en}</h3><ul class="format-chips" role="list">{"".join(chips)}</ul><p class="format-route-peek" aria-hidden="true">{example}</p></li>')
 return '<ul id="format-families" class="format-families" role="list">'+"".join(cards)+'</ul>'

def showcase(assets,tr):
 choose=lambda en,tt:tt if tr else en
 video_url=f'{assets}showcase/nashi-demo.mp4?v={media_revision}'
 poster_url=f'{assets}showcase/demo-poster.jpg?v={poster_revision}'
 shots=[
  ('mac-home.png','macOS · Home','macOS · Ana ekran','Choose files and get to your next step.','Dosya seç ve bir sonraki adıma geç.'),
  ('mac-crop.png','macOS · Crop image','macOS · Görsel kırp','Drag a selection, lock its ratio and save a fresh copy.','Alanı sürükleyerek seç, oranı kilitle ve yeni kopyayı kaydet.'),
  ('mac-trim.png','macOS · Trim video','macOS · Video kırp','Preview a clip, drag In/Out and save the selected range.','Klibi önizle, In/Out tutamaçlarını sürükle ve seçilen aralığı kaydet.'),
  ('windows-home-preview.png','Windows · 0.7.5','Windows · 0.7.5','Native Windows 0.7.5 Home in the dark theme.','Koyu temada yerel Windows 0.7.5 ana ekranı.')]
 gallery=''.join(f'<figure class="screenshot-card"><a href="{assets}showcase/{name}"><img src="{assets}showcase/{name}" alt="{E(choose(en,tt))}" loading="lazy" width="{820 if name in ("mac-home.png","windows-home-preview.png") else 1000}" height="{780 if name=="mac-home.png" else 756 if name=="windows-home-preview.png" else 760}"></a><figcaption><strong>{E(choose(en,tt))}</strong><span>{E(choose(desc,tdesc))}</span></figcaption></figure>' for name,en,tt,desc,tdesc in shots)
 return f'''<section class="showcase" aria-labelledby="flow-heading">
<div class="demo-overview">
<div>
<h2 id="flow-heading">{choose('How Nashi works','Nashi nasıl çalışır?')}</h2>
<ol class="flow-steps">
<li><h3>{choose('Choose or drag a file','Dosya seç veya sürükle')}</h3><p>{choose('Pick a file in Nashi, or hold <kbd>Shift</kbd> while you drag it in Finder or File Explorer. Nashi opens a wheel with your output formats.','Nashi’de bir dosya seç ya da dosyayı Finder veya Dosya Gezgini’nde <kbd>Shift</kbd>’e basılı tutarak sürükle. Çark açıldığında kullanabileceğin çıktı formatlarını görürsün.')}</p></li>
<li><h3>{choose('Choose a format or tool','Format veya araç seç')}</h3><p>{choose('Convert to JPG, crop an image or trim a clip. Preview your edits and fine-tune them before saving.','JPG’ye dönüştür, bir görseli kırp veya videoyu kısalt. Kaydetmeden önce sonucu gör ve gerekirse ayarları değiştir.')}</p></li>
<li><h3>{choose('Save a new copy','Yeni kopyayı kaydet')}</h3><p>{choose('Find the result in your Library. The original stays where it was.','Sonucu Kitaplık’ta bulabilirsin. Asıl dosyan yerinde kalır.')}</p></li>
</ol>
</div>
<div class="demo-recording" id="demo">
<h3>{choose('Watch the Mac app in action','Mac uygulamasını iş başında izle')}</h3>
<p>{choose('A 95-second recording of Nashi 0.7.3 on macOS: the format wheel, image crop and video trim.','Nashi 0.7.3’ün macOS’teki 95 saniyelik kaydı: format çarkı, görsel kırpma ve video kısaltma.')}</p>
<div class="demo-player"><video controls playsinline preload="none" poster="{poster_url}" width="1440" height="810" aria-label="{choose('Nashi real screen recording','Nashi gerçek kullanım ekran kaydı')}"><source src="{video_url}" type="video/mp4"><track kind="captions" src="{assets}showcase/demo-en.vtt?v={media_revision}" srclang="en" label="English" {'' if tr else 'default'}><track kind="captions" src="{assets}showcase/demo-tr.vtt?v={media_revision}" srclang="tr" label="Türkçe" {'default' if tr else ''}><a href="{video_url}">{choose('Open the MP4 demo','MP4 demoyu aç')}</a></video></div>
<p class="demo-links"><a href="{video_url}" download>{download_icon}{choose('Download MP4','MP4 dosyasını indir')}</a><span>{choose('Silent · English and Turkish captions','Sessiz · Türkçe ve İngilizce altyazı')}</span></p>
<details class="demo-transcript"><summary>{choose('Read the tour','Turu metin olarak oku')}{down_icon}</summary><ol><li>{choose('Drag an image in Finder while holding Shift.','Finder’da görseli Shift’e basılı tutarak sürükle.')}</li><li>{choose('Drop on JPG in the conversion wheel to create a new copy.','Dönüştürme çarkında JPG’nin üzerine bırakarak yeni kopya oluştur.')}</li><li>{choose('In Crop Image, drag a selection. Shift locks the aspect ratio; Return saves a cropped copy.','Görsel kırpma aracında sürükleyerek alanı seç. Shift ile oranı kilitle, Enter ile kırpılmış kopyayı kaydet.')}</li><li>{choose('Drag a video while holding Command, then choose Trim Video. Space controls playback; drag In and Out to set the range. Preview it and save a fresh trimmed copy.','Command’a basılı tutarak videoyu sürükle ve video kırpma aracını seç. Boşluk tuşuyla oynat, tutamaçlarla başlangıcı ve bitişi belirle. Aralığı önizle ve yeni klibi kaydet.')}</li><li>{choose('Find the outputs in Freshly made and Finder. The demo creates a JPG, a 410×410 crop and a 7.07-second trimmed clip from a 12-second source; originals remain unchanged.','Çıktıları Freshly made ve Finder’da bul. Demoda JPG, 410×410 kırpılmış görsel ve 12 saniyelik kaynaktan 7,07 saniyelik klip oluşturulur; orijinaller korunur.')}</li></ol></details>
</div>
</div>
<details class="desktop-gallery">
<summary>{choose('Desktop screenshots','Masaüstü ekran görüntüleri')}{down_icon}</summary>
<div class="screenshot-grid">{gallery}</div>
<p class="subtle">{choose('Mac screenshots: 0.7.3. Windows screenshot: native UI test capture of released 0.7.5, verified in','Mac ekranları: 0.7.3. Windows ekranı, yayınlanan 0.7.5 kodunun yerel UI testinden alınmıştır:')} <a href="https://github.com/orkunisitmak/nashi-app/pull/1">PR #1</a>. {choose('Demo files use Nashi artwork only. Click any screenshot to see it in full.','Demo dosyalarında yalnızca Nashi görselleri kullanılır. Tam boyut için ekran görüntüsüne tıkla.')}</p>
</details>
</section>'''

def windows_section(assets,tr):
 choose=lambda en,tt:tt if tr else en
 # (topic, Mac, Windows): only the places where the operating system changes the experience.
 rows=[
  (('Drag wheels','Sürükleme çarkları'),('<kbd>Shift</kbd> / <kbd>⌘</kbd> / <kbd>⌃</kbd> while dragging in Finder','Finder’da sürüklerken <kbd>Shift</kbd> / <kbd>⌘</kbd> / <kbd>⌃</kbd>'),('<kbd>Shift</kbd> / <kbd>Ctrl</kbd> / <kbd>Alt</kbd> while dragging in Explorer','Dosya Gezgini’nde sürüklerken <kbd>Shift</kbd> / <kbd>Ctrl</kbd> / <kbd>Alt</kbd>')),
  (('Where Nashi waits','Nashi nerede bekler'),('Menu bar','Menü çubuğu'),('System tray','Sistem tepsisi')),
  (('Sharing files','Dosya paylaşma'),('AirDrop','AirDrop'),('Windows Share. AirDrop is Apple-only.','Windows Paylaşımı. AirDrop yalnızca Apple’da var.')),
  (('Keyboard','Klavye'),('<kbd>⌘ 1</kbd> Home, <kbd>⌘ 2</kbd> Library, <kbd>⌘ O</kbd> choose files','<kbd>⌘ 1</kbd> Ana ekran, <kbd>⌘ 2</kbd> Kitaplık, <kbd>⌘ O</kbd> dosya seç'),('<kbd>Ctrl 1</kbd>, <kbd>Ctrl 2</kbd>, <kbd>Ctrl O</kbd>, plus <kbd>Ctrl Space</kbd> for the selected files’ wheel','<kbd>Ctrl 1</kbd>, <kbd>Ctrl 2</kbd>, <kbd>Ctrl O</kbd>; seçili dosyaların çarkı için <kbd>Ctrl Boşluk</kbd>')),
  (('Arranging a dragged window','Sürüklenen pencereyi yerleştirme'),('<kbd>Shift</kbd> or <kbd>⌘</kbd>, eight layouts','<kbd>Shift</kbd> veya <kbd>⌘</kbd>, sekiz düzen'),('<kbd>Shift</kbd> or <kbd>Alt</kbd>, eight layouts. Elevated apps can refuse to move.','<kbd>Shift</kbd> veya <kbd>Alt</kbd>, sekiz düzen. Yönetici yetkisiyle çalışan uygulamalar taşınmayı reddedebilir.')),
  (('Video preview','Video önizleme'),('System codecs','Sistem kodekleri'),('Windows codecs, with a local FFmpeg preview copy for anything they can’t play','Windows kodekleri; oynatamadıkları için yerel bir FFmpeg önizleme kopyası')),
  (('HTML to PDF','HTML’den PDF’ye'),('Page scripts run, so script-drawn QR codes print','Sayfa betikleri çalışır, betikle çizilen QR kodları da basılır'),('Scripts are removed first, so script-drawn content is left out','Betikler önce kaldırılır, betikle çizilen içerik çıktıya girmez')),
 ]
 table=''.join(f'<tr><th scope="row">{choose(*t)}</th><td data-label="Mac"><span>{choose(*m)}</span></td><td data-label="Windows"><span>{choose(*w)}</span></td></tr>' for t,m,w in rows)
 notes=[
  (('One-time tool setup','Tek seferlik araç kurulumu'),('Video, Office and metadata tools download from pinned sources into your Nashi tools folder. No administrator rights needed.','Video, Office ve metadata araçları sabitlenmiş kaynaklardan Nashi araç klasörüne iner. Yönetici yetkisi gerekmez.')),
  (('Keep the folder together','Klasörü bir arada tut'),('Extract the whole ZIP into a writable folder. No separate .NET install; updates replace the folder in place.','ZIP’in tamamını yazılabilir bir klasöre çıkar. Ayrıca .NET kurman gerekmez; güncellemeler klasörü yerinde yeniler.')),
  (('Text recognition','Metin tanıma'),('OCR-assisted redaction needs a Windows OCR language installed.','OCR destekli gizleme için Windows’ta bir OCR dili yüklü olmalı.')),
  (('First launch','İlk açılış'),('The build has no Authenticode signature yet, so SmartScreen may ask first. Updates are signed and verified.','Sürümde henüz Authenticode imzası yok, bu yüzden SmartScreen önce onay isteyebilir. Güncellemeler imzalı ve doğrulanır.')),
 ]
 note_items=''.join(f'<li><strong>{choose(*h)}</strong><span>{choose(*b)}</span></li>' for h,b in notes)
 return f'''<section id="windows" class="windows" aria-labelledby="windows-heading">
<div class="windows-intro">
<div class="windows-copy">
<p class="eyebrow">Windows 0.7.5</p>
<h2 id="windows-heading">{choose('The same Nashi, at home on Windows','Windows’ta da aynı Nashi')}</h2>
<p class="windows-lead">{choose('The Windows app shares the Mac catalog: every format, every tool and the same three drag wheels. Only the parts that belong to the operating system change.','Windows uygulaması Mac ile aynı kataloğu kullanır: tüm formatlar, tüm araçlar ve aynı üç sürükleme çarkı. Yalnızca işletim sistemine ait kısımlar değişir.')}</p>
<ul class="windows-stats" role="list"><li><strong>46</strong><span>{choose('input formats','girdi formatı')}</span></li><li><strong>27</strong><span>{choose('tools','araç')}</span></li><li><strong>3</strong><span>{choose('drag wheels','sürükleme çarkı')}</span></li></ul>
<p class="windows-status">{check_icon}<span>{choose('Nothing in the catalog is Mac-only. AirDrop is the one Apple service; Windows Share takes its place.','Katalogda yalnızca Mac’e özel bir şey yok. Tek Apple hizmeti AirDrop; yerini Windows Paylaşımı alır.')}</span></p>
</div>
<figure class="windows-shot"><a href="{assets}showcase/windows-home-preview.png"><img src="{assets}showcase/windows-home-preview.png" alt="{choose('Nashi 0.7.5 Home on Windows in the dark theme','Koyu temada Windows’ta Nashi 0.7.5 ana ekranı')}" width="820" height="756" loading="lazy"></a><figcaption>{choose('Home on Windows, dark theme. Captured by the native UI test of the released code.','Windows’ta ana ekran, koyu tema. Yayınlanan kodun yerel arayüz testinden alındı.')}</figcaption></figure>
</div>
<div class="platform-diff">
<h3>{choose('What works the Windows way','Windows’a göre değişenler')}</h3>
<div class="table-wrap"><table><thead><tr><th scope="col"><span class="sr-only">{choose('Topic','Konu')}</span></th><th scope="col">Mac</th><th scope="col">Windows</th></tr></thead><tbody>{table}</tbody></table></div>
</div>
<div class="windows-notes">
<h3>{choose('Good to know before you start','Başlamadan önce bil')}</h3>
<ul role="list">{note_items}</ul>
<p class="windows-evidence">{choose('The released code passed 198 engine checks and two native runs of 231 assertions on a Windows runner.','Yayınlanan kod bir Windows test makinesinde 198 motor denetiminden ve 231 doğrulamalık iki yerel çalıştırmadan geçti.')} <a class="quiet-link" href="https://github.com/orkunisitmak/nashi-app/blob/main/windows/FEATURE-PARITY.md">{choose('Parity matrix and test evidence','Eşdeğerlik tablosu ve test kanıtları')} {external_icon}</a></p>
</div>
</section>'''

for language in ['en','tr']:
 tr=language=='tr';prefix='../' if tr else '';assets=prefix+'assets/';choose=lambda en,tt:tt if tr else en
 title=choose(('Convert files.','Keep them local.'),('Formatı değiştir.','Dosyan sende.'))
 other_language='en' if tr else 'tr'
 globe=icon('M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0ZM16 12a4 9 0 1 1-8 0 4 9 0 0 1 8 0ZM3 12h18')
 cards=''.join(f'<article class="tool" data-tool="{x[0]}"><h3>{E(x[2] if tr else x[1])}</h3><p>{E(x[4] if tr else x[3])}</p></article>' for x in features)
 keys=''.join(f'<tr><th scope="row"><kbd>{E(key)}</kbd></th><td>{E(tt if tr else en)}</td></tr>' for key,en,tt in shortcuts)
 doc=f'''<!doctype html>
<html lang="{language}" data-type="software"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src 'self' blob: data:; media-src 'self' blob:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self' blob:; base-uri 'none'; form-action 'none'">
<meta name="page-type" content="software"><meta name="site-kind" content="software">
<meta name="description" content="{choose('Nashi: open-source local file conversion, editing, web downloads and a playful drag wheel. All features, formats and keyboard shortcuts.','Nashi: açık kaynaklı yerel dosya dönüştürme, düzenleme, web indirme ve sürükleme çarkı. Tüm özellikler, formatlar ve kısayollar.')}">
<title>Nashi — {' '.join(title)}</title><link rel="icon" href="{assets}nashi.png"><link rel="stylesheet" href="{assets}site.css?v={css_revision}"><link rel="stylesheet" href="{assets}playground.css?v={play_revision}"><script src="{assets}vendor/pdf-lib.min.js" defer></script><script src="{assets}playground.js?v={play_revision}" defer></script></head>
<body><a class="skip" href="#main">{choose('Skip to content','İçeriğe geç')}</a><header><a class="brand" href="#"><img src="{assets}nashi.png" alt="" width="42" height="42">Nashi</a><nav aria-label="{choose('Main navigation','Ana gezinme')}"><a href="#playground">{choose("Try it","Dene")}</a><a href="#features">{choose('Features','Özellikler')}</a><a href="#formats">{choose('Formats','Formatlar')}</a><a href="#shortcuts">{choose('Shortcuts','Kısayollar')}</a><a class="language-link" href="{('../index.html' if tr else 'tr/index.html')}" lang="{other_language}" hreflang="{other_language}">{globe}{choose('Türkçe','English')}</a><a class="nav-github" href="https://github.com/orkunisitmak/nashi-app">GitHub {external_icon}</a></nav></header>
<main id="main"><div class="hero hero-stage" data-enter><div class="hero-copy"><h1>{title[0]} <span>{title[1]}</span></h1><p class="intro">{choose('Nashi isn’t another app window. Hold <kbd>Shift</kbd> while you drag any file, and a format wheel opens right where you are: in Finder, on the desktop, in File Explorer. The new copy lands next to the original.','Nashi yeni bir uygulama penceresi değil. Bir dosyayı <kbd>Shift</kbd>’e basılı tutarak sürükle; format çarkı Finder’da, masaüstünde ya da Dosya Gezgini’nde, tam orada açılır. Yeni kopya asıl dosyanın yanına kaydedilir.')}</p><div class="hero-actions"><a id="hero-download" class="button primary" href="#download">{download_icon}<span>{choose('Download Nashi','Nashi’yi indir')}</span></a><a class="quiet-link" href="https://github.com/orkunisitmak/nashi-app">{choose('Steal the source (legally)','Kaynak kodu al, güle güle kullan')} {external_icon}</a></div><p class="hero-download-meta">{choose('Free','Ücretsiz')} <span id="hero-download-platform">· {choose('Mac and Windows','Mac ve Windows')}</span><span id="hero-other-platforms" hidden> · <a href="#download">{choose('Other platforms','Diğer platformlar')}</a></span></p>
<div class="try-block" id="try-block"><h2 id="playground-heading"><span class="live-dot" aria-hidden="true"></span>{choose('Try it right here','Burada dene')}</h2><p class="try-step" id="wheel-keys-lead">{choose('<strong>Hold <kbd>Shift</kbd> while you drag a file</strong> in the window.','Penceredeki bir dosyayı <strong><kbd>Shift</kbd>’e basılı tutarak sürükle.</strong>')}</p></div></div>
<section id="playground" aria-labelledby="playground-heading"><div id="playground-content"><div class="live-loading"><p role="status">{choose('Loading the live demo…','Demo yükleniyor…')}</p><div class="loading-tabs" aria-hidden="true"><span></span><span></span><span></span></div><div class="loading-files" aria-hidden="true"><span></span><span></span><span></span></div></div><noscript><p class="demo-noscript">{choose('Enable JavaScript to try the live demo.','Canlı demoyu denemek için JavaScript’i aç.')}</p></noscript></div></section></div>

<section class="status" id="download">
<div><h2>{choose('Download for Mac or Windows','Mac veya Windows için indir')}</h2><ul class="check-list">
<li>{check_icon}{choose('Free and open source','Ücretsiz ve açık kaynaklı')}</li>
<li>{check_icon}{choose('Image and PDF tools work right away','Görsel ve PDF araçları hemen çalışır')}</li>
<li>{check_icon}{choose('One-time setup for video and Office tools','Video ve Office araçları için tek seferlik kurulum')}</li>
</ul></div>
<div class="platforms">
<div><div><strong>macOS</strong><span data-platform-requirements>macOS 13+ · Apple Silicon · ZIP</span></div><a class="button secondary" data-download-platform="mac" data-hero-label="{choose('Download for Mac','Mac için indir')}" href="https://github.com/orkunisitmak/nashi-app/releases/latest/download/Nashi-macOS-arm64.zip">{download_icon}{choose('Download for Mac','Mac için indir')}</a></div>
<div><div><strong>Windows</strong><span data-platform-requirements>Windows x64 · {choose('Preview release','Önizleme sürümü')} · ZIP</span></div><a class="button secondary" data-download-platform="windows" data-hero-label="{choose('Download for Windows','Windows için indir')}" href="https://github.com/orkunisitmak/nashi-app/releases/latest/download/Nashi-Windows-x64.zip">{download_icon}{choose('Download for Windows','Windows için indir')}</a></div>
<details class="first-launch">
<summary>{info_icon}{choose('First launch','İlk açılış')}{down_icon}</summary>
<p>{choose('macOS and Windows may ask for approval on first launch. The Mac build lacks Apple notarization and the Windows build lacks Authenticode signing. Continue only if you downloaded Nashi from the official release above.','Nashi’yi ilk kez açtığında macOS veya Windows onay isteyebilir. Mac sürümü Apple onayı, Windows sürümü ise Authenticode imzası taşımıyor. Nashi’yi yukarıdaki resmi sürüm bağlantısından indirdiysen devam et.')}</p>
<p><strong>macOS</strong> {choose('Move <code>Nashi.app</code> to Applications and try to open it. If macOS blocks it, go to <strong>System Settings → Privacy &amp; Security</strong>, click <strong>Open Anyway</strong>, and confirm.','<code>Nashi.app</code> dosyasını Uygulamalar klasörüne taşı ve açmayı dene. macOS uygulamayı engellerse <strong>Sistem Ayarları → Gizlilik ve Güvenlik</strong> bölümünden <strong>Yine de Aç</strong>’a tıkla ve onayla.')}</p>
<p><strong>Windows</strong> {choose('Extract the entire ZIP and open <code>Nashi.exe</code>. If SmartScreen appears, select <strong>More info</strong>, then <strong>Run anyway</strong> if available.','ZIP’in tamamını çıkar ve <code>Nashi.exe</code> dosyasını aç. SmartScreen uyarısı çıkarsa <strong>Ek bilgi</strong>’ye tıkla, ardından seçenek varsa <strong>Yine de çalıştır</strong>’ı seç.')}</p>
</details>
<a class="quiet-link release-notes" href="https://github.com/orkunisitmak/nashi-app/releases/latest">{choose('Release notes and checksums','Sürüm notları ve sağlama toplamları')} {external_icon}</a>
</div></section>
{showcase(assets,tr)}
<section id="features"><div class="section-heading"><h2>27 {choose('tools for your files','dosya aracı')}</h2><p>{choose('The shared Mac and Windows tool catalog. Native platform details are listed below.','Mac ve Windows’un ortak araç kataloğu. Platforma özgü ayrıntılar aşağıda listelenir.')}</p></div><div class="tool-grid">{cards}</div></section>
{windows_section(assets,tr)}
<section class="web-feature"><h2>{choose('Save media from a link','Bağlantıdan medya indir')}</h2><p>{choose('Paste a public video share link or direct media address. Choose video, MP3 or the original image/media, quality and destination. YouTube cards can offer video and thumbnail separately; Instagram reel cards need an actual reel link.','Herkese açık bir video bağlantısını veya doğrudan medya adresini yapıştır. Video, MP3 ya da özgün dosya seçeneklerinden birini seç, ardından kaliteyi ve kayıt klasörünü belirle. YouTube kartlarında video ve küçük resim ayrı sunulabilir. Instagram Reels için doğrudan Reels bağlantısı gerekir.')}</p><p>{choose('Use Control–Shift–D for a copied link, drag a browser card with Command, or drop it onto Home. Some players only expose a poster or blob address; Copy Share Link is the fallback. Supported sites change. Instagram photo posts open a photo picker; selected photos are saved in post order. Reels offer video and audio without a thumbnail action. DRM, live streams, login-required media, profile-wide downloads and video playlists are not supported.','Bağlantıyı kopyalayıp Control–Shift–D’ye bas. Tarayıcı kartını Command ile sürükleyebilir veya ana ekrana bırakabilirsin. Bazı oynatıcılar yalnızca kapak görselini veya geçici bir adresi verir. Böyle bir durumda paylaşım bağlantısını kopyala. Site desteği değişebilir. Instagram fotoğraf gönderilerinde seçtiğin görseller gönderideki sırayla kaydedilir. Reels’te küçük resim seçeneği yoktur. DRM korumalı içerikler, canlı yayınlar, giriş gerektiren medya, profilin tamamı ve video oynatma listeleri desteklenmez.')}</p><button id="try-download" class="button secondary">{choose("Try the download flow","İndirme akışını dene")} {forward_icon}</button></section>
<section id="formats"><div class="section-heading"><h2>{choose('Supported file formats','Desteklenen dosya formatları')}</h2><p>{choose('Choose an input format to see what Nashi can convert it to.','Nashi’nin hangi çıktıları sunabildiğini görmek için bir girdi formatı seç.')}</p></div><label class="search-label" for="format-search">{choose('Find an input or output format','Girdi veya çıktı formatı ara')}</label><input id="format-search" type="search" placeholder="PNG, MP4, PDF…" autocomplete="off"><p id="format-search-status" class="sr-only" role="status"></p>{format_families(tr)}<p class="format-note">{choose('Complex documents can lose some formatting. PDF to DOCX or XLSX rebuilds the document approximately. PDF to PPTX uses page images.','Karmaşık belgeler dönüştürülürken bazı biçimlendirmeler kaybolabilir. PDF’den DOCX veya XLSX’e geçerken belge yaklaşık olarak yeniden oluşturulur. PDF’den PPTX’e geçerken sayfalar görsel olarak aktarılır.')}</p></section>
<section class="actions"><h2>{choose('Share, organize and move files','Dosyalarını paylaş, düzenle ve taşı')}</h2><div class="action-list">{''.join('<span>'+E(v)+'</span>' for v in (['AirDrop','Float','Copy','Move','Copy path','Open in Terminal'] if not tr else ['AirDrop','Float rafı','Kopyala','Taşı','Yolu kopyala','Terminal’de aç']))}</div><p>{choose('Float keeps files in a small shelf. History can be searched, reused and revealed in Finder. Move explicitly moves originals; exports create new copies and do not overwrite existing names.','Float dosyaları küçük bir rafta tutar. Geçmişte ara, dosyaları yeniden kullan veya Finder’da göster. Taşı işlemi orijinali taşır; dışa aktarımlar yeni kopya oluşturur ve mevcut adların üzerine yazmaz.')}</p></section>
<section id="shortcuts"><div class="section-heading"><h2>{choose('Keyboard shortcuts','Klavye kısayolları')}</h2><p>{choose('macOS defaults. Drag modifiers are customizable. Editor shortcuts apply where that action is available; text fields and native controls retain their normal input behavior.','macOS varsayılanları. Sürükleme tuşları özelleştirilebilir. Editör kısayolları ilgili işlem destekleniyorsa çalışır; metin alanları ve yerel kontroller normal davranışını korur.')}</p></div><div class="table-wrap"><table><thead><tr><th>{choose('Shortcut','Kısayol')}</th><th>{choose('Action','İşlem')}</th></tr></thead><tbody>{keys}</tbody></table></div></section>
<section class="trust"><div><h2>{choose('Privacy and updates','Gizlilik ve güncellemeler')}</h2></div><div><h3>{choose('Privacy','Gizlilik')}</h3><p>{choose('Conversion and editing run locally. Web downloads contact the chosen site. No analytics, cloud account, browser-cookie import or subscription is built in.','Dönüştürme ve düzenleme yerelde çalışır. Web indirmeleri seçilen siteye bağlanır. Nashi kullanım verisi toplamaz, hesap veya abonelik istemez ve tarayıcı çerezlerini içe aktarmaz.')}</p><h3>{choose('Signed updates','İmzalı güncellemeler')}</h3><p>{choose('Official public updates require no GitHub token. macOS verifies its signed feed and archive; Windows verifies its archive signature and version. Active jobs finish before the app restarts.','Resmî güncellemeler için GitHub erişim anahtarı gerekmez. macOS güncelleme akışını ve arşiv imzasını, Windows da arşiv imzasını ve sürümünü doğrular. Uygulama yeniden başlamadan önce devam eden işlem tamamlanır.')}</p><h3>{choose('Transparent limitations','Açık sınırlamalar')}</h3><p>{choose('Complex document conversions can lose some formatting. Haptic feedback depends on your hardware.','Karmaşık belgeler dönüştürülürken bazı biçimlendirmeler kaybolabilir. Dokunsal geri bildirim donanıma bağlıdır.')}</p></div></section>
<section class="open-source">
<div><h2>{choose('Fork it to fit.','Kendi Nashi’ni yap.')}</h2><p>{choose('Want a different shortcut or a new format? Nashi’s source is yours to tinker with under MIT. Third-party tools keep their own licenses.','Başka bir kısayol ya da yeni bir format mı istiyorsun? Nashi’nin kaynak kodunu MIT lisansı kapsamında dilediğin gibi değiştirebilirsin. Üçüncü taraf araçların kendi lisansları geçerli.')}</p><a class="quiet-link" href="https://github.com/orkunisitmak/nashi-app#build">{choose('Build instructions','Derleme adımları')} {external_icon}</a></div>
<div class="terminal"><div>{terminal_icon}<span>{choose('Clone the repository','Depoyu klonla')}</span><a href="https://github.com/orkunisitmak/nashi-app">GitHub {external_icon}</a></div><pre><code><span aria-hidden="true">$ </span>git clone https://github.com/orkunisitmak/nashi-app.git
<span aria-hidden="true">$ </span>cd nashi-app</code></pre></div>
</section>
<section class="closing"><img src="{assets}nashi.png" alt="" width="56" height="56" loading="lazy"><h2>{choose('Your files have places to be.','Dosyaların yola çıkmaya hazır.')}</h2><p>{choose('Give them the right format before they go.','Yola çıkmadan önce formatlarını değiştir.')}</p><a class="button primary" href="#download">{download_icon}{choose('Download Nashi','Nashi’yi indir')}</a></section></main>
<footer><div class="footer-row"><a class="brand" href="#"><img src="{assets}nashi.png" alt="" width="34" height="34">Nashi</a><a href="https://github.com/orkunisitmak/nashi-app/blob/main/SECURITY.md">{choose('Security','Güvenlik')}</a><a href="https://github.com/orkunisitmak/nashi-app/blob/main/LICENSE">MIT</a></div>
<p class="credit">{choose('Inspired by <a href="https://tangerineformac.com/">Tangerine</a>. Nashi is not affiliated with or endorsed by Tangerine or LEAP STUDIO.','<a href="https://tangerineformac.com/">Tangerine</a>’den ilham aldı. Nashi’nin Tangerine ya da LEAP STUDIO ile bağlantısı yoktur ve bu kuruluşlar Nashi’yi desteklemez.')}</p></footer></body></html>'''
 out=R/'docs'/('tr/index.html' if tr else 'index.html');out.write_text(doc)
print('Built EN/TR pages and interactive catalog: 27 tools, 46 inputs, 276 routes,',len(shortcuts),'shortcuts each')
