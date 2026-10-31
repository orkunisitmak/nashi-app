using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Nashi.Core;
namespace Nashi.Linux;

/// Turkish UI from the shared catalog (the same one Windows and macOS use), plus the few Linux-only
/// strings. Every English text that reaches a control is swapped as it is set, so status messages
/// and dialogs are covered without wrapping each string.
internal static class Localizer
{
    public static string Language{get;set;}="en";
    [ThreadStatic]private static bool swapping;
    private static readonly Dictionary<string,string> Linux=new()
    {
        ["Web"]="Web",["Open terminal here"]="Terminalde aç",["Copy path"]="Yolu kopyala",["File actions"]="Dosya işlemleri",
        ["Download from the web"]="Web'den indir",["Paste a public video, audio or image link"]="Herkese açık bir video, ses veya görsel bağlantısı yapıştırın",
        ["Next to the original"]="Orijinalin yanına",["In one folder"]="Tek bir klasöre",["No folder chosen"]="Klasör seçilmedi",
        ["Downloads"]="İndirilenler",["Tools"]="Araçlar",["Language"]="Dil",["Restart Nashi to use the new language everywhere."]="Yeni dilin her yerde görünmesi için Nashi'yi yeniden başlatın.",
        ["No terminal app was found."]="Terminal uygulaması bulunamadı.",["Path copied."]="Yol kopyalandı.",["Copied. Paste it in your file manager."]="Kopyalandı. Dosya yöneticinize yapıştırın.",
        ["Install LibreOffice with your package manager to convert documents."]="Belgeleri dönüştürmek için LibreOffice'i paket yöneticinizle kurun.",
        ["Install FFmpeg with your package manager to use media tools."]="Medya araçları için Ayarlar'dan medya araçlarını kurun veya FFmpeg'i paket yöneticinizle kurun.",
        ["These files have no Linux preview action yet. Try one kind of file at a time."]="Bu dosyalar için henüz bir işlem yok. Tek türde dosyalarla deneyin.",
        ["Set up media tools first (Settings)."]="Önce medya araçlarını kurun (Ayarlar).",
        ["Make all pages the same width"]="Tüm sayfaları aynı genişlikte yap",["Save organized copy"]="Düzenlenmiş kopyayı kaydet",
        ["Select all"]="Tümünü seç",["Duplicate"]="Çoğalt",["Remove"]="Kaldır",["⟳ Rotate"]="⟳ Döndür",["◀ Move"]="◀ Taşı",["Move ▶"]="Taşı ▶",
        ["Keep at least one page."]="En az bir sayfa kalmalı.",["Reading pages…"]="Sayfalar okunuyor…",["Reading tags…"]="Etiketler okunuyor…",
        ["Choose image…"]="Görsel seç…",["Font size"]="Yazı boyutu",["Stroke"]="Çizgi",["Draw a mark first."]="Önce bir işaret çizin.",["No marks yet"]="Henüz işaret yok",["1 mark"]="1 işaret",
        ["Marker"]="İşaretleyici",["Rectangle"]="Dikdörtgen",["Arrow"]="Ok",["Line"]="Çizgi",["Ellipse"]="Elips",["Number"]="Numara",["Callout"]="Balon",["Magnifier"]="Büyüteç",["Freehand"]="Serbest çizim",["Select / Move"]="Seç / Taşı",
        ["Orange"]="Turuncu",["Red"]="Kırmızı",["Yellow"]="Sarı",["Green"]="Yeşil",["Blue"]="Mavi",["Black"]="Siyah",["White"]="Beyaz",["Note"]="Not",
        ["Remove all metadata"]="Tüm metadata'yı kaldır",["Save new copy"]="Yeni kopyayı kaydet",
        ["Editable text and images"]="Düzenlenebilir metin ve görseller",["Keep the exact look (page pictures)"]="Görünümü aynen koru (sayfa görselleri)",
        ["EPUB layout"]="EPUB düzeni",["Reflowable (text adapts to the reader)"]="Akışkan (metin okuyucuya uyar)",["Fixed pages"]="Sabit sayfalar",
        ["Download ExifTool (~11 MB) to edit fields"]="Alanları düzenlemek için ExifTool'u indir (~11 MB)",["Only remove all metadata"]="Yalnızca tüm metadata'yı kaldır",
        ["Download FFmpeg, yt-dlp and Deno from their official releases (SHA-256 verified)"]="FFmpeg, yt-dlp ve Deno'yu resmi sürümlerinden indir (SHA-256 doğrulanır)",["Not now"]="Şimdi değil",
        ["Paste link"]="Bağlantıyı yapıştır",["Find media"]="Medyayı bul",["Video quality"]="Video kalitesi",["Best available"]="En iyi",["Up to 1080p"]="1080p'ye kadar",["Up to 720p"]="720p'ye kadar",
        ["Paste a public media link to begin."]="Başlamak için herkese açık bir medya bağlantısı yapıştırın.",["Choose Find media to inspect this link."]="Bu bağlantıyı incelemek için Medyayı bul'u seçin.",
        ["Paste one complete public media link."]="Tek ve eksiksiz bir herkese açık medya bağlantısı yapıştırın.",["Finding the media…"]="Medya aranıyor…",
        ["A link. A file. Yours to work with."]="Bir bağlantı. Bir dosya. Üzerinde çalışmak sizin.",
        ["Instagram photo posts open a photo selection. Reel links offer video and audio. Public downloadable media only."]="Instagram fotoğraf gönderileri fotoğraf seçimi açar. Reels bağlantıları video ve ses sunar. Yalnızca herkese açık, indirilebilir medya.",
        ["video"]="video",["audio"]="ses",["thumbnail"]="kapak görseli",["photos"]="fotoğraflar",["image"]="görsel",
        ["Drop something good."]="Güzel bir şey bırakın.",["Pick a format or a tool below. Originals are never overwritten."]="Aşağıdan bir biçim veya araç seçin. Orijinaller asla değiştirilmez.",
        ["Images, videos, audio, PDFs, documents, spreadsheets, slides, subtitles and archives. Drop files here, choose them or paste with Ctrl+V."]="Görseller, videolar, ses, PDF'ler, belgeler, tablolar, sunumlar, altyazılar ve arşivler. Dosyaları buraya bırakın, seçin veya Ctrl+V ile yapıştırın.",
        ["Remove selected"]="Seçileni kaldır",["Show in folder"]="Klasörde göster",["Search Library"]="Kitaplıkta ara",["Finished files appear here."]="Biten dosyalar burada görünür.",
        ["Cancelled. Nothing was saved."]="İptal edildi. Hiçbir şey kaydedilmedi.",["Use"]="Kullan",["Folder"]="Klasör",["Output"]="Çıktı",["Change…"]="Değiştir…",["Choose folder…"]="Klasör seç…",
        ["Add at least one range."]="En az bir aralık ekleyin.",["Add range"]="Aralık ekle",["Aspect"]="Oran",["Cancelled."]="İptal edildi.",["Choose an image to place"]="Yerleştirilecek görseli seçin",
        ["Choose background image"]="Arka plan görseli seçin",["Could not play the audio."]="Ses çalınamadı.",["Could not read the video size."]="Video boyutu okunamadı.",["Geometry"]="Geometri",
        ["I / O mark the selected handle · ←/→ move it one second · Enter trims"]="I / O seçili tutamacı işaretler · ←/→ bir saniye kaydırır · Enter kırpar",
        ["Media is re-encoded until it fits the target; audio keeps its sample rate."]="Medya hedefe sığana kadar yeniden kodlanır; ses örnekleme hızını korur.",
        ["Order"]="Sıra",["Out"]="Çıkış",["Play selection"]="Seçimi çal",["Reading a frame…"]="Kare okunuyor…",["Replace with"]="Şununla değiştir",["Selection"]="Seçim",["Stop"]="Durdur",["Updating…"]="Güncelleniyor…",
        ["Find text"]="Metni bul",["Finding text…"]="Metin aranıyor…",["No text found."]="Metin bulunamadı.",["From"]="Başlangıç",["To"]="Bitiş",["end"]="son",["Enter a start before the end, within the video."]="Videonun içinde, bitişten önce bir başlangıç girin.",
        ["Set up media tools"]="Medya araçlarını kur",["Move to…"]="Taşı…",["Copy"]="Kopyala",["Cancel"]="İptal",["Save"]="Kaydet",["Undo"]="Geri al",["Clear"]="Temizle",["Tool"]="Araç",["Color"]="Renk",["Text"]="Metin",["Cover"]="Kapat",["Redact"]="Sansürle",
    };
    /// For sentences built from numbers or names, which a catalog lookup cannot match.
    public static string Pick(string english,string turkish)=>Language=="tr"?turkish:english;
    public static string T(string source)=>Language!="tr"?source:Linux.TryGetValue(source,out var text)?text:AppLocalization.Text(source,"tr");
    /// The language for a first start: the saved choice, otherwise the desktop's.
    public static string Initial(PreferencesStore preferences,string folder)=>File.Exists(Path.Combine(folder,"preferences.json"))?AppLocalization.Normalize(preferences.Value.Language):(Environment.GetEnvironmentVariable("LANG")??"").StartsWith("tr",StringComparison.OrdinalIgnoreCase)?"tr":"en";
    public static void Install()
    {
        TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>((c,e)=>{if(c is not SelectableTextBlock)Swap(c,TextBlock.TextProperty,e.NewValue);});
        ContentControl.ContentProperty.Changed.AddClassHandler<ContentControl>((c,e)=>Swap(c,ContentControl.ContentProperty,e.NewValue));
        HeaderedContentControl.HeaderProperty.Changed.AddClassHandler<HeaderedContentControl>((c,e)=>Swap(c,HeaderedContentControl.HeaderProperty,e.NewValue));
        TextBox.WatermarkProperty.Changed.AddClassHandler<TextBox>((c,e)=>Swap(c,TextBox.WatermarkProperty,e.NewValue));
        Window.TitleProperty.Changed.AddClassHandler<Window>((c,e)=>Swap(c,Window.TitleProperty,e.NewValue));
    }
    private static void Swap(AvaloniaObject target,AvaloniaProperty property,object? value)
    {
        if(Language!="tr"||swapping||value is not string source||source.Length==0)return;
        var text=T(source);if(text==source)return;
        swapping=true;try{target.SetValue(property,text);}finally{swapping=false;}
    }
}
