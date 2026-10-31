using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageMagick;
using Nashi.Core;
using Nashi.Linux;
using Nashi.Linux.Editors;

static partial class EditorChecks
{
    // ---- Read QR Codes

    static string QrImage(string path, string text)
    {
        var data = new ZXing.BarcodeWriterPixelData { Format = ZXing.BarcodeFormat.QR_CODE, Options = new ZXing.QrCode.QrCodeEncodingOptions { Width = 240, Height = 240, Margin = 4, CharacterSet = "UTF-8" } }.Write(text);
        using var image = new MagickImage();
        image.ReadPixels(data.Pixels, new PixelReadSettings((uint)data.Width, (uint)data.Height, StorageType.Char, PixelMapping.BGRA));
        using var file = File.Create(path);
        image.Write(file, MagickFormat.Png);
        return path;
    }

    static void Qr(string work, string photo)
    {
        const string url = "https://example.com/nashi?q=ş";
        const string note = "javascript:alert(1)";
        var link = QrImage(Path.Combine(work, Name + " qr.png"), url);
        var other = QrImage(Path.Combine(work, "note qr.png"), note);
        var pdf = ImageOperations.Convert([other], "pdf", Directory.CreateDirectory(Path.Combine(work, "qr-pdf")).FullName, null, default)[0];
        using (var image = PhotoCodec.Read(link)) check(ImageTools.QrCodes(image).SequenceEqual([url]), "ZXing decodes a generated QR code");
        using (var none = PhotoCodec.Read(photo)) check(ImageTools.QrCodes(none).Count == 0, "A photo without a code has none");
        check(Capabilities.Tools([pdf]).Contains("readQRCode") && Capabilities.Tools([link, pdf]).Contains("readQRCode"), "Read QR Codes is offered for PDFs and for images with PDFs");

        var opened = new List<string[]>();
        var spawn = Desktop.Spawn;
        Desktop.Spawn = (program, args, _, _) => { opened.Add([program, .. args]); return true; };
        try
        {
            var reader = new QrReaderWindow([link, pdf, photo]);
            reader.Show();
            Wait(reader.Scanning);
            Dispatcher.UIThread.RunJobs();
            var texts = Texts(reader);
            check(reader.Results.Select(r => (r.Text, r.Page)).SequenceEqual([(url, (int?)null), (note, 1)]) && texts.Contains(url) && texts.Contains("Page 1") && texts.Contains("2 found"), "The reader lists each code with its file and PDF page, and the count");
            Button Find(string text) => reader.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == text);
            var opens = reader.GetVisualDescendants().OfType<Button>().Count(b => b.Content as string == "Open");
            check(opens == 1, "Open is offered for the web link only, not for javascript:");
            Find("Open").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            check(opened.Count == 1 && opened[0].SequenceEqual(["xdg-open", new Uri(url).AbsoluteUri]), "Open hands the link to xdg-open as one argument");
            Find("Copy all").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var copied = Wait(reader.Clipboard!.TryGetTextAsync());
            check(copied == url + "\n" + note && texts.Contains("Copy all") && reader.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Copied all"), "Copy all puts every code on the clipboard");
            reader.Close();

            var empty = new QrReaderWindow([photo]);
            empty.Show();
            Wait(empty.Scanning);
            Dispatcher.UIThread.RunJobs();
            check(Texts(empty).Contains("No QR codes this time.") && Texts(empty).Contains("0 found"), "No codes says so");
            empty.Close();
        }
        finally { Desktop.Spawn = spawn; }

        foreach (var (theme, language, suffix) in new[] { (Avalonia.Styling.ThemeVariant.Light, "en", "light"), (Avalonia.Styling.ThemeVariant.Dark, "en", "dark"), (Avalonia.Styling.ThemeVariant.Light, "tr", "tr") })
        {
            Avalonia.Application.Current!.RequestedThemeVariant = theme;
            L10n.Language = language;
            try
            {
                var window = new QrReaderWindow([link, pdf]);
                window.Show();
                Wait(window.Scanning);
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame();
                var frame = window.CaptureRenderedFrame();
                check(frame is not null, $"QR reader renders ({suffix})");
                smoke(frame, "editor-qr-" + suffix);
                window.Close();
            }
            finally { Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light; L10n.Language = "en"; }
        }
    }
}
