# Linux ayrıntılı kullanım rehberi

[Linux özeti](README.tr.md) · [English](GUIDE.md) · [Proje](../README.md)

Bu, Nashi'nin **ilk Linux sürümüdür (v1)**. **Ayrıntılı testler devam ediyor.** Farklı dağıtımlar, masaüstleri, codec'ler ve gerçek dosyalarla uyumluluk henüz bütünüyle doğrulanmadı. Önizleme sürümü olarak değerlendirin. Uygulamanın paylaşılan sürüm numarası şu anda **0.7.14**; “Linux v1” ilk Linux sürümünü ifade eder.

.NET 10 ve Avalonia ile geliştirilen uygulama, Windows sürümüyle dönüşüm motorunu paylaşır. x86_64 AppImage çalışma zamanını içerir; çalıştırmak için ayrıca .NET kurmanız gerekmez. Geliştirme ve gerçek sürükle-bırak kontrolleri Ubuntu 22.04.5 / X11 üzerinde yapıldı. Mevcut araçların İngilizce/Türkçe çevirileri vardır; yeni masaüstü ekranları şu anda İngilizcedir. Aşağıdaki düğme adları İngilizce verilmiştir.

## İndirme ve çalıştırma

Linux dosyalarını içeren bir [yayından](https://github.com/orkunisitmak/nashi-app/releases) veya [Linux iş akışının](../.github/workflows/linux.yml) **Linux preview** çıktısından AppImage ve ona ait `.sha256` dosyasını indirin. Henüz yayımlanmış bir Linux release dosyası olmayabilir. Önizleme paketleri imzasızdır.

İndirdiğiniz klasörde:

```sh
sha256sum -c Nashi-Linux-x86_64.AppImage.sha256
chmod +x Nashi-Linux-x86_64.AppImage
./Nashi-Linux-x86_64.AppImage
```

FUSE nedeniyle açılmazsa:

```sh
./Nashi-Linux-x86_64.AppImage --appimage-extract-and-run
```

Normal AppImage çalıştırması için Ubuntu 22.04 üzerinde `sudo apt install libfuse2` kullanılabilir. Diğer dağıtımlarda paket adı değişebilir. Entegrasyonu kurmadan önce AppImage'ı kalıcı bir konuma taşıyın. `--version` uygulama sürümünü gösterir.

## Gereken ek araçlar

Görüntü işleme ve PDF kitaplıkları pakete dahildir. Tam medya desteği için araç kurulumundaki sabitlenmiş FFmpeg 9 sürümünü kullanın; eski dağıtım paketlerinde gereken filtreler eksik olabilir. Örneğin Ubuntu 22.04’ün FFmpeg 4.4 sürümü güncel motorla kare çıkaramaz. Diğer gereksinimler yapacağınız işleme bağlıdır:

| Araç | Ne için gerekli? | Kurulum |
| --- | --- | --- |
| FFmpeg / ffprobe | Ses-video dönüşümü, düzenleme, önizleme ve kare çıkarma | **Set up tools → Download missing tools** veya sistemin `ffmpeg` paketi. |
| LibreOffice | Office, belge, EPUB ve Pages dönüşüm yolları | Dağıtımın yerel paketini kurun. `soffice` veya `libreoffice`, `PATH` üzerinde olmalı. Nashi bunu indirmez. |
| ExifTool 13.59 + Perl | Görüntü ve PDF metadata düzenleme | ExifTool araç kurulumundan indirilip doğrulanır; `perl` sistemde ayrıca bulunmalı. |
| Deno | Pages dönüşümleri | **Download missing tools** üzerinden. |
| PulseAudio veya PipeWire PulseAudio uyumluluğu | Önizleme sesi ve tekerlek sesleri | Masaüstünüzün ses hizmeti. Olmazsa video önizlemesi sessiz çalışabilir. |
| `xdg-open` ve terminal | Dosya/bağlantı açma ve Terminal işlemi | Masaüstü araçları ve desteklenen bir terminal. |

Ubuntu/Debian üzerinde ihtiyacınız olan sistem araçları için:

```sh
sudo apt update
sudo apt install ffmpeg libreoffice perl
```

LibreOffice'in Snap ve Flatpak sürümleri kullanılmaz; yalıtılmış geçici klasörleri dönüşüm çalışanıyla uyumlu değildir. Dağıtımın yerel paketini kullanın. Araç indirme internet gerektirir ve sabitlenmiş dosyaları sağlama toplamıyla doğrular. Araçlar kurulduktan sonra dönüşümler yerelde çalışır. Kurulum, bağlantı indirme ekranı ve komutu için yt-dlp de sağlar.

## İki yüzlü tekerlek

**Choose files**, uygulamaya sürükleme veya kopyalanmış dosyaları **Ctrl+V** ile yapıştırma kullanılır. Uygulama içinde **Ctrl+Space**, seçili dosyaların tekerleğini açar; seçim yoksa dosya seçiciyi açar.

- **Convert:** Uygun hedefleri Images, Documents, Video, Audio, Archives ve Text & Data gruplarında gösterir.
- **Tools:** Dosyaya göre Edit, Background, Markup, Create, Frames, Pages, Compress, Metadata, Scan QR ve Files gibi araçları gösterir.

**Tab**, bu iki yüz arasında geçer. Grubun üzerinde kısa süre bekleyince dış halka açılır. Kısa başlıklar tek satırdır; işlemin tam adı seçim sırasında tekerleğin altında görünür. Hızlı işlemler doğrudan çalışır; ayar gerektirenler Save/Cancel içeren düzenleyiciyi açar.

X11 entegrasyonu çalışırken **Ctrl basılı tutup dosyayı sürükleyin**, grubun üzerinde bekleyin ve dış halkadaki seçeneğe bırakın.

## Neler yapabilir?

| Dosya ailesi | Girdiler | İşlemler |
| --- | --- | --- |
| Görüntü | JPG/JPEG, PNG, WebP, HEIC/HEIF, TIFF/TIF, SVG, AVIF, BMP | PNG, JPG, WebP, HEIC, AVIF, BMP, GIF, TIFF ve PDF hedefleri; HEIC çıktısı FFmpeg gerektirir. |
| Video / GIF | MP4, MOV, MKV, WebM, AVI, WMV, GIF, M4V | MP4, MOV, MKV, WebM, AVI, WMV, GIF çıktıları; JPG/PNG kareler; ses çıkarma. GIF'te ses çıkarma gösterilmez. |
| Ses | MP3, M4A, WAV, FLAC, OGG, OPUS, AIFF, WMA, AAC | MP3, WAV, FLAC, AAC, M4A, OGG, OPUS, AIFF, WMA hedefleri; ses araçları. |
| PDF | PDF | Sayfaları JPG/PNG yapma, mevcut metni TXT/Markdown'a çıkarma, belge dönüşümleri ve PDF araçları. |
| Belge / veri | PPT, PPTX, DOCX, Pages, MD, RTF, RTFD, textClipping, HTML, EPUB, TXT, XLSX, XLS, ODS, CSV, TSV | Kaynağa uygun belge, tablo, metin ve sayfa görüntüsü dönüşümleri; genellikle LibreOffice gerekir. |
| Arşiv | ZIP, TAR, GZ, RAR | Açma ve desteklenen ZIP/TAR/GZIP çıktıları. RAR oluşturma yoktur. |
| Altyazı | SRT, VTT | Altyazı ve metin dönüşümleri. |

Her girdi her çıktıya çevrilmez; seçim ve kurulu araçlar uygun yolları belirler. Karışık JPG/PNG seçiminde iki hedef de kullanılabilir ve zaten hedef biçimindeki dosyalar için de yeni kopya üretilir. Düzenlenen HEIC dosyaları PNG kaydedilir. Klasörler ZIP ve dosya işlemleri için seçilebilir, bırakılabilir veya yapıştırılabilir; adları .png gibi bitse de görsel sayılmazlar.

**Görüntü:** Kırpma, döndürme, çevirme, boyutlandırma, renk/ışık/detay ayarları, siyah-beyaz, düz arka plan kaldırma; renk/geçiş/görüntü arka planı, gölge ve köşeler; elle karartma/bulanıklaştırma/pikselleştirme; şekil, ok, kalem, yazı ve işaretlerle anotasyon; kolaj; kalite/boyut/hedef büyüklükle sıkıştırma; metadata ve QR okuma. Arka plan kaldırma düz zemin, kâğıt, logo ve imzalar içindir; karmaşık sahneler için yapay zekâ nesne ayırma değildir.

**PDF ve belgeler:** Sayfa sıralama, döndürme, çoğaltma, silme, bölme, birleştirme, sıkıştırma, metadata ve görüntülerden PDF oluşturma. PDF → görüntü her sayfa için ayrı çıktı verir. Metin çıkarma mevcut metin katmanını okur; metinsiz sayfalar otomatik yerel OCR ile tanınır. Düzenlenebilir belge dönüşümleri de tarama metnini içerir. Ayrı **Tools → Read → OCR** ekranında metni kontrol edip düzenleyerek TXT kaydedebilirsiniz. Bazı PDF düzenleyicileri parola sorar; genel dönüşüm akışında kapsamlı parola/seçenek ekranı henüz yoktur. Varsayılanlar 144 DPI sayfa görüntüsü, düzenlenebilir PDF yerleşimi ve akışkan EPUB'dır. LibreOffice işlemleri geçici ayrı profil, iptal desteği ve beş dakika zaman aşımıyla çalışır. Eski LibreOffice sürümlerinde Markdown düz metne düşebilir ve sonuç mesajı bunu belirtir. HTML dönüşümü tarayıcı betiklerini çalıştırmaz.

**Ses ve video:** Kırpma, bölme, hız, video karesi kırpma, anlık görüntüler, zaman aralıklı karartma, sessize alma, video birleştirme, sıkıştırma ve metadata; seste ses yüksekliği normalleştirme, kanal ayarları, bipleme/sessiz aralıklar ve görselleştirmeli video. JPG/PNG video hedefi kare düzenleyicisini açar; ilk kare hazır seçilidir, başka zamanlar ve aralıklar eklenebilir. Space oynatır, oklar ilerletir, Shift+oklar kare adımlar, I/O aralığı ve desteklenen yerde M bölme noktası belirler. Codec uyumu FFmpeg derlemesine bağlıdır.

**Dosya işlemleri / Float:** Files grubunda Copy, Move, Copy Path, Terminal, Share ve Float bulunur. Share dosyayı dosya yöneticisinde gösterir. Float, kalıcı dosya referansları tutan, kenara yaslanabilen ve daralabilen raftır; raftan kaldırma asıl dosyayı silmez. Tarayıcıdan sürüklemede yerel dosyalar, görüntü verileri ve desteklenen HTTPS indirmeleri alınır. İzlenen klasör kuralları Settings ekranından yönetilir.

## OCR

**Tools → Read → OCR**, görüntü ve PDF sayfalarından yerelde metin tanır. Türkçe/İngilizce seçimi, otomatik/blok/dağınık metin yerleşimleri, PDF sayfa aralığı ve parola alanı vardır. Varsa PDF metni korunur; taranmış sayfalar 300 DPI ile tanınır. Her dosyanın sonucu düzenlenebilir, kopyalanabilir ve kaynak yanına yeni UTF-8 TXT olarak kaydedilebilir. İptal veya pencereyi kapatma çalışan tanımayı durdurur.

Tesseract ve dil paketlerini sistem paket yöneticisiyle kurun. Ubuntu/Debian: `sudo apt install tesseract-ocr tesseract-ocr-eng tesseract-ocr-tur`. Tesseract 5 önerilir; uyumlu Tesseract 4 LSTM kurulumları desteklenir. Kurulum sonrası **Check again** kullanılır. Karartma aracı yerel Tesseract ile metin bölgelerini bulabilir; OCR metni kaçırabileceği için bölgeleri kaydetmeden önce gözden geçirin. Aranabilir PDF çıktısı henüz eklenmedi. Çok kareli görüntülerde ilk görüntü kullanılır; PDF sayfaları seçilebilir. [Teknik ayrıntılar](OCR-OPTIONS.md).

## Karışık seçimler

Convert her dosyaya uygun hedefleri değerlendirir; ortak hedefler önce gelir. PNG ve PDF birlikte JPG yapılabilir. Yalnızca bazı dosyalara uygun hedeflerde işlenecek dosya sayısı seçimden önce tekerleğin altında gösterilir. Uygun olmayan dosyalar ve klasörleri işleme alınmaz.

Zaten hedef biçimindeki görüntüler yeni kopyaya yeniden kodlanır; diğer desteklenen türler yeni bir dosyaya kopyalanır. Farklı motorların işlemleri ortak bir geçici çıktı işlemiyle yürütülür; sonraki dosya hata verirse önceki geçici çıktılar kaldırılır. Bu işlem görüntü ve belge içeriğini tek PDF'de birleştirmez; birleştirme için özel oluşturma araçlarını kullanın.

## Çıktı nereye kaydedilir?

**Varsayılan olarak yeni dosya kaynak dosyanın yanına kaydedilir.** Örneğin `Downloads/report.pdf` → JPG işlemi `Downloads/report Page 1.jpg` gibi sayfa görüntüleri oluşturur. Ayrı bir Nashi çıktı klasörü açılmaz.

Orijinaller korunur; isim çakışırsa benzersiz bir ad seçilir. Ayrı girdiler kendi klasörlerine, birleştirme işlemleri ilk girdinin yanına yazılır. Arşiv kendi yanındaki bir klasöre açılır. Kaynak klasör yazılabilir değilse başka konum seçmeniz istenir. Sonuç kartında gerçek yol ve **Show in folder** bulunur; kısaltılmış yolun üzerinde bekleyerek tamamını okuyabilirsiniz.

## Masaüstü entegrasyonu

```sh
./Nashi-Linux-x86_64.AppImage --install-integration
```

Arka planda çalışmayı ve oturum açılışında başlamayı kurar; görüntü/ses/video/PDF/ZIP için **Open With → Nashi** girdisi ve simge ekler. **Nautilus Scripts menüsüne seçenek eklemez.** Eski İngilizce ve Türkçe Nashi scriptleri temizlenir; diğer scriptler korunur.

Yönetilen dosyalar:

- `~/.config/autostart/nashi.desktop`
- `~/.local/share/applications/nashi.desktop`
- `~/.local/share/icons/hicolor/256x256/apps/nashi.png`

Eski Nashi Ctrl+Alt+N kısayolu da diğer kısayollar korunarak temizlenir. AppImage taşınırsa entegrasyonu yeniden kurun.

**Global Ctrl-sürükleme ve imleç konumunda tekerlek için X11 gerekir.** Bu Avalonia derlemesi Wayland oturumunda XWayland gerektirir. Wayland'de uygulama veya Open With kullanılabilir; tekerlek ortada açılır. Ctrl+Space yalnızca uygulama içindeki kısayoldur.

```sh
./Nashi-Linux-x86_64.AppImage --background
./Nashi-Linux-x86_64.AppImage --wheel /dosya/yolu/belge.pdf
./Nashi-Linux-x86_64.AppImage --actions /dosya/yolu/belge.pdf
./Nashi-Linux-x86_64.AppImage --uninstall-integration
```

Bu bayraklar arayüzü açar; terminalden arayüzsüz dönüşüm sağlamaz. Entegrasyonu kaldırma arka planı durdurur ve yönetilen girdileri temizler; dosyalarınız, tercihleriniz ve indirilmiş araçlar kalır.

Veriler `$XDG_DATA_HOME/Nashi` altında, normalde `~/.local/share/Nashi` konumundadır. `settings.json` içindeki `language`: `en`, `tr`, `system`; `gestureKey`: `Ctrl`, `Shift`, `Alt`, `Ctrl+Shift` gibi birleşimler veya `Off` olabilir. Ayarlar ekranından çıktı modu/klasörü, görünüm, sürükleme tuşu, OCR dili ve biçim varsayılanları değiştirilebilir.

## Eksikler ve test durumu

Kitaplık, Ayarlar, bağlantı indirme ve gerçek CLI komutları uygulanmıştır. Genel toplu önizleme, otomatik güncelleme, aranabilir PDF üretimi, yüz algılama ve kapsamlı işlem başına belge seçenekleri eksiktir. İşler sıralı çalışır. Metin bölgesi algılama yerel Tesseract ile yapılır; bölgeleri kaydetmeden önce kontrol edin. ARM64 CI işi henüz uzak ortamda çalışmadı; araç indirme x64 ile sınırlıdır. x64 DEB ve RPM önizlemeleri üretilebilir; Debian/Fedora sistemlerine kurulum henüz doğrulanmadı.

2026-11-02 yerel doğrulamasında sabitlenmiş FFmpeg 9, Deno 2.9.7 ve ExifTool 13.59 ile sistem LibreOffice kullanıldı. **4.878 Linux kontrolü** ve **gerçek medya işlemleri dahil 2.285 ortak motor kontrolü** geçti; Pages dahil **380 kaynak/hedef yolunun tamamı** atlama olmadan çalıştırıldı. ExifTool tur testi de geçti. Küçük örneklerle geçen kontroller her dosya varyantını ve dağıtımı kapsamaz. X11 testi gerçek PDF → JPG sürükle-bırak işlemini, aynı klasöre çıktıyı ve kaynağın değişmediğini doğrular. İlk hover zaman aşımından sonra, son üçü art arda olmak üzere dört tekrar geçti; aralıklı hatanın nedeni henüz kesinleşmedi. **Ayrıntılı testler sürmektedir.**

Sorun bildirirken uygulama sürümünü, dağıtımı, X11/Wayland oturumunu, kaynak/hedef türünü, araç sürümlerini ve hata metnini ekleyin.

## Kaynaktan derleme ve kontroller

Derlemek için **.NET 10 SDK**, `curl` ve `sha256sum` gerekir:

```sh
linux/scripts/build-linux-appimage.sh
dotnet run --project windows/Nashi.Verify -c Release
dotnet run --project linux/Nashi.Linux.Checks -c Release
dotnet run --project linux/Nashi.Linux.Checks -c Release -- --integration
```

Çıktılar `build/linux/Nashi-Linux-x86_64.AppImage` ve `.sha256` dosyasıdır. PDFium pakete eklenir; paketleme araçları sabit sürüm ve SHA-256 doğrulamasıyla alınır. Eksik araç gerektiren testler atlanıp raporlanır. Daha geniş kapsama için `NASHI_VERIFY_FFMPEG`, `NASHI_VERIFY_FFPROBE`, `NASHI_VERIFY_DENO` yürütülebilir yollarını sağlayın ve LibreOffice kurun.

Python 3 standart kitaplığı ve X11 ile gerçek XDND testi çalıştırılabilir. Yalıtılmış ekran için Xvfb ve xauth gerekir:

```sh
python3 linux/scripts/test-linux-native-drop.py
xvfb-run --auto-servernum python3 linux/scripts/test-linux-native-drop.py
```

Tam teknik ayrıntılar ve İngilizce kurulum açıklamaları için [English guide](GUIDE.md).

## Kitaplık, indirme ve komut satırı

Library son 50 tamamlanan işlemi gerçek çıktı yollarıyla saklar. Arama, yeniden seçim, dış uygulamada açma ve klasörde gösterme desteklenir. Eksik dosyalar geçmişten silinmez; yeniden seçime alınmaz. Download a link ekranı video, ses, doğrudan görüntü, fotoğraf ve küçük resim indirmeyi destekler; site kısıtlamaları değişebilir.

```sh
nashi doctor
nashi list /dosya/gorsel.png /dosya/belge.pdf
nashi convert /dosya/belge.pdf --to jpg --dpi 200
nashi tool quick:zip /dosya/klasor /dosya/belge.pdf
nashi ocr /dosya/tarama.pdf --language eng+tur --pages 1-3
nashi qr /dosya/kod.png
```

`nashi` komutu için entegrasyonu kurun ve `~/.local/bin` klasörünün PATH üzerinde olduğundan emin olun; öncesinde AppImage yolunu kullanabilirsiniz. CLI arayüzü başlatmaz. Varsayılan çıktı kaynakların yanıdır; `--output` tek hedef seçer. Ctrl+C iptal eder ve 130 kodu döndürür. Etkileşimli seçenek gerektiren araçlar grafik düzenleyicide kalır; `--help` desteklenen komutları listeler.

Klasör kopyalama bağlantıları, kaynak içindeki hedefi ve 4 GB/10.000 öğe üstü işleri reddeder. Farklı dosya sistemleri arasında klasör taşıma başarısız olabilir; kaynak korunur. Taşınan dosya geri alınamazsa kalan konumlar açıkça bildirilir.

Taşınabilir paket: `DOTNET=/dotnet/yolu bash linux/scripts/build-linux-portable.sh linux-x64`. Paketteki `nashi` doğrudan çalışır. İsteğe bağlı `install.sh`, kullanıcı kurulumunu yapar, önceki uygulamayı `.previous-*` yedeğinde tutar ve masaüstü entegrasyonunu kaydeder. Sistem bağımlılıklarını kurmaz.
