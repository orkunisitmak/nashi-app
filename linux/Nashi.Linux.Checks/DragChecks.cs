using System.Text;
using Nashi.Linux;

/// Drags from browsers: the formats, what is a file, and the download (cases from Filee's BrowserDragFormatsTests
/// and BrowserDragTests).
static partial class DragChecks
{
    public static async Task BrowserAsync(string root, Action<bool, string> check)
    {
        // Formats.
        check(BrowserDrop.ParseUriList("# comment\r\nhttps://a.example/x.png\r\n\r\nfile:///tmp/a%20b.png\n").SequenceEqual(["https://a.example/x.png", "file:///tmp/a%20b.png"]), "text/uri-list skips comments and blank lines");
        var moz = Encoding.Unicode.GetBytes("https://a.example/one.jpg\nOne\nhttps://a.example/two.pdf\nTwo").Concat(new byte[6]).ToArray();
        check(BrowserDrop.ParseMozUrl(BrowserDrop.DecodeText(moz)).SequenceEqual(["https://a.example/one.jpg", "https://a.example/two.pdf"]), "text/x-moz-url pairs read from UTF-16 bytes padded with NULs");
        check(BrowserDrop.DecodeText([0xFF, 0xFE, (byte)'h', 0, (byte)'i', 0]) == "hi" && BrowserDrop.DecodeText([0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i']) == "hi"
            && BrowserDrop.DecodeText("ğüş"u8.ToArray()) == "ğüş" && BrowserDrop.DecodeText(null) == "", "Text decodes as UTF-16 (with or without BOM) or UTF-8");
        check(BrowserDrop.ParseMozUrl("https://a.example/x.png\nTitle") is ["https://a.example/x.png"], "_NETSCAPE_URL gives the address, not the title");
        check(BrowserDrop.ParseDownloadUrl("image/png::https://a.example/x.png") is null && BrowserDrop.ParseDownloadUrl("nonsense") is null
            && BrowserDrop.ParseDownloadUrl("image/png:photo.png:https://a.example/p:8080/x.png") is { Name: "photo.png" } d && d.Url.Port == 443, "DownloadURL splits at its first two colons");
        check(BrowserDrop.ImageOnly("<a href='/p'><img alt=x src=\"https://a.example/cat.webp?w=2&amp;h=1\"></a>") == "https://a.example/cat.webp?w=2&h=1"
            && BrowserDrop.ImageOnly("<p>Look <img src='a.png'></p>") is null && BrowserDrop.ImageOnly("<img src=a.png><img src=b.png>") is null, "A picture is an HTML fragment of one image and no text");
        check(BrowserDrop.DecodeDataUrl("data:image/png;base64,iVBORw0KGgo=") is { Mime: "image/png", Bytes.Length: 8 } && BrowserDrop.DecodeDataUrl("data:,hi%20there") is { Mime: "text/plain" } t && Encoding.UTF8.GetString(t.Bytes) == "hi there"
            && BrowserDrop.DecodeDataUrl("data:image/png;base64,@@@") is null && BrowserDrop.DecodeDataUrl("data:image/png;base64," + new string('A', 4000), 100) is null, "data: URLs decode, and bad or oversized ones don't");

        // What is a file.
        IReadOnlyList<WebItem> From(params (string Type, string Text)[] types) => BrowserDrop.Items(type => types.FirstOrDefault(p => p.Type == type).Text);
        check(From(("text/uri-list", "https://a.example/files/report.pdf")) is [{ Name: "report.pdf" }], "A link to a PDF is a file");
        check(From(("text/uri-list", "https://a.example/a%20photo.HEIC")) is [{ Name: "a photo.HEIC" }], "A link's name is percent-decoded");
        check(From(("text/uri-list", "https://a.example/index.html\nhttps://a.example/setup.exe\nhttps://a.example/\nftp://a.example/x.png\nhttp://a.example/x.png\njavascript:alert(1)\nhttps://user:pw@a.example/x.png")).Count == 0, "Pages, programs, plain HTTP, FTP, scripts and addresses with credentials are not files");
        check(From(("text/html", "<p>Some text <img src=https://a.example/x.png></p>"), ("text/uri-list", "https://a.example/page")).Count == 0, "A text selection with a picture in it is not a file");
        check(From(("text/html", "<img src=\"/img/cat\">"), ("text/x-moz-url", "https://a.example/gallery\nGallery")) is [{ Name: "cat.jpg", Url.AbsoluteUri: "https://a.example/img/cat" }], "A dragged picture is its address, relative to the page, named .jpg without an extension");
        check(From(("text/html", "<img src=\"data:image/webp;base64,UklGRg==\">")) is [{ Name: "image.webp", Bytes.Length: 4 }], "An inline data picture is decoded");
        check(From(("DownloadURL", "application/pdf:Invoice 7.pdf:https://mail.example/att?id=7")) is [{ Name: "Invoice 7.pdf" }], "A download a page offers is named as the page says");
        check(From(("application/x-moz-file-promise-url", "https://a.example/raw/123"), ("application/x-moz-file-promise-dest-filename", "sunset.png")) is [{ Name: "sunset.png" }], "A Firefox file promise is a picture with its name");
        check(From(("text/uri-list", "https://a.example/a.png\nhttps://a.example/b.mp4\nhttps://a.example/a.png")) is [{ Name: "a.png" }, { Name: "b.mp4" }], "Several links give several files, once each");
        check(BrowserDrop.Sanitize("../../.bashrc", "x") == "bashrc" && BrowserDrop.Sanitize("a/b\\c:d*.png", "x") == "abcd.png" && BrowserDrop.Sanitize("", "image") == "image"
            && BrowserDrop.Sanitize(new string('n', 200) + ".jpg", "x") is { Length: 120 } longName && longName.EndsWith(".jpg"), "Names from the web are made safe, and a long one keeps its extension");

        // The local files of a drop come first; browser types only when there are none.
        var local = Path.Combine(root, "drag local ğ.png");
        File.WriteAllText(local, "x");
        var content = DropContent.From(new Dictionary<string, byte[]> { ["text/uri-list"] = Encoding.UTF8.GetBytes(new Uri(local).AbsoluteUri + "\r\nfile:///no/such/file.png\r\n"), ["text/x-moz-url"] = Encoding.Unicode.GetBytes("https://a.example/x.png\nx") });
        check(content.Files.SequenceEqual([local]) && content.Web.Count == 0, "A file manager drop gives its existing local files, percent-decoded");
        content = DropContent.From(new Dictionary<string, byte[]> { ["_NETSCAPE_URL"] = Encoding.UTF8.GetBytes("https://a.example/song.mp3\nSong") });
        check(content.Files.Count == 0 && content.Web is [{ Name: "song.mp3" }], "A browser drop gives web items");

        // XDG download folder.
        check(BrowserDrop.XdgDownload("# x\nXDG_DOWNLOAD_DIR=\"$HOME/İndirilenler\"\n", "/home/u") == "/home/u/İndirilenler" && BrowserDrop.XdgDownload("XDG_DOWNLOAD_DIR=\"$HOME/\"", "/home/u") is null
            && BrowserDrop.XdgDownload("XDG_MUSIC_DIR=\"$HOME/Music\"", "/home/u") is null, "The download folder follows user-dirs.dirs (a localized name; the home itself turns it off)");
        var home = Directory.CreateDirectory(Path.Combine(root, "home")).FullName;
        check(BrowserDrop.DownloadFolder(home) == Path.Combine(home, "Downloads"), "Without user-dirs.dirs downloads go to ~/Downloads");

        // Saving: data bytes written, HTTPS fetched through the (fake) download, a failure leaves nothing.
        var saved = BrowserDrop.Fetch;
        var fetched = new List<Uri>();
        BrowserDrop.Fetch = (url, path, _) => { fetched.Add(url); File.WriteAllText(path, url.AbsolutePath); return url.AbsolutePath.Contains("fail") ? throw new HttpRequestException("offline") : Task.CompletedTask; };
        try
        {
            var downloads = Directory.CreateDirectory(Path.Combine(root, "Downloads")).FullName;
            File.WriteAllText(Path.Combine(downloads, "a.png"), "already here");
            var paths = await BrowserDrop.SaveAsync([new("a.png", new Uri("https://a.example/a.png")), new("b.webp", null, [1, 2, 3])], downloads, CancellationToken.None);
            check(paths.Count == 2 && Path.GetFileName(paths[0]) != "a.png" && File.ReadAllText(Path.Combine(downloads, "a.png")) == "already here" && File.ReadAllBytes(paths[1]).Length == 3 && fetched.Count == 1,
                "Downloads take a free name beside what is there and never replace it");
            var failed = false;
            var before = Directory.GetFiles(downloads).Length;
            try { await BrowserDrop.SaveAsync([new("c.png", null, [1]), new("d.png", new Uri("https://a.example/fail.png"))], downloads, CancellationToken.None); }
            catch (HttpRequestException) { failed = true; }
            check(failed && Directory.GetFiles(downloads).Length == before, "A failed download removes what that drop saved");
            var refused = false;
            try { await BrowserDrop.SaveAsync([new("e.png", new Uri("http://a.example/e.png"))], downloads, CancellationToken.None); }
            catch (InvalidDataException) { refused = true; }
            check(refused, "A plain HTTP item is refused before downloading");
        }
        finally { BrowserDrop.Fetch = saved; }
        var https = false;
        try { await Nashi.Core.VerifiedDownload.FetchAsync(new Uri("http://127.0.0.1:9/x.png"), Path.Combine(root, "never.png"), 10, null, CancellationToken.None); }
        catch (InvalidDataException) { https = true; }
        check(https && !File.Exists(Path.Combine(root, "never.png")), "The real download refuses anything but HTTPS");
    }
}
