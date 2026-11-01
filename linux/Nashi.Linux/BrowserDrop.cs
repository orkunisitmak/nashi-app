// Adapted from Filee (MIT, © KnifeLemon and contributors): src/Filee.Core/Web/BrowserDragFormats.cs,
// src/Filee.Core/Web/BrowserDrag.cs and src/Filee.Platform.Linux/XdgUserDirs.cs. Nashi keeps the Linux formats
// (text/uri-list, text/x-moz-url, _NETSCAPE_URL, Firefox's file promise, DownloadURL, a picture-only HTML fragment,
// data: URLs) and decides by FeatureCatalog's inputs; downloads go through Core's VerifiedDownload.
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Nashi.Core;

namespace Nashi.Linux;

/// One file a browser drag stands for: a web address to download, or bytes it carried (a data: URL).
public sealed record WebItem(string Name, Uri? Url, byte[]? Bytes = null);

/// What the drag brought, read from the XDND types: local files, or the browser items to download.
public sealed record DropContent(IReadOnlyList<string> Files, IReadOnlyList<WebItem> Web)
{
    public static readonly DropContent None = new([], []);
    public bool Any => Files.Count > 0 || Web.Count > 0;

    /// Local files from text/uri-list (existing files only), else the browser formats.
    public static DropContent From(IReadOnlyDictionary<string, byte[]> data)
    {
        string? Text(string type) => data.TryGetValue(type, out var bytes) ? BrowserDrop.DecodeText(bytes) : null;
        var files = Desktop.ClipboardPaths(Text("text/uri-list") ?? "").Where(File.Exists).Distinct().ToList();
        if (files.Count > 0) return new(files, []);
        return new([], BrowserDrop.Items(Text));
    }
}

public static partial class BrowserDrop
{
    private static class Copy
    {
        public const string Https = "Only HTTPS downloads are supported.";
    }

    /// The most a dragged download may weigh.
    public const long MaxBytes = 2L << 30;

    /// Text from a drag type's bytes: UTF-16 (Firefox's text/x-moz-url, with or without a BOM) or UTF-8, up to a NUL.
    public static string DecodeText(byte[]? data)
    {
        if (data is null || data.Length == 0) return "";
        string text;
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE) text = Encoding.Unicode.GetString(data, 2, (data.Length - 2) & ~1);
        else if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) text = Encoding.UTF8.GetString(data, 3, data.Length - 3);
        else if (LooksLikeUtf16(data)) text = Encoding.Unicode.GetString(data, 0, data.Length & ~1);
        else text = Encoding.UTF8.GetString(data);
        var end = text.IndexOf('\0');
        return end >= 0 ? text[..end] : text;
    }

    // ASCII in UTF-16 has a zero in every odd byte; UTF-8 text has none.
    private static bool LooksLikeUtf16(byte[] data)
    {
        var length = data.Length;
        while (length > 0 && data[length - 1] == 0) length--;
        var pairs = Math.Min((length + 1) / 2, 64);
        if (pairs == 0) return false;
        var zeros = 0;
        for (var i = 0; i < pairs; i++) if (i * 2 + 1 >= length || data[i * 2 + 1] == 0) zeros++;
        return zeros * 2 >= pairs;
    }

    /// text/x-moz-url and _NETSCAPE_URL: lines alternate address and title.
    public static IReadOnlyList<string> ParseMozUrl(string? text) => Lines(text).Where((_, i) => i % 2 == 0).ToList();

    /// text/uri-list: one address per line; "#" lines are comments.
    public static IReadOnlyList<string> ParseUriList(string? text) => Lines(text).Where(l => !l.StartsWith('#')).ToList();

    /// DownloadURL ("image/png:photo.png:https://…"): name and address, or null.
    public static (string Mime, string Name, Uri Url)? ParseDownloadUrl(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        var first = text.IndexOf(':');
        var second = first < 0 ? -1 : text.IndexOf(':', first + 1);
        if (first <= 0 || second < 0) return null;
        var (mime, name, url) = (text[..first], text[(first + 1)..second], text[(second + 1)..]);
        return mime.Contains('/') && name.Length > 0 && Uri.TryCreate(url, UriKind.Absolute, out var uri) ? (mime, name, uri) : null;
    }

    /// The src of a fragment that is one picture and no text (a dragged image), or null.
    public static string? ImageOnly(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var images = ImageTag().Matches(html);
        if (images.Count != 1) return null;
        if (WebUtility.HtmlDecode(Tag().Replace(Comment().Replace(html, ""), "")).Any(c => !char.IsWhiteSpace(c))) return null;
        var src = WebUtility.HtmlDecode(images[0].Groups["src"].Value).Trim();
        return src.Length == 0 ? null : src;
    }

    /// A data: URL's media type and bytes, or null (not one, malformed, or over `maxBytes`).
    public static (string Mime, byte[] Bytes)? DecodeDataUrl(string? url, int maxBytes = 256 << 20)
    {
        if (url is null || !url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
        var comma = url.IndexOf(',');
        if (comma < 0) return null;
        var parts = url[5..comma].Split(';');
        var mime = parts[0].Trim().Length == 0 ? "text/plain" : parts[0].Trim().ToLowerInvariant();
        var payload = url[(comma + 1)..];
        try
        {
            if (parts.Skip(1).Any(p => p.Trim().Equals("base64", StringComparison.OrdinalIgnoreCase)))
            {
                payload = Uri.UnescapeDataString(payload).Replace(" ", "").Replace("\r", "").Replace("\n", "");
                return (long)payload.Length * 3 / 4 > maxBytes ? null : (mime, Convert.FromBase64String(payload));
            }
            var bytes = Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
            return bytes.Length > maxBytes ? null : (mime, bytes);
        }
        catch (FormatException) { return null; }
    }

    private static readonly Dictionary<string, string> MimeExtensions = new()
    {
        ["image/jpeg"] = "jpg", ["image/png"] = "png", ["image/webp"] = "webp", ["image/gif"] = "gif", ["image/avif"] = "avif",
        ["image/bmp"] = "bmp", ["image/tiff"] = "tiff", ["image/svg+xml"] = "svg", ["image/heic"] = "heic", ["application/pdf"] = "pdf",
    };

    /// Whether Nashi takes this name: a catalog input (or a PDF).
    public static bool Takes(string name) => FeatureCatalog.Extension(name) is { Length: > 0 } e && (FeatureCatalog.Conversions.ContainsKey(e) || e == "pdf");

    /// The files a browser drag stands for, from its types (`text(type)` gives a type's text, or null). In order:
    /// a picture (an HTML fragment that is one image; Firefox's file promise), a page's DownloadURL, then links whose
    /// name is a format Nashi takes (never a web page). Only HTTPS addresses and data: URLs are kept.
    public static IReadOnlyList<WebItem> Items(Func<string, string?> text)
    {
        var links = ParseMozUrl(text("text/x-moz-url")).Concat(ParseMozUrl(text("_NETSCAPE_URL"))).Concat(ParseUriList(text("text/uri-list"))).ToList();
        var page = links.Select(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) ? uri : null).FirstOrDefault(u => u is not null);
        if (ImageOnly(text("text/html")) is { } src && Address(src, image: true, page) is { } picture) return [picture];
        if (text("application/x-moz-file-promise-url")?.Trim() is { Length: > 0 } promised
            && Address(promised, image: true, null, text("application/x-moz-file-promise-dest-filename")?.Trim()) is { } promise) return [promise];
        if (ParseDownloadUrl(text("DownloadURL")) is { } download && Address(download.Url.OriginalString, false, null, download.Name, download.Mime) is { } offered) return [offered];
        return links.Distinct().Select(u => Address(u, image: false, null)).OfType<WebItem>().ToList();
    }

    /// The file at `address`, or null: not https or data:, a page, or a type Nashi doesn't take.
    public static WebItem? Address(string address, bool image, Uri? page, string? name = null, string? mime = null)
    {
        if (DecodeDataUrl(address) is { } data)
            return MimeExtensions.TryGetValue(data.Mime, out var ext) ? new WebItem(Sanitize(name, "image") is var n && Takes(n) ? n : Path.ChangeExtension(n, ext), null, data.Bytes) : null;
        Uri? url = null;
        if (address.Contains("://", StringComparison.Ordinal)) Uri.TryCreate(address, UriKind.Absolute, out url);
        else if (page is not null) Uri.TryCreate(page, address, out url);
        if (url is null || url.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(url.UserInfo)) return null;
        var file = Sanitize(name ?? Uri.UnescapeDataString(url.Segments.LastOrDefault() ?? ""), image ? "image" : "download");
        // A web page is never the file, even though HTML is a catalog input; a picture served from one keeps its address.
        if (Path.GetExtension(file).ToLowerInvariant() is ".html" or ".htm" or ".php" or ".asp" or ".aspx" or ".jsp" or ".cgi")
        {
            if (!image) return null;
            file = Path.GetFileNameWithoutExtension(file);
        }
        if (Takes(file)) return new WebItem(file, url);
        var typed = mime is not null && MimeExtensions.TryGetValue(mime, out var known) ? known : image ? "jpg" : null;
        return typed is null ? null : new WebItem(Path.GetFileNameWithoutExtension(file) + "." + typed, url);
    }

    /// A safe file name: no folders, no control or reserved characters, not hidden, at most 120 characters.
    public static string Sanitize(string? name, string fallback)
    {
        var clean = new string((name ?? "").Where(c => !char.IsControl(c) && c is not ('/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')).ToArray()).Trim().TrimStart('.');
        if (clean.Length > 120) clean = clean[..(120 - Path.GetExtension(clean).Length)] + Path.GetExtension(clean);
        return clean.Length == 0 ? fallback : clean;
    }

    /// The user's download folder (XDG_DOWNLOAD_DIR in ~/.config/user-dirs.dirs, else ~/Downloads).
    public static string DownloadFolder(string? home = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c && Path.IsPathRooted(c) ? c : Path.Combine(home, ".config");
        try
        {
            var file = Path.Combine(config, "user-dirs.dirs");
            if (File.Exists(file) && XdgDownload(File.ReadAllText(file), home) is { } folder) return folder;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return Path.Combine(home, "Downloads");
    }

    /// The XDG_DOWNLOAD_DIR line of a user-dirs.dirs file with $HOME expanded; null when absent or the home itself.
    public static string? XdgDownload(string text, string home)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("XDG_DOWNLOAD_DIR=", StringComparison.Ordinal)) continue;
            var value = line["XDG_DOWNLOAD_DIR=".Length..].Trim().Trim('"').Replace("$HOME", home, StringComparison.Ordinal);
            return value.StartsWith('/') && value.TrimEnd('/') != home.TrimEnd('/') ? value : null;
        }
        return null;
    }

    /// The download itself (HTTPS only, size-capped, no cookies: VerifiedDownload); the checks swap in their own.
    public static Func<Uri, string, CancellationToken, Task> Fetch { get; set; } = (url, path, token) => VerifiedDownload.FetchAsync(url, path, MaxBytes, null, token);

    /// Saves each item into `folder` under a unique name; the paths. A failure or cancel removes what it saved.
    public static async Task<IReadOnlyList<string>> SaveAsync(IReadOnlyList<WebItem> items, string folder, CancellationToken token)
    {
        Directory.CreateDirectory(folder);
        var saved = new List<string>();
        try
        {
            foreach (var item in items)
            {
                var path = SafePaths.Unique(folder, Sanitize(item.Name, "download"));
                saved.Add(path);
                if (item.Bytes is { } bytes) await File.WriteAllBytesAsync(path, bytes, token);
                else if (item.Url is { Scheme: "https" } url) await Fetch(url, path, token);
                else throw new InvalidDataException(Copy.Https);
            }
            return saved;
        }
        catch
        {
            foreach (var path in saved) try { File.Delete(path); } catch (IOException) { }
            throw;
        }
    }

    private static List<string> Lines(string? text) =>
        string.IsNullOrEmpty(text) ? [] : [.. text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    [GeneratedRegex("""<img\b[^>]*?\bsrc\s*=\s*(?:"(?<src>[^"]*)"|'(?<src>[^']*)'|(?<src>[^\s>]+))""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImageTag();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex Tag();

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Comment();
}
