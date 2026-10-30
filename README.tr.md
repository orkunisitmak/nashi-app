# Nashi

[English](README.md) · [Türkçe özellikler ve kısayollar](https://orkunisitmak.github.io/nashi-app/tr/) · [Son sürüm](https://github.com/orkunisitmak/nashi-app/releases/latest)

Nashi, [Tangerine](https://tangerineformac.com/) uygulamasının sürükle ve çark etkileşiminden ilham alan bağımsız, açık kaynaklı bir dosya dönüştürme/düzenleme uygulamasıdır. Web’den medya indirme, QR okuma, geliştirilmiş crop/annotate araçları, klavyeyle medya düzenleme, canlı ilerleme ve imzalı otomatik güncelleme ekler. Tangerine veya LEAP STUDIO ile bağlantılı değildir; referans uygulamanın binary dosyaları veya görselleri dağıtılmaz.

[Etkileşimli web demosunu dene](https://orkunisitmak.github.io/nashi-app/tr/#playground): örnek dosyaları sürükle, wheel ve kısayolları kullan, 27 aracı keşfet. Desteklenen görsel/PDF/ses çıktıları gerçektir; masaüstüne özel işlemler önizleme olarak işaretlenir.

## Nashi’yi iş başında gör

[![Nashi gerçek ekran kaydı: Finder sürükleme, wheel, dönüştürme, crop ve video trim](docs/assets/showcase/nashi-demo.gif?v=50e9219b2083)](https://orkunisitmak.github.io/nashi-app/tr/#demo)

**[95 saniyelik ekran kaydını izle](https://orkunisitmak.github.io/nashi-app/tr/#demo)** · [MP4 aç / indir](https://orkunisitmak.github.io/nashi-app/assets/showcase/nashi-demo.mp4?v=50e9219b2083)

macOS 0.7.3 ile çekilmiş gerçek kullanım ekran kaydı: Finder’dan sürükleme, wheel’den JPG’ye dönüştürme, görsel kırpma ve In/Out tutamaçlarıyla video trim. Beklemeler ve uzun önizlemeler kısaltıldı; kalan hareketler normal hızda oynar. Video oynatıcıda Türkçe ve İngilizce altyazı bulunur.

| macOS · Ana ekran | macOS · Görsel kırp |
| :---: | :---: |
| [![Nashi Mac ana ekranı](docs/assets/showcase/mac-home.png)](docs/assets/showcase/mac-home.png) | [![Kare seçim ve oran kontrolleriyle Nashi kırpma editörü](docs/assets/showcase/mac-crop.png)](docs/assets/showcase/mac-crop.png) |
| **macOS · Video kırp** | **Windows · 0.7.5** |
| [![In/Out tutamaçları ve seçilen aralıkla Nashi video trim editörü](docs/assets/showcase/mac-trim.png)](docs/assets/showcase/mac-trim.png) | [![Koyu temada yerel Windows 0.7.5 ana ekranı](docs/assets/showcase/windows-home-preview.png)](docs/assets/showcase/windows-home-preview.png) |

Windows görüntüsü, [PR #1](https://github.com/orkunisitmak/nashi-app/pull/1) ile doğrulanan ve yayınlanan 0.7.5 kodunun yerel UI testinden alınmıştır. Demo dosyaları yalnızca Nashi görsellerini içerir; kişisel dosya veya hesap bilgisi gösterilmez.

## İndir ve kullan

- **[macOS ZIP · Apple Silicon, macOS 13+](https://github.com/orkunisitmak/nashi-app/releases/latest/download/Nashi-macOS-arm64.zip):** ZIP’i aç, Nashi.app’i Uygulamalar klasörüne taşı ve aç. macOS kataloğu 27 araç, 46 girdi formatı ve 276 dönüşüm yolu içerir.
- **[Windows x64 ZIP · yerel önizleme](https://github.com/orkunisitmak/nashi-app/releases/latest/download/Nashi-Windows-x64.zip):** ZIP’in tamamını kendine ait, yazılabilir bir klasöre çıkar ve Nashi.exe’yi aç. Ayrıca .NET kurmak gerekmez. Güncelleme için klasörün bütünlüğünü koru. [Windows özellikleri ve farkları](windows/README.md).

İlk açılışta **Kurulumu tamamla → Eksik araçları indir** akışı Medya ve Office araçlarını sırayla kurar. Bilgisayarında bulunan araçlar yeniden indirilmez; iptal veya hata sonrasında yalnızca eksik gruplarla devam edilir. **Araçlar hazır** durumu iki grup da kullanılabilir olduğunda gösterilir. İndirmeler doğrudan upstream’den gelir; hash’leri kurulumdan önce doğrulanır. Temel yerel görsel/PDF işlemleri hemen çalışır. [Sürümler, hash’ler ve lisanslar](docs/dependencies.md).

İlk public build’lerde Apple notarization / Windows Authenticode yayıncı imzası bulunmuyor. İşletim sistemi ilk açılışta onay isteyebilir. Nashi sistem güvenlik ayarlarını kapatmaz. Resmi yayınlarda SHA-256 dosyası vardır; her CI çıktısı resmi imzalı güncelleme sayılmaz.

## Kısayollar

macOS’ta Finder’da sürüklerken **Shift** dönüşüm, **Command** araçlar, **Control** dosya işlemleri çarkını açar. **Control–Shift–D** kopyalanmış public linki indirir. Sürükleme tuşları özelleştirilebilir. Windows’ta **Ctrl+O** dosya seçer; **Ctrl+Space** seçili dosyaların araç ve dönüşüm çarkını açar. Çarkta **Dönüştür** ile çıktı formatını seçebilirsin. Ana sayfadaki dosya listesinde **Delete**, seçili dosyaları asıllarını silmeden Nashi’den kaldırır.

Desteklenen medya editörlerinde **Space** oynat/duraklat, **sağ/sol ok** bir saniye ilerle/geri git, **I/O** aralık başlangıcı/sonu. Crop’ta sürükleyerek seç, **Shift** ile oranı kilitle, **Enter** ile kaydet. [Tam kısayol rehberi](https://orkunisitmak.github.io/nashi-app/tr/#shortcuts).

Dönüştürme ve düzenleme yerelde çalışır. Web indirmeleri seçtiğin siteye bağlanır. Analytics, bulut hesabı veya tarayıcı çerezi aktarma yok. Public güncellemeler GitHub hesabı/token istemez. macOS feed ve arşiv imzasını, Windows arşiv imzasını ve sürümünü doğrular. İşler tamamlanmadan veya iptal edilmeden güncelleme için uygulama kapanmaz. Windows eski uygulama klasörünü geri alınabilir yedek olarak tutar.

Kaynak kod [MIT](LICENSE) lisanslıdır; üçüncü taraf bileşenlerin kendi lisansları geçerlidir. [macOS build adımları](README.md#build) · [Windows build adımları](windows/README.md).
