using System.Text;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Nashi.Core;
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

    // ---- The drag gesture (cases from Filee's GestureDetectorTests): one key, Ctrl by default.

    public static void Gestures(Action<bool, string> check)
    {
        var detector = new GestureDetector { Threshold = 8 };
        var (started, ended, sourceAsked) = (0, 0, 0);
        var source = true;
        var dragging = true;
        detector.Source = (_, _) => { sourceAsked++; return source; };
        detector.Dragging = () => dragging;
        detector.DragStarted += (_, _) => started++;
        detector.DragEnded += (_, _) => ended++;
        void Drag(Modifiers atPress, Modifiers moving, int distance = 40)
        {
            detector.Down(100, 100);
            detector.Move(103, 101, atPress);
            for (var d = 10; d <= distance; d += 10) detector.Move(100 + d, 100, moving);
            detector.Up(100 + distance, 100);
        }
        check(detector.Key == Modifiers.Ctrl, "The gesture key is Ctrl by default");
        detector.Down(100, 100);
        detector.Move(104, 102, Modifiers.Ctrl);
        check(started == 0, "Inside the threshold nothing fires");
        detector.Move(130, 100, Modifiers.Ctrl);
        detector.Move(160, 100, Modifiers.Ctrl);
        check(started == 1 && detector.Active && sourceAsked == 1, "Ctrl held at the drag's start fires once past the threshold, asking the source once");
        detector.Up(160, 100);
        check(ended == 1 && !detector.Active, "Releasing the button ends the gesture");
        (started, ended) = (0, 0);
        Drag(Modifiers.None, Modifiers.None);
        check(started == 0 && ended == 0, "A drag with no key opens no wheel (an ordinary drag)");
        Drag(Modifiers.Ctrl, Modifiers.Ctrl | Modifiers.Shift);
        Drag(Modifiers.Shift, Modifiers.Shift);
        check(started == 0, "Another key, or Ctrl with another, opens nothing");
        detector.Down(100, 100);
        detector.Move(104, 100, Modifiers.Ctrl);
        detector.Move(130, 100, Modifiers.None); // Ctrl let go before the threshold
        check(started == 0, "Ctrl released mid-drag, before the threshold: no wheel");
        detector.Move(150, 100, Modifiers.Ctrl);
        detector.Move(170, 100, Modifiers.Ctrl);
        check(started == 1, "Ctrl pressed again later in the same drag (late, as for a copy) fires once");
        detector.Move(190, 100, Modifiers.None);
        detector.Up(190, 100);
        check(ended == 1, "Ctrl released after the wheel opened keeps the drag; the release ends it");
        started = 0;
        source = false;
        Drag(Modifiers.Ctrl, Modifiers.Ctrl);
        source = true;
        dragging = false;
        Drag(Modifiers.Ctrl, Modifiers.Ctrl);
        dragging = true;
        check(started == 0, "Not over a file manager or browser, or no drag-and-drop running (a rubber band): nothing fires");
        detector.Key = GestureDetector.Parse("Off");
        Drag(Modifiers.None, Modifiers.None);
        Drag(Modifiers.Ctrl, Modifiers.Ctrl);
        detector.Paused = true;
        detector.Key = Modifiers.Ctrl;
        Drag(Modifiers.Ctrl, Modifiers.Ctrl);
        detector.Paused = false;
        check(started == 0, "Off, or paused, nothing fires");
        check(GestureDetector.Parse("Ctrl") == Modifiers.Ctrl && GestureDetector.Parse("Shift+Alt") == (Modifiers.Shift | Modifiers.Alt) && GestureDetector.Parse("Ctrl+Bogus") == Modifiers.None
            && LinuxSettings.Keys.All(k => k == "Off" ? GestureDetector.Parse(k) == Modifiers.None : GestureDetector.Parse(k) != Modifiers.None), "Every Settings key maps to its modifiers, Off to none");
        detector.Key = GestureDetector.Parse("Alt");
        Drag(Modifiers.Alt, Modifiers.Alt);
        check(started == 1, "A key chosen in Settings (Alt) is the one that opens the wheel");
        detector.Key = Modifiers.Ctrl;

        // The X11 poll's samples: masks to modifiers, press and release edges, Tab once per press while watched.
        started = ended = 0;
        var sampler = new PointerSampler(detector);
        var flips = 0;
        sampler.Flip += () => flips++;
        check(PointerSampler.ModifiersOf(4 | 16 | 2) == Modifiers.Ctrl && PointerSampler.ModifiersOf(1 | 8) == (Modifiers.Shift | Modifiers.Alt), "X masks map to modifiers, ignoring Num Lock and Caps Lock");
        sampler.Sample(10, 10, 4 | 256);
        sampler.Sample(60, 10, 4 | 256 | 16);
        sampler.Sample(61, 10, 4 | 256, tabDown: true);
        sampler.WatchFlip = true;
        sampler.Sample(62, 10, 4 | 256, tabDown: true);
        sampler.Sample(63, 10, 4 | 256, tabDown: true);
        sampler.Sample(64, 10, 4 | 256);
        sampler.Sample(65, 10, 4 | 256, tabDown: true);
        sampler.Sample(65, 10, 4);
        check(started == 1 && ended == 1 && flips == 1, "Samples press, drag with Ctrl and release; a held Tab flips once, only while watched");

        // XDND, as made-up events: the files come in, the status follows the petals, a drop on one is taken.
        var accept = (int x, int y) => x < 100;
        var protocol = new XdndProtocol((x, y) => accept(x, y));
        var requests = new List<string>();
        var statuses = new List<bool>();
        var finishes = new List<bool>();
        DropContent? got = null;
        var lefts = 0;
        protocol.Request += requests.Add;
        protocol.Status += statuses.Add;
        protocol.Finished += finishes.Add;
        protocol.Dropped += (c, _, _) => got = c;
        protocol.Left += () => lefts++;
        var file = Path.Combine(Path.GetTempPath(), "nashi drag ş " + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllText(file, "x");
        try
        {
            protocol.Enter(["x-special/gnome-icon-list", "text/uri-list", "UTF8_STRING", "text/plain"]);
            protocol.Position(10, 10);
            check(requests is ["text/uri-list"] && statuses is [true], "A file manager drag: Nashi asks for text/uri-list first and accepts while it arrives");
            protocol.Reply(Encoding.UTF8.GetBytes("copy\n" + new Uri(file).AbsoluteUri + "\r\n"));
            check(requests is ["text/uri-list", "text/plain"], "Then the next wanted type");
            protocol.Reply(null);
            protocol.Position(500, 10);
            protocol.Position(20, 10);
            check(statuses.SequenceEqual([true, true, false, true]), "The data reply refreshes acceptance; off the petals the drop is refused, on one accepted");
            protocol.Drop();
            check(finishes is [true] && got is { Files: [var f] } && f == file, "A drop on a petal is taken, with the dragged file");
            protocol.Enter(["text/uri-list"]);
            protocol.Position(500, 10);
            protocol.Reply(Encoding.UTF8.GetBytes(new Uri(file).AbsoluteUri));
            protocol.Drop();
            check(finishes.SequenceEqual([true, false]) && lefts == 1, "A drop off the petals is refused, so it stays the source's (a normal Ctrl copy elsewhere is untouched)");
            protocol.Enter(["text/uri-list", "_NETSCAPE_URL"]);
            protocol.Position(5, 5);
            protocol.Drop();
            check(finishes.Count == 2, "A drop before the data waits for it");
            protocol.Reply(Encoding.UTF8.GetBytes("https://a.example/page"));
            protocol.Reply(Encoding.UTF8.GetBytes("https://a.example/cat.webp\nCat"));
            check(finishes.SequenceEqual([true, false, true]) && got is { Files.Count: 0, Web: [{ Name: "cat.webp" }] }, "…then answers it: a browser picture link comes as a web item");
            protocol.Enter(["text/uri-list"]);
            protocol.Position(5, 5);
            protocol.Leave();
            protocol.Reply(Encoding.UTF8.GetBytes(new Uri(file).AbsoluteUri));
            protocol.Drop();
            check(lefts == 2 && finishes.Count == 3, "A drag that leaves is forgotten");
            protocol.Enter(["text/uri-list"]);
            protocol.Position(5, 5);
            protocol.Reply(Encoding.UTF8.GetBytes("file:///no/such/file.png"));
            protocol.Position(6, 5);
            protocol.Drop();
            check(statuses[^1] == false && finishes[^1] == false, "A drag of nothing Nashi reads is refused");
        }
        finally { File.Delete(file); }
    }

    // ---- The drag wheel in the flow, with a recorder for the X drop target.

    public static void DragWheel(string root, Action<bool, string> check, Action<Avalonia.Media.Imaging.WriteableBitmap?, string> smoke)
    {
        var work = Directory.CreateDirectory(Path.Combine(root, "drag wheel")).FullName;
        var png = Path.Combine(work, "-ş photo.png");
        using (var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.Gold, 16, 16)) image.Write(png);
        Func<int, int, bool>? accepts = null;
        Action<DropContent, int, int>? hover = null, dropped = null;
        Action? refused = null;
        var targets = 0;
        var made = 0;
        var gone = 0;
        var saved = XdndTarget.Create;
        XdndTarget.Create = (_, _, a, h, _, d, r) => { (accepts, hover, dropped, refused) = (a, h, d, r); targets++; made++; return new Gone(() => gone++); };
        var ticks = 0;
        WheelFeedback.Player = _ => ticks++;
        try
        {
            var notes = new List<string>();
            var flow = new WheelFlow(new Toolchain(Path.Combine(work, "no-tools"), ""), null, notes.Add, () => Task.CompletedTask);

            // The drag source answers at once (a fake XdndSelection owner): the petals are there before any XDND message.
            var savedRead = XdndTarget.ReadDrag;
            XdndTarget.ReadDrag = _ => DropContent.From(new Dictionary<string, byte[]> { ["text/uri-list"] = Encoding.UTF8.GetBytes(new Uri(png).AbsoluteUri + "\r\n") });
            try
            {
                flow.ShowForDrag(new PixelPoint(400, 300));
                for (var i = 0; i < 100 && flow.LastWheel!.Wheel.Sides.Count == 0; i++) { Thread.Sleep(5); Dispatcher.UIThread.RunJobs(); }
                check(flow.LastWheel!.Known && flow.LastWheel.Wheel.Sides.Count == 2 && flow.LastWheel.Wheel.Items[0].Id == "group:images", "The gesture fills the wheel from the drag source's uri-list before the drag reaches it");
                refused!();
                Dispatcher.UIThread.RunJobs();
                check(notes is [var said] && said.StartsWith("Dropped between the petals"), "A drop on the centre is not lost silently: a card says nothing changed");
                flow.LastWheel.Close();
                Dispatcher.UIThread.RunJobs();
            }
            finally { XdndTarget.ReadDrag = savedRead; }
            notes.Clear();
            flow.ShowForDrag(new PixelPoint(400, 300));
            Dispatcher.UIThread.RunJobs();
            var wheel = flow.LastWheel!;
            check(wheel.IsVisible && !wheel.IsActive && wheel.Wheel.Items.Count == 0 && targets == 2, "The gesture shows an empty, unactivated wheel at the pointer and makes it a drop target");
            var (rect, scaling) = wheel.ScreenRect();
            // A point of the wheel control relative to the window in pixels (as XDND positions become), and one over a petal.
            (int X, int Y) At(Point p) => wheel.Wheel.TranslatePoint(p, wheel) is { } w ? ((int)Math.Round(w.X * scaling), (int)Math.Round(w.Y * scaling)) : (-1, -1);
            (int X, int Y) Screen(int petal)
            {
                var b = wheel.Wheel.Bounds;
                for (var y = 0.0; y < b.Height; y += 4)
                    for (var x = 0.0; x < b.Width; x += 4)
                        if (wheel.Wheel.IndexAt(new Point(x, y)) == petal) return At(new Point(x, y));
                return (-1, -1);
            }
            var content = new DropContent([png], []);
            var (px, py) = At(new Point(wheel.Wheel.Bounds.Width / 2, wheel.Wheel.Bounds.Height / 2 - 100)); // the top petal
            hover!(content, px, py);
            Dispatcher.UIThread.RunJobs();
            var sides = wheel.Wheel.Sides;
            check(sides.Select(x => x.Title).SequenceEqual(["Convert", "Tools"]) && sides[0].Items[0].Id == "group:images"
                && sides.All(s => s.Items.All(i => !i.Id.StartsWith("side:")))
                && sides[1].Items.SelectMany(i => i.Group ?? [i]).Any(i => i.Id == "action:copy"),
                "The drag wheel has only Convert and Tools, with actions in a group and no navigation petals");
            check(wheel.Wheel.Side == 0 && wheel.Wheel.HighlightedIndex == 0 && accepts!(px, py) && !accepts(rect.Width / 2, rect.Height / 2), "The petal under the drag lights up; the centre refuses a drop");

            // Live positions arrive in root coordinates, with the wheel somewhere on screen: each petal crossed
            // lights up and ticks, and off the ring the highlight clears.
            var live = new XdndProtocol(accepts!, () => (1200, 700));
            live.Hover += hover;
            live.Enter(["text/uri-list"]);
            wheel.Wheel.ShowSide(1); // the Tools face: eight petals
            Dispatcher.UIThread.RunJobs();
            var count = wheel.Wheel.Segments.Count;
            var crossed = new List<int>();
            Dispatcher.UIThread.RunJobs();
            var tickBefore = ticks;
            for (var k = 1; k <= count; k++)
            {
                var (lx, ly) = Screen(k % count);
                live.Position(1200 + lx, 700 + ly);
                Dispatcher.UIThread.RunJobs();
                crossed.Add(wheel.Wheel.HighlightedIndex);
            }
            check(count >= 8 && crossed.SequenceEqual(Enumerable.Range(1, count).Select(k => k % count)) && ticks == tickBefore + count,
                $"XdndPosition in root coordinates across {count} petals, the wheel at (1200, 700): {count} highlight changes and {count} ticks");
            live.Position(1200 + (int)(5 * scaling), 700 + (int)(5 * scaling));
            Dispatcher.UIThread.RunJobs();
            check(wheel.Wheel.HighlightedIndex == -1, "Outside the ring the highlight clears");
            wheel.Wheel.ShowSide(0);
            live.Position(1200 + px, 700 + py);
            Dispatcher.UIThread.RunJobs();
            var before = ticks;
            flow.FlipDrag();
            check(wheel.Wheel.Side == 1 && ticks == before + 1, "Tab during the drag switches directly to Tools, ticking once");
            flow.FlipDrag();
            check(wheel.Wheel.Side == 0 && wheel.Wheel.Page == 0, "The next Tab returns directly to Convert, with no intermediate page");
            smoke(wheel.CaptureRenderedFrame(), "drag-wheel-convert");
            // Hover opens a tool's outer ring without switching the main face.
            wheel.Wheel.ShowSide(1);
            var background = wheel.Wheel.Segments.ToList().FindIndex(i => i.Id == "group:background");
            var (sx, sy) = Screen(background);
            hover(content, sx, sy);
            for (var i = 0; i < 100 && wheel.Wheel.OpenGroupIndex < 0; i++) { Thread.Sleep(5); Dispatcher.UIThread.RunJobs(); }
            check(wheel.Wheel.Side == 1 && wheel.Wheel.OpenGroupIndex == background, "Resting on Background opens its outer ring and keeps Tools visible");
            wheel.Wheel.ShowSide(0);
            var jpgPetal = wheel.Wheel.Items.ToList().FindIndex(i => i.Group?.Any(child => child.Id == "convert:jpg") == true);
            wheel.Wheel.OpenGroup(jpgPetal);
            var children = wheel.Wheel.SubItems;
            var child = children.ToList().FindIndex(i => i.Id == "convert:jpg");
            var mid = (wheel.Wheel.Items.Count == 3 ? 180 : 90) - jpgPetal * 360.0 / wheel.Wheel.Items.Count;
            var span = Wheel.SubSpan(360.0 / wheel.Wheel.Items.Count, children.Count);
            var angle = Wheel.SubMid(mid, child, children.Count, span) * Math.PI / 180;
            var radius = (Wheel.SubInner + Wheel.SubOuter) / 2;
            var (jx, jy) = At(new Point(wheel.Wheel.Bounds.Width / 2 + radius * Math.Cos(angle), wheel.Wheel.Bounds.Height / 2 - radius * Math.Sin(angle)));
            dropped!(content, jx, jy);
            for (var i = 0; i < 400 && !File.Exists(Path.Combine(work, "-ş photo.jpg")); i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
            Wait(flow.Current);
            check(File.Exists(Path.Combine(work, "-ş photo.jpg")) && !wheel.IsVisible && gone == made, "A drop on JPG converts the dragged file beside it, closes the wheel and drops the target");

            // A drag that ends anywhere else closes the wheel and leaves the files alone.
            flow.ShowForDrag(new PixelPoint(400, 300));
            Dispatcher.UIThread.RunJobs();
            var second = flow.LastWheel!;
            hover(content, px, py);
            flow.DragEnded();
            for (var i = 0; i < 60 && second.IsVisible; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
            check(!second.IsVisible && Directory.GetFiles(work).Length == 2, "A drag released elsewhere closes the wheel and makes nothing");

            // A drop on a group keeps its outer ring open for a click, as an ordinary pointer wheel.
            flow.ShowForDrag(new PixelPoint(400, 300));
            Dispatcher.UIThread.RunJobs();
            var third = flow.LastWheel!;
            hover(content, px, py);
            Dispatcher.UIThread.RunJobs();
            third.Wheel.ShowSide(1);
            wheel = third;
            var turn = third.Wheel.Segments.ToList().FindIndex(i => i.Id == "group:background");
            var (tx, ty) = Screen(turn);
            dropped(content, tx, ty);
            for (var i = 0; i < 20; i++) { Thread.Sleep(5); Dispatcher.UIThread.RunJobs(); }
            flow.DragEnded();
            for (var i = 0; i < 40; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
            check(third.IsVisible && third.Wheel.Side == 1 && third.Wheel.OpenGroupIndex == turn, "A drop on a group opens its outer ring and keeps the wheel up for a choice");
            third.Close();
            Dispatcher.UIThread.RunJobs();
            check(gone == made, "Every drop target is gone once its wheel is");
        }
        finally
        {
            XdndTarget.Create = saved;
            SuccessToast.Current?.Close();
            Dispatcher.UIThread.RunJobs();
            WheelFeedback.Player = _ => { };
            WheelWindow.DwellTime = TimeSpan.FromMilliseconds(700);
        }
    }

    static void Wait(Task task)
    {
        for (var i = 0; i < 3000 && !task.IsCompleted; i++) { Thread.Sleep(5); Dispatcher.UIThread.RunJobs(); }
    }

    sealed class Gone(Action done) : IDisposable
    {
        public void Dispose() => done();
    }
}
