# Nashi Windows Hap Modu — doğrulama kaydı

## Sabah Özeti

- Windows uygulamasına ayarlardan açılan, varsayılanı kapalı tek pencereli hap görünümü eklendi.
- Fareyle genişleme/daralma, Ctrl+Alt+N, üst kenar alanı ve sistem tepsisi akışları; Tepsi, Medya, Araçlar, Notlar, Yapılacaklar, Zamanlayıcı ve aramayla açılan ek modüller doğrulandı.
- Gerçek Windows medya oturumu, kamera aygıtı, farklı DPI/çoklu monitör, tam ekran bastırma ve Focus Assist davranışı bu oturumda donanım/oturum koşullarıyla sınanmadı.
- Görüntüler sentetik test girdileriyle üretildi; kamera başlatılmadı.
- Owner incelemesi gereken ürün kararları: yok. PR draft kalacak.

## İlerleme

- ✅ A — Tek şekil pencere, siyah stil, ayar, kısayol, sıcak kenar ve debug Stil Galerisi.
- ✅ B — Dock, arama paleti/yerel arama, Tepsi, olay tabanlı Medya görünümü ve 27 Araç kataloğu.
- ✅ C — Yerel Notlar, Yapılacaklar ve Zamanlayıcı.
- ✅ D — Takvim, Cep, Teleprompter ve kamera kapalı başlayan Ayna.

## Kararlar

- Tercihler ve modül verileri mevcut Windows depolama düzeniyle uyumlu JSON dosyalarında, bu bilgisayarda tutulur.
- DWM acrylic yerine tek, opak siyah WPF yüzeyi kullanılır; böylece şeklin kenarı ve ekran görüntüleri tutarlı kalır.
- Apple'a ait font/ikon kullanılmaz; Windows sistem yazı tipi ve metin tabanlı semboller kullanılır.
- Hap modu varsayılan olarak kapalıdır; mevcut ana pencere ve çark korunur.

## Testler

- `dotnet build windows/Nashi.Windows/Nashi.Windows.csproj -c Debug --no-restore` — geçti, 0 uyarı / 0 hata.
- `dotnet build windows/Nashi.Windows/Nashi.Windows.csproj -c Release` — geçti, 0 uyarı / 0 hata.
- `dotnet run --project windows/Nashi.Verify/Nashi.Verify.csproj -c Release` — 1.492 kontrol geçti.
- `dotnet run --project windows/Nashi.Windows/Nashi.Windows.csproj -c Debug --no-build -- --island-interaction-smoke` — 11 kontrol geçti.
- `--pill-mode-smoke` — 27/27 kontrol; gerçek fare hareketi/tıklaması, kısayol, gizlenme, yerel yazma, arama ve zamanlayıcı akışı.
- Stil Galerisi ve modül görüntüleri yalnızca sentetik veri kullanır.
- PDF edit shows only PDFs, preserves the requested action, and renders page content in the organizer thumbnail.

## Görüntüler

- Stil Galerisi: `verified-gallery/style-gallery.png`
- Hap ve modül akışları: `verified-final/*.png`
- Not alanı odağı altın renge, Ayna önizlemesi 260 px panel yüksekliğine sığacak ölçüye getirildi.

### Aşama B — çekirdek modüller

✅ Tepsi, Medya, Araçlar ve yerel arama; sentetik denetimde doğrulandı. Görseller: `verified-final/module-tray.png`, `module-media.png`, `module-tools.png`.

### Aşama C — üretkenlik modülleri

✅ Notlar, Yapılacaklar ve Zamanlayıcı; gerçek fare/klavye etkileşimi ve izole yerel veriyle doğrulandı. Görseller: `verified-final/module-notes-filled.png`, `module-todo-filled.png`, `module-timer.png`.

### Aşama D — ileri modüller

✅ Takvim, Cep, Teleprompter ve kamera kapalı başlangıçlı Ayna; kamera açılmadan sentetik denetimde görüntülendi. Gerçek cihaz izni/oturumu test edilmedi. Görseller: `verified-final/module-calendar.png`, `module-pocket.png`, `module-prompter.png`, `module-mirror.png`.