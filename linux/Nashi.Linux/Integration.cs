using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Nashi.Core;

namespace Nashi.Linux;

/// `--install-integration`: autostart in the background (which hosts the Ctrl-drag wheel), an Open With entry and two
/// Nautilus scripts. Idempotent; it writes only the files below, and uninstall removes exactly those. Both remove the
/// GNOME Ctrl+Alt+N keybinding earlier versions added (only Nashi's own entry; everyone else's stay). External programs run with argument lists through `run` (program, args → stdout, or null on failure).
public static class Integration
{
    private static class Copy
    {
        public const string ScriptName = "Convert with Nashi";
        public const string ActionsScriptName = "Nashi actions";
        public const string Wrote = "Wrote {0}";
        public const string Removed = "Removed {0}";
        public const string ShortcutRemoved = "Removed the old Ctrl+Alt+N shortcut.";
        public const string Started = "Started Nashi in the background.";
        public const string Stopped = "Stopped the background Nashi.";
    }

    const string Schema = "org.gnome.settings-daemon.plugins.media-keys";
    public static readonly string KeybindingPath = "/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/nashi/";
    const UnixFileMode Executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    // Input formats the Linux app handles, as freedesktop MIME types.
    static readonly Dictionary<string, string> Mime = new()
    {
        ["aiff"] = "audio/x-aiff", ["avi"] = "video/x-msvideo", ["avif"] = "image/avif", ["bmp"] = "image/bmp", ["flac"] = "audio/flac",
        ["gif"] = "image/gif", ["heic"] = "image/heif", ["jpg"] = "image/jpeg", ["m4a"] = "audio/mp4", ["mkv"] = "video/x-matroska",
        ["mov"] = "video/quicktime", ["mp3"] = "audio/mpeg", ["mp4"] = "video/mp4", ["ogg"] = "audio/ogg", ["opus"] = "audio/x-opus+ogg",
        ["pdf"] = "application/pdf", ["png"] = "image/png", ["svg"] = "image/svg+xml", ["tiff"] = "image/tiff", ["wav"] = "audio/x-wav",
        ["webm"] = "video/webm", ["webp"] = "image/webp", ["wma"] = "audio/x-ms-wma", ["wmv"] = "video/x-ms-wmv", ["zip"] = "application/zip",
    };

    /// The program to start: the AppImage itself when running from one.
    public static string ExecutablePath => Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } image ? image : Environment.ProcessPath!;

    /// Image, audio, video, PDF and ZIP inputs from the feature catalog.
    public static IReadOnlyList<string> InputExtensions() =>
        FeatureCatalog.Conversions.Keys.Append("pdf").Distinct()
            .Where(e => FeatureCatalog.IsImage("x." + e) || FeatureCatalog.IsAudio("x." + e) || FeatureCatalog.IsVideo("x." + e) || e is "pdf" or "zip")
            .Order(StringComparer.Ordinal).ToList();

    public static string MimeTypes() => string.Concat(InputExtensions().Select(e => Mime[e] + ";").Distinct());

    public static string Autostart(string home) => Path.Combine(home, ".config", "autostart", "nashi.desktop");
    public static string OpenWith(string home) => Path.Combine(home, ".local", "share", "applications", "nashi.desktop");
    public static string Icon(string home) => Path.Combine(home, ".local", "share", "icons", "hicolor", "256x256", "apps", "nashi.png");
    static string Scripts(string home) => Path.Combine(home, ".local", "share", "nautilus", "scripts");
    static IEnumerable<string> ScriptNames => [Copy.ScriptName, AppLocalization.Text(Copy.ScriptName, "tr"), Copy.ActionsScriptName, AppLocalization.Text(Copy.ActionsScriptName, "tr")];
    public static string Script(string home) => Path.Combine(Scripts(home), L10n.T(Copy.ScriptName));
    public static string ActionsScript(string home) => Path.Combine(Scripts(home), L10n.T(Copy.ActionsScriptName));

    /// Double-quoted for sh, desktop-entry Exec and GLib's g_shell_parse_argv: \ " $ ` are escaped.
    public static string Quote(string value) => "\"" + Regex.Replace(value, @"[\\""$`]", @"\$0") + "\"";

    // Desktop-entry string values escape backslashes once more, and Exec doubles %.
    static string Exec(string exe, string args) => "Exec=" + (Quote(exe).Replace("\\", "\\\\").Replace("%", "%%")) + " " + args;

    public static IReadOnlyList<string> Install(string home, string exe, Func<string, IReadOnlyList<string>, string?> run)
    {
        var log = new List<string>();
        void Write(string path, string text, UnixFileMode? mode = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            if (mode is { } m) File.SetUnixFileMode(path, m);
            log.Add(L10n.F(Copy.Wrote, path));
        }

        Write(Autostart(home), $"[Desktop Entry]\nType=Application\nName=Nashi\n{Exec(exe, "--background")}\nIcon=nashi\nTerminal=false\nNoDisplay=true\nX-GNOME-Autostart-enabled=true\n");
        Write(OpenWith(home), $"[Desktop Entry]\nType=Application\nName=Nashi\n{Exec(exe, "%F")}\nIcon=nashi\nCategories=Utility;\nTerminal=false\nStartupWMClass=Nashi\nMimeType={MimeTypes()}\n");
        Directory.CreateDirectory(Path.GetDirectoryName(Icon(home))!);
        using (var icon = new ImageMagick.MagickImage(typeof(Integration).Assembly.GetManifestResourceStream("Nashi.Icon.png")!))
        {
            icon.Resize(256, 256);
            icon.Write(Icon(home), ImageMagick.MagickFormat.Png);
        }
        log.Add(L10n.F(Copy.Wrote, Icon(home)));
        foreach (var other in ScriptNames.Select(n => Path.Combine(Scripts(home), n)).Where(p => p != Script(home) && p != ActionsScript(home) && File.Exists(p))) File.Delete(other); // the other language's names
        Write(Script(home), $"#!/bin/sh\nexec {Quote(exe)} --wheel \"$@\"\n", Executable);
        Write(ActionsScript(home), $"#!/bin/sh\nexec {Quote(exe)} --actions \"$@\"\n", Executable);
        run("update-desktop-database", [Path.GetDirectoryName(OpenWith(home))!]);
        if (RemoveShortcut(run)) log.Add(L10n.T(Copy.ShortcutRemoved));
        return log;
    }

    public static IReadOnlyList<string> Uninstall(string home, Func<string, IReadOnlyList<string>, string?> run)
    {
        var log = new List<string>();
        foreach (var path in new[] { Autostart(home), OpenWith(home), Icon(home) }.Concat(ScriptNames.Select(n => Path.Combine(Scripts(home), n))))
        {
            if (!File.Exists(path)) continue;
            File.Delete(path);
            log.Add(L10n.F(Copy.Removed, path));
        }
        run("update-desktop-database", [Path.GetDirectoryName(OpenWith(home))!]);
        if (RemoveShortcut(run)) log.Add(L10n.T(Copy.ShortcutRemoved));
        return log;
    }

    /// Removes the custom keybinding earlier versions added, keeping everyone else's; true when there was one. A list
    /// it could not read, or GNOME settings that aren't there, are left exactly as they are.
    static bool RemoveShortcut(Func<string, IReadOnlyList<string>, string?> run)
    {
        if (run("gsettings", ["list-keys", Schema]) is not { } keys || !keys.Split('\n').Contains("custom-keybindings")) return false;
        var current = run("gsettings", ["get", Schema, "custom-keybindings"])?.Trim();
        if (current is null || !Regex.IsMatch(current, @"^(@as \[\]|\[\s*('(?:[^'\\]|\\.)*'\s*(,\s*'(?:[^'\\]|\\.)*'\s*)*)?\])$")) return false;
        var list = Regex.Matches(current, @"'((?:[^'\\]|\\.)*)'").Select(m => m.Groups[1].Value).ToList();
        if (!list.Remove(KeybindingPath)) return false;
        run("gsettings", ["set", Schema, "custom-keybindings", Variant(list)]);
        run("gsettings", ["reset-recursively", Schema + ".custom-keybinding:" + KeybindingPath]);
        return true;
    }

    static string Variant(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
    static string Variant(IReadOnlyList<string> values) => values.Count == 0 ? "@as []" : "[" + string.Join(", ", values.Select(Variant)) + "]";

    /// Runs a program with an argument list; its stdout, or null when it is missing or fails.
    public static string? Run(string program, IReadOnlyList<string> args)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(program, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            if (process is null) return null;
            var errors = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            errors.Wait();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch (Win32Exception) { return null; }
    }

    /// `exe --background`, fully detached, with stdio on /dev/null, so neither the terminal nor the AppImage runtime
    /// that ran the installer waits for it to close them. With `setsid` (util-linux) it gets its own session.
    /// Without it: SIGHUP ignored (nohup) and, under bash, its own process group (job control; dash has none without
    /// a terminal). It still shares the installer's session then, so other signals to that session can reach it,
    /// which setsid would have prevented. The shell gets the program as $0 and setsid as $1, never inside the
    /// script text. True when it started.
    public static bool StartBackground(string exe, string setsid = "/usr/bin/setsid")
    {
        const string script = """
            if [ -x "$1" ]; then exec "$1" -f "$0" --background </dev/null >/dev/null 2>&1; fi
            set -m
            if command -v nohup >/dev/null 2>&1; then nohup "$0" --background </dev/null >/dev/null 2>&1 &
            else "$0" --background </dev/null >/dev/null 2>&1 &
            fi
            """;
        try
        {
            var shell = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
            using var sh = Process.Start(new ProcessStartInfo(shell, ["-c", script, exe, setsid]) { UseShellExecute = false });
            if (sh is null) return false;
            sh.WaitForExit();
            return sh.ExitCode == 0;
        }
        catch (Win32Exception) { return false; }
    }

    /// The real thing for this user: install (and start the background instance) or uninstall (and stop it).
    public static IReadOnlyList<string> Apply(bool install)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var exe = ExecutablePath;
        var log = install ? Install(home, exe, Run).ToList() : Uninstall(home, Run).ToList();
        if (install && !Instance.IsRunning(Instance.SocketPath) && StartBackground(exe)) log.Add(L10n.T(Copy.Started));
        else if (!install && Instance.Send(Instance.SocketPath, new("quit"))) log.Add(L10n.T(Copy.Stopped));
        return log;
    }
}
