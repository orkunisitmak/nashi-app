using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nashi.Core;

namespace Nashi.Linux.Editors.Media;

/// The video and audio tools that play (macOS ToolEditor's mediaPreview with its controls): Trim, Split, Change Speed,
/// Snapshots, Mute, Normalize, Channels, Bleep and Audio to Video. The preview plays the first file, video on the
/// canvas, with the timeline and transport under it; Space plays, ←/→ step a second (Shift: a frame), I and O set
/// the range, M adds a split or a frame, Delete removes the chosen point. Speed, channels, bleeps and loudness play
/// processed. Save runs MediaEngine with the same options on every file.
public sealed class MediaEditor : EditorPanel
{
    private static class Copy
    {
        public const string Fine = "Fine-tune it. Save a fresh copy.";
        public const string Straight = "Straight to the good stuff.";
        public const string Trimmed = "Save trimmed copy";
        public const string SpeedCopy = "Save speed copy";
        public const string AudioCopy = "Save audio copy";
        public const string Bleeped = "Save bleeped copy";
        public const string Visualize = "Create visualization";
        public const string Frames = "Save frames";
        public const string Clips = "Export clips";
        public const string Muted = "Save muted copy";
        public const string Normalized = "Save normalized copy";
        public const string Play = "Play";
        public const string Pause = "Pause";
        public const string PlayRange = "Play Selected Range";
        public const string Silent = "Sound preview is off: PulseAudio isn’t available. The picture still plays.";
        public const string Keys = "Space plays · ←/→ 1 s · Shift+←/→ one frame · I/O set the range";
        public const string Start = "Start";
        public const string End = "End";
        public const string Seconds = "Seconds";
        public const string Selected = "Selected duration: {0}";
        public const string TrimSilence = "Remove silence from beginning and end";
        public const string SplitCount = "{0} split points";
        public const string AddSplit = "Add Split";
        public const string EqualClips = "Equal clips";
        public const string Set = "Set";
        public const string ClipsHint = "Click a clip to leave it out of the result.";
        public const string Clip = "Clip {0}";
        public const string JoinClips = "Join retained clips into one video";
        public const string Speed = "Playback speed";
        public const string Pitch = "Audio pitch is preserved.";
        public const string FramesSelected = "{0} selected";
        public const string AddFrame = "Add Frame";
        public const string Every = "Every N seconds";
        public const string Evenly = "Evenly spaced";
        public const string Interval = "Interval";
        public const string Count = "Count";
        public const string MuteHint = "The copy keeps the picture and drops every audio track.";
        public const string Target = "Target loudness";
        public const string Range = "Loudness range";
        public const string Peak = "True peak limit";
        public const string Output = "Output";
        public const string Mono = "Mono";
        public const string Stereo = "Stereo";
        public const string Swap = "Swap left and right";
        public const string Left = "Left volume";
        public const string Right = "Right volume";
        public const string Depth = "Sample depth";
        public const string MatchSource = "Match source";
        public const string Style = "Style";
        public const string Bleep = "Bleep";
        public const string Mute = "Mute";
        public const string DragRange = "Drag across the waveform to choose a range.";
        public const string AddRedaction = "Add Redaction";
        public const string Waveform = "Waveform";
        public const string Spectrum = "Spectrum";
        public const string Bars = "Bars";
        public const string Still = "Still image";
        public const string ChooseStill = "Choose Still Image…";
        public const string Size = "Video size";
        public const string Landscape = "Landscape";
        public const string Portrait = "Portrait";
        public const string Square = "Square";
        public const string Color = "Color";
        public const string Remove = "Remove";
        public const string Range1 = "Choose a valid time range.";
        public const string NoSplit = "Add a split point or choose equal clips.";
        public const string Redacted = "Save redacted copy";
        public const string Solid = "Solid";
        public const string Blur = "Blur";
        public const string Pixelate = "Pixelate";
        public const string DragArea = "Drag on the preview to add an area. Each area is saved into the new copy.";
        public const string AreaTimes = "Each area covers the range on the timeline when it is drawn; choose an area to change its range.";
        public const string OneArea = "1 area selected";
        public const string Areas = "{0} areas selected";
        public const string UndoArea = "Undo Area";
        public const string DeleteArea = "Delete Area";
    }

    static readonly string[] Colors = ["#E4BD59", "#FFFFFF", "#FF6B79", "#4B79A1", "#9B8CFF"];

    private readonly MediaTools tools;
    private readonly Timeline timeline = new();
    private readonly Button play = new() { Classes = { "secondary", "compact" } };
    private readonly TextBlock clock = new() { FontSize = 11, FontFamily = new FontFamily("monospace"), VerticalAlignment = VerticalAlignment.Center, Classes = { "secondary" } };
    private readonly TextBlock silent = new() { IsVisible = false };
    private readonly Control stage;
    private readonly CancellationTokenSource lifetime = new();
    private MediaInfo? info;
    private MediaPlayer? player;
    private WriteableBitmap? visual;
    private bool rendering, renderAgain;
    private Action? refresh;

    // The options (MediaOptions, as macOS keeps them).
    private double start, end, speed = 1, loudness = -16, loudnessRange = 11, peak = -1.5, left = 1, right = 1, interval = 5;
    private bool trimSilence, joinClips, swap;
    private int channels = 2, equalCount = 3, evenCount = 6, width = 1920, height = 1080;
    private string depth = "matchSource", bleepMode = "bleep", visualization = "waveform", color = "E4BD59";
    private string? image;
    private readonly List<double> points = [];
    private readonly HashSet<int> discarded = [];
    private readonly List<MediaRange> bleeps = [];

    public MediaEditor(string tool, IReadOnlyList<string> inputs, MediaTools tools) : base(tool, inputs)
    {
        this.tools = tools;
        var transport = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 10 };
        play.Click += (_, _) => Toggle();
        transport.Children.Add(play);
        Grid.SetColumn(clock, 1);
        transport.Children.Add(clock);
        if (tool is "trimVideo" or "trimAudio")
        {
            var range = Ui.Button(Copy.PlayRange, PlayRange);
            range.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(range, 2);
            transport.Children.Add(range);
        }
        var box = new StackPanel { Spacing = 10 };
        box.Children.Add(timeline);
        box.Children.Add(transport);
        box.Children.Add(Ui.Warning(silent));
        silent.Text = L10n.T(Copy.Silent);
        ((Control)box.Children[^1]).Bind(Visual.IsVisibleProperty, silent.GetObservable(Visual.IsVisibleProperty));
        stage = box;
        timeline.ShowsRange = tool is "trimVideo" or "trimAudio" or "redactAudio" or "redactVideo";
        timeline.Seeked += t => player?.Seek(t);
        timeline.RangeChanged += () => { (start, end) = (timeline.Start, timeline.End); Retime(); Change(); refresh?.Invoke(); };
        timeline.PointChosen += _ => refresh?.Invoke();
        timeline.PointMoved += MovePoint;
        ShowTransport();
    }

    public override string Hint => Tool is "trimVideo" or "trimAudio" ? Copy.Straight : Copy.Fine;
    public override string Primary => Tool switch
    {
        "trimVideo" or "trimAudio" => Copy.Trimmed,
        "changeVideoSpeed" => Copy.SpeedCopy,
        "audioChannels" => Copy.AudioCopy,
        "redactAudio" => Copy.Bleeped,
        "audioToVideo" => Copy.Visualize,
        "videoSnapshots" => Copy.Frames,
        "splitVideo" => Copy.Clips,
        "muteVideo" => Copy.Muted,
        "redactVideo" => Copy.Redacted,
        _ => Copy.Normalized,
    };
    /// Redact Video draws its areas on a frame of the video (the window's canvas, in video pixels).
    public override bool ImageSource => Tool == "redactVideo";
    public override async Task<string> StillAsync(CancellationToken token) => still = await StillFrameAsync(tools, Inputs[0], token);
    private string? still;
    public override bool Interactive => Tool == "redactVideo";
    public override bool ComparesResult => false;
    public override bool Canvas => IsVideoTool || Tool == "audioToVideo";
    bool IsVideoTool => Tool is "trimVideo" or "splitVideo" or "changeVideoSpeed" or "videoSnapshots" or "muteVideo" or "redactVideo";
    public override Control? Stage => stage;
    public override Bitmap? Frame => Tool == "audioToVideo" ? visual : player?.Frame;
    public override string? Dimensions => info is null ? null : (info.Video is { Width: { } w, Height: { } h } ? $"{w} × {h} · " : "") + Timeline.Format(info.Duration);
    public override bool CanSave => info is not null && Tool switch
    {
        "videoSnapshots" => points.Count > 0,
        "splitVideo" => points.Count > 0 && Clips().Count - discarded.Count >= 1,
        "redactAudio" => bleeps.Count > 0,
        "redactVideo" => areas.Count > 0 && drawing is null,
        _ => true,
    };

    /// The player (the checks drive it).
    public MediaPlayer? Player => player;
    internal Timeline Timeline => timeline;
    public IReadOnlyList<double> Points => points;
    public IReadOnlyList<MediaRange> Bleeps => bleeps;

    public override async Task LoadAsync(CancellationToken token)
    {
        info = await tools.Engine.InspectAsync(Inputs[0], token);
        end = info.Duration;
        timeline.Duration = info.Duration;
        timeline.SetRange(0, info.Duration);
        var (w, h) = PreviewSize(info.Video?.Width ?? 0, info.Video?.Height ?? 0, 640);
        player = new MediaPlayer(tools, Inputs[0], info, info.Video is null ? 0 : w, h);
        player.Changed += OnPlayer;
        ShowTransport();
        if (Tool == "splitVideo") SetEqual(2);
        if (Tool == "audioToVideo") (width, height) = (1920, 1080);
        Live();
        player.Still(0);
        _ = StripAsync(lifetime.Token);
        if (Tool == "audioToVideo") RenderVisual();
    }

    static (int W, int H) PreviewSize(int w, int h, int edge)
    {
        if (w <= 0 || h <= 0) return (0, 0);
        var scale = Math.Min(1, (double)edge / Math.Max(w, h));
        return ((int)Math.Round(w * scale / 2) * 2, (int)Math.Round(h * scale / 2) * 2);
    }

    // The filmstrip (video) or waveform (audio), off the UI thread; the editor works without it meanwhile.
    private async Task StripAsync(CancellationToken token)
    {
        try
        {
            if (info!.Video is { Width: > 0, Height: > 0 } v)
            {
                var (w, h) = PreviewSize(v.Width!.Value, v.Height!.Value, 96);
                var frames = await Task.Run(() => tools.FilmstripAsync(Inputs[0], info.Duration, 10, w, h, token), token);
                if (token.IsCancellationRequested) return;
                timeline.Thumbnails = [.. frames.Where(f => f.Length == w * h * 4).Select(f => (Bitmap)ImageCanvas.Bitmap((f, w, h)))];
            }
            else if (info.HasAudio)
            {
                timeline.Peaks = await Task.Run(() => tools.PeaksAsync(Inputs[0], info.Duration, 1200, token, Tool == "audioChannels" ? 0 : null), token);
                if (Tool == "audioChannels") timeline.SecondPeaks = await Task.Run(() => tools.PeaksAsync(Inputs[0], info.Duration, 1200, token, 1), token);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or InvalidOperationException) { }
    }

    private void OnPlayer()
    {
        if (player is null) return;
        timeline.Playhead = player.Position;
        silent.IsVisible = player.Silent;
        ShowTransport();
        Repainted();
    }

    private void ShowTransport()
    {
        play.Content = L10n.T(player?.Playing == true ? Copy.Pause : Copy.Play);
        clock.Text = Timeline.Format(player?.Position ?? 0) + " / " + Timeline.Format(info?.Duration ?? 0);
    }

    public void Toggle() => player?.Toggle();

    public void PlayRange() => player?.Play(start, end);

    // The live processed preview: what the player applies while it plays.
    private void Live()
    {
        if (player is null) return;
        player.Speed = Tool == "changeVideoSpeed" ? speed : 1;
        player.AudioFilter = Tool switch
        {
            "audioChannels" => ChannelFilter(),
            "redactAudio" when bleeps.Count > 0 => $"aeval=exprs='if({string.Join('+', bleeps.Select(r => $"between(t,{MediaTools.N(r.Start)},{MediaTools.N(r.End)})"))},{(bleepMode == "mute" ? "0" : "0.18*sin(2*PI*1000*t)")},val(ch))':c=same",
            "normalizeAudio" => $"loudnorm=I={MediaTools.N(loudness)}:LRA={MediaTools.N(loudnessRange)}:TP={MediaTools.N(peak)}",
            _ => null,
        };
        if (player.Playing) player.Seek(player.Position);
    }

    // The same pan as MediaEngine's audioChannels; Mono previews in both ears.
    private string ChannelFilter() => channels == 1
        ? $"aformat=channel_layouts=stereo,pan=stereo|c0={MediaTools.N(left / 2)}*c0+{MediaTools.N(right / 2)}*c1|c1={MediaTools.N(left / 2)}*c0+{MediaTools.N(right / 2)}*c1"
        : $"aformat=channel_layouts=stereo,pan=stereo|c0={MediaTools.N(left)}*c{(swap ? 1 : 0)}|c1={MediaTools.N(right)}*c{(swap ? 0 : 1)}";

    public override bool Key(Key key, KeyModifiers modifiers)
    {
        if (player is null) return false;
        var shift = (modifiers & KeyModifiers.Shift) != 0;
        switch (key)
        {
            case Avalonia.Input.Key.Space when shift && Tool is "trimVideo" or "trimAudio": PlayRange(); return true;
            case Avalonia.Input.Key.Space: Toggle(); return true;
            case Avalonia.Input.Key.Left or Avalonia.Input.Key.Right:
                var step = (shift ? 1 / 30.0 : 1) * (key == Avalonia.Input.Key.Left ? -1 : 1);
                timeline.Playhead = player.Position + step;
                player.Seek(timeline.Playhead);
                return true;
            case Avalonia.Input.Key.I when timeline.ShowsRange: SetRange(player.Position, Math.Max(end, player.Position + Timeline.MinRange)); return true;
            case Avalonia.Input.Key.O when timeline.ShowsRange: SetRange(Math.Min(start, player.Position - Timeline.MinRange), player.Position); return true;
            case Avalonia.Input.Key.M when Tool is "splitVideo" or "videoSnapshots": AddPoint(player.Position); return true;
            default: return false;
        }
    }

    /// Sets the In/Out range as the handles do (clamped).
    public void SetRange(double from, double to)
    {
        timeline.SetRange(from, to);
        (start, end) = (timeline.Start, timeline.End);
        Change();
        refresh?.Invoke();
    }

    /// A split point or a snapshot at `time` (split points keep 0.05 s from each other and the ends).
    public void AddPoint(double time)
    {
        if (info is null) return;
        if (Tool == "splitVideo")
        {
            if (time < Timeline.MinRange || time > info.Duration - Timeline.MinRange || points.Any(p => Math.Abs(p - time) < Timeline.MinRange)) return;
            discarded.Clear();
        }
        points.Add(Math.Clamp(time, 0, Math.Max(0, info.Duration - .05)));
        points.Sort();
        timeline.SelectedPoint = points.IndexOf(time);
        Points2();
    }

    private void Points2()
    {
        timeline.Points = [.. points];
        Change();
        refresh?.Invoke();
    }

    private void MovePoint(int index, double time)
    {
        if (index < 0 || index >= points.Count || info is null) return;
        var lower = index > 0 ? points[index - 1] + Timeline.MinRange : (Tool == "splitVideo" ? Timeline.MinRange : 0);
        var upper = index + 1 < points.Count ? points[index + 1] - Timeline.MinRange : info.Duration - Timeline.MinRange;
        points[index] = Math.Clamp(time, lower, Math.Max(lower, upper));
        Points2();
    }

    public override bool Delete()
    {
        if (Tool == "redactVideo")
        {
            if (selected < 0 || selected >= areas.Count) return false;
            areas.RemoveAt(selected);
            selected = -1;
            Change();
            refresh?.Invoke();
            return true;
        }
        if (Tool is not ("splitVideo" or "videoSnapshots") || timeline.SelectedPoint is var i && (i < 0 || i >= points.Count)) return false;
        points.RemoveAt(timeline.SelectedPoint);
        timeline.SelectedPoint = -1;
        discarded.Clear();
        Points2();
        return true;
    }

    /// `count` equal clips (split) .
    public void SetEqual(int count)
    {
        if (info is null || count < 2) return;
        points.Clear();
        discarded.Clear();
        for (var n = 1; n < count; n++) points.Add(info.Duration * n / count);
        Points2();
    }

    /// Snapshots every `seconds` (from 0) or `count` evenly spaced.
    public void Every(double seconds)
    {
        if (info is null || !(seconds > 0)) return;
        points.Clear();
        for (var t = 0.0; t < info.Duration - .05 && points.Count < 500; t += seconds) points.Add(t);
        Points2();
    }

    public void Evenly(int count)
    {
        if (info is null || count < 1) return;
        points.Clear();
        for (var n = 0; n < count; n++) points.Add(info.Duration * (n + .5) / count);
        Points2();
    }

    public void Discard(int clip)
    {
        if (!discarded.Remove(clip) && Clips().Count - discarded.Count > 1) discarded.Add(clip);
        Change();
        refresh?.Invoke();
    }

    private List<MediaRange> Clips()
    {
        var bounds = new[] { 0d }.Concat(points).Append(info?.Duration ?? 0).ToArray();
        return [.. bounds.Zip(bounds.Skip(1), (a, b) => new MediaRange(a, b))];
    }

    /// Adds the selected range as a bleep (or silence).
    public void AddBleep()
    {
        if (end - start < Timeline.MinRange) return;
        bleeps.Add(new(start, end));
        timeline.Ranges = [.. bleeps];
        Live();
        Change();
        refresh?.Invoke();
    }

    public void Set(string option, object value)
    {
        switch (option)
        {
            case "speed": speed = (double)value; break;
            case "channels": channels = (int)value; break;
            case "swap": swap = (bool)value; break;
            case "join": joinClips = (bool)value; break;
            case "visualization": visualization = (string)value; break;
            case "size": (width, height) = ((int, int))value; break;
            case "loudness": loudness = (double)value; break;
            case "bleepMode": bleepMode = (string)value; break;
        }
        Live();
        Change();
        if (Tool == "audioToVideo") RenderVisual();
        refresh?.Invoke();
    }

    public override Control Options()
    {
        var stack = new StackPanel { Spacing = 14 };
        refresh = null;
        TextBlock Line(Func<string> text)
        {
            var line = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
            line.Text = text();
            refresh += () => line.Text = text();
            return line;
        }
        Control Slider(string label, double min, double max, double value, double neutral, Action<double> set, string unit, string format = "0.0") =>
            Ui.Slider(label, min, max, value, neutral, v => { set(v); Live(); Change(); }, unit, v => v.ToString(format, CultureInfo.CurrentCulture));
        switch (Tool)
        {
            case "trimVideo" or "trimAudio":
                stack.Children.Add(TimeFields());
                if (Tool == "trimAudio")
                {
                    var silence = new CheckBox { Content = L10n.T(Copy.TrimSilence), IsChecked = trimSilence, FontSize = 12 };
                    silence.IsCheckedChanged += (_, _) => { trimSilence = silence.IsChecked == true; Change(); };
                    stack.Children.Add(silence);
                }
                stack.Children.Add(Line(() => L10n.F(Copy.Selected, Timeline.Format(Math.Max(0, end - start)))));
                break;
            case "splitVideo":
                stack.Children.Add(Ui.Row(Line(() => L10n.F(Copy.SplitCount, points.Count)), Ui.Button(Copy.AddSplit, () => AddPoint(player?.Position ?? 0))));
                stack.Children.Add(PointList());
                var equal = Ui.Number(equalCount, v => equalCount = v);
                stack.Children.Add(Ui.Row(Ui.Labeled(Copy.EqualClips, equal), Ui.Button(Copy.Set, () => SetEqual(Math.Clamp(equalCount, 2, 100)))));
                stack.Children.Add(Ui.Caption(Copy.ClipsHint));
                var clips = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
                void Fill()
                {
                    clips.Children.Clear();
                    var all = Clips();
                    for (var n = 0; n < all.Count; n++)
                    {
                        var at = n;
                        var chip = new Button { Classes = { "segment" }, Content = L10n.F(Copy.Clip, n + 1) + " · " + Timeline.Format(all[n].End - all[n].Start), Opacity = discarded.Contains(n) ? .4 : 1, FontSize = 11 };
                        chip.Classes.Set("chosen", !discarded.Contains(n));
                        chip.Click += (_, _) => Discard(at);
                        clips.Children.Add(chip);
                    }
                }
                Fill();
                refresh += Fill;
                stack.Children.Add(clips);
                var join = new CheckBox { Content = L10n.T(Copy.JoinClips), IsChecked = joinClips, FontSize = 12 };
                join.IsCheckedChanged += (_, _) => Set("join", join.IsChecked == true);
                stack.Children.Add(join);
                break;
            case "changeVideoSpeed":
                var row = Slider(Copy.Speed, .25, 4, speed, 1, v => speed = v, "×", "0.00");
                stack.Children.Add(row);
                stack.Children.Add(Ui.Wrap([.. new[] { .25, .5, 1, 1.5, 2, 4 }.Select(s => Ui.Button(s.ToString("0.##", CultureInfo.CurrentCulture) + "×", () => Ui.SliderOf(row).Value = s))]));
                stack.Children.Add(Ui.Caption(Copy.Pitch));
                break;
            case "videoSnapshots":
                stack.Children.Add(Ui.Row(Line(() => L10n.F(Copy.FramesSelected, points.Count)), Ui.Button(Copy.AddFrame, () => AddPoint(player?.Position ?? 0))));
                var seconds = Ui.Number((int)interval, v => interval = v, 60);
                var count = Ui.Number(evenCount, v => evenCount = v, 60);
                stack.Children.Add(Ui.Row(Ui.Labeled(Copy.Interval, seconds), Ui.Button(Copy.Every, () => Every(Math.Max(.1, interval)))));
                stack.Children.Add(Ui.Row(Ui.Labeled(Copy.Count, count), Ui.Button(Copy.Evenly, () => Evenly(Math.Clamp(evenCount, 1, 500)))));
                stack.Children.Add(PointList());
                break;
            case "redactVideo":
                var looks = new Segments([("solid", Copy.Solid), ("blur", Copy.Blur), ("pixelate", Copy.Pixelate)], redactStyle);
                looks.Chosen += v => { redactStyle = v; Change(); };
                stack.Children.Add(Ui.Labeled(Copy.Style, looks));
                stack.Children.Add(Ui.Caption(Copy.DragArea));
                stack.Children.Add(Ui.Caption(Copy.AreaTimes));
                stack.Children.Add(TimeFields());
                stack.Children.Add(Line(() => areas.Count == 1 ? L10n.T(Copy.OneArea) : L10n.F(Copy.Areas, areas.Count)));
                stack.Children.Add(Ui.Row(Ui.Button(Copy.UndoArea, () => { if (areas.Count > 0) { areas.RemoveAt(areas.Count - 1); selected = -1; Change(); refresh?.Invoke(); } }), Ui.Button(Copy.DeleteArea, () => Delete())));
                break;
            case "muteVideo":
                stack.Children.Add(Ui.Caption(Copy.MuteHint));
                break;
            case "normalizeAudio":
                stack.Children.Add(Slider(Copy.Target, -40, -5, loudness, -16, v => loudness = v, "LUFS"));
                stack.Children.Add(Slider(Copy.Range, 1, 20, loudnessRange, 11, v => loudnessRange = v, "LU"));
                stack.Children.Add(Slider(Copy.Peak, -9, -.1, peak, -1.5, v => peak = v, "dBTP"));
                break;
            case "audioChannels":
                var outputs = new Segments([("1", Copy.Mono), ("2", Copy.Stereo)], channels.ToString(CultureInfo.InvariantCulture));
                var swapBox = new CheckBox { Content = L10n.T(Copy.Swap), IsChecked = swap, FontSize = 12, IsVisible = channels == 2 };
                outputs.Chosen += v => { Set("channels", int.Parse(v, CultureInfo.InvariantCulture)); swapBox.IsVisible = channels == 2; };
                swapBox.IsCheckedChanged += (_, _) => Set("swap", swapBox.IsChecked == true);
                stack.Children.Add(Ui.Labeled(Copy.Output, outputs));
                stack.Children.Add(swapBox);
                stack.Children.Add(Slider(Copy.Left, 0, 2, left, 1, v => left = v, "×", "0.00"));
                stack.Children.Add(Slider(Copy.Right, 0, 2, right, 1, v => right = v, "×", "0.00"));
                if (FeatureCatalog.Extension(Inputs[0]) is "wav" or "aiff")
                {
                    var depths = new Segments([("matchSource", Copy.MatchSource), ("pcm16", "16-bit"), ("pcm24", "24-bit"), ("pcm32", "32-bit"), ("float32", "32-bit float")], depth);
                    depths.Chosen += v => { depth = v; Change(); };
                    stack.Children.Add(Ui.Heading(Copy.Depth));
                    stack.Children.Add(depths);
                }
                break;
            case "redactAudio":
                var modes = new Segments([("bleep", Copy.Bleep), ("mute", Copy.Mute)], bleepMode);
                modes.Chosen += v => Set("bleepMode", v);
                stack.Children.Add(Ui.Labeled(Copy.Style, modes));
                stack.Children.Add(Ui.Caption(Copy.DragRange));
                stack.Children.Add(TimeFields());
                stack.Children.Add(Ui.Row(Ui.Button(Copy.AddRedaction, AddBleep)));
                var list = new StackPanel { Spacing = 4 };
                void Ranges()
                {
                    list.Children.Clear();
                    for (var n = 0; n < bleeps.Count; n++)
                    {
                        var at = n;
                        list.Children.Add(Ui.Row(new TextBlock { Text = Timeline.Format(bleeps[n].Start) + " → " + Timeline.Format(bleeps[n].End), FontSize = 12, FontFamily = new FontFamily("monospace"), VerticalAlignment = VerticalAlignment.Center }, Ui.Button(Copy.Remove, () => { bleeps.RemoveAt(at); timeline.Ranges = [.. bleeps]; Live(); Change(); refresh?.Invoke(); }, "quiet")));
                    }
                }
                Ranges();
                refresh += Ranges;
                stack.Children.Add(list);
                break;
            case "audioToVideo":
                var styles = new Segments([("waveform", Copy.Waveform), ("spectrum", Copy.Spectrum), ("bars", Copy.Bars), ("image", Copy.Still)], visualization);
                styles.Chosen += v => Set("visualization", v);
                stack.Children.Add(Ui.Labeled(Copy.Style, styles));
                stack.Children.Add(Ui.Button(Copy.ChooseStill, async () =>
                {
                    if (PickImage is null || await PickImage() is not { } path) return;
                    image = path;
                    styles.Select("image");
                    Set("visualization", "image");
                }));
                stack.Children.Add(Ui.Labeled(Copy.Color, Ui.Swatches(Colors, "#" + color, hex => { color = hex.TrimStart('#'); Change(); RenderVisual(); })));
                stack.Children.Add(Ui.Heading(Copy.Size));
                var w = Ui.Number(width, v => { if (v is >= 16 and <= 8192) { width = v; Change(); RenderVisual(); } });
                var h = Ui.Number(height, v => { if (v is >= 16 and <= 8192) { height = v; Change(); RenderVisual(); } });
                void SizeTo(int a, int b) { w.Show(a); h.Show(b); Set("size", (a, b)); }
                stack.Children.Add(Ui.Wrap(Ui.Button(Copy.Landscape, () => SizeTo(1920, 1080)), Ui.Button(Copy.Portrait, () => SizeTo(1080, 1920)), Ui.Button(Copy.Square, () => SizeTo(1080, 1080))));
                stack.Children.Add(Ui.Row(Ui.Labeled("W", w), Ui.Labeled("H", h)));
                break;
        }
        stack.Children.Add(Ui.Caption(Copy.Keys));
        return stack;
    }

    // Start and End in seconds, in step with the timeline.
    private Control TimeFields()
    {
        TextBox Field(Func<double> get, Action<double> set)
        {
            var box = new TextBox { Width = 92, FontSize = 12, Watermark = L10n.T(Copy.Seconds) };
            string shown = "";
            void Show() { if (!box.IsFocused) box.Text = shown = get().ToString("0.000", CultureInfo.CurrentCulture); }
            Show();
            refresh += Show;
            box.TextChanged += (_, _) =>
            {
                if (box.Text == shown) return;
                if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) && double.IsFinite(v)) set(v);
            };
            box.LostFocus += (_, _) => Show();
            return box;
        }
        return Ui.Row(Ui.Labeled(Copy.Start, Field(() => start, v => SetRange(v, end))), Ui.Labeled(Copy.End, Field(() => end, v => SetRange(start, v))));
    }

    // Each split point or frame with its time and a remove button; frames show their picture.
    private Control PointList()
    {
        var list = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        void Fill()
        {
            list.Children.Clear();
            for (var n = 0; n < points.Count; n++)
            {
                var at = n;
                var remove = new Button { Content = "×", Classes = { "quiet", "compact" }, Padding = new Thickness(6, 1) };
                ToolTip.SetTip(remove, L10n.T(Copy.Remove));
                remove.Click += (_, _) => { timeline.SelectedPoint = at; Delete(); };
                var label = new TextBlock { Text = Timeline.Format(points[n]), FontSize = 11, FontFamily = new FontFamily("monospace"), VerticalAlignment = VerticalAlignment.Center };
                var chip = new Border { Classes = { "pill" }, Child = Ui.Row(label, remove), BorderBrush = n == timeline.SelectedPoint ? Ui.Gold : null, BorderThickness = new Thickness(n == timeline.SelectedPoint ? 1 : 0) };
                chip.PointerPressed += (_, _) => { timeline.SelectedPoint = at; player?.Seek(points[at]); refresh?.Invoke(); };
                list.Children.Add(chip);
            }
        }
        Fill();
        refresh += Fill;
        return list;
    }

    // One frame of the visualization at the playhead, at a preview size (macOS renders a short preview).
    private async void RenderVisual()
    {
        if (info is null || Tool != "audioToVideo") return;
        if (rendering) { renderAgain = true; return; }
        rendering = true;
        try
        {
            do
            {
                renderAgain = false;
                var (w, h) = PreviewSize(width, height, 480);
                var o = Settings;
                var at = Math.Min(player?.Position ?? 0, Math.Max(0, info.Duration - .5));
                var filter = o.Visualization switch
                {
                    "spectrum" => $"showspectrum=s={w}x{h}:slide=replace:color=intensity",
                    "bars" => $"showfreqs=s={w}x{h}:mode=bar:ascale=log:fscale=log:colors=0x{o.VisualizationColor}",
                    _ => $"showwaves=s={w}x{h}:mode=cline:colors=0x{o.VisualizationColor}",
                };
                string[] args = o.Visualization == "image" && image is not null
                    ? ["-i", image, "-frames:v", "1", "-vf", $"scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,setsar=1", "-f", "rawvideo", "-pix_fmt", "rgba", "-"]
                    : ["-ss", MediaTools.N(at), "-t", "1", "-i", Inputs[0], "-filter_complex", $"[0:a]{filter},format=rgba[v]", "-map", "[v]", "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgba", "-"];
                var token = lifetime.Token;
                var pixels = await Task.Run(async () =>
                {
                    using var pipe = tools.Start(args);
                    using var registration = token.Register(pipe.Dispose);
                    var frame = new byte[w * h * 4];
                    return await pipe.ReadAsync(frame) == frame.Length ? frame : null;
                }, token);
                if (pixels is null || token.IsCancellationRequested) continue;
                var old = visual;
                visual = ImageCanvas.Bitmap((pixels, w, h));
                Repainted();
                old?.Dispose();
            }
            while (renderAgain && !lifetime.IsCancellationRequested);
        }
        catch (Exception error) when (error is OperationCanceledException or IOException) { }
        finally { rendering = false; }
    }

    public MediaOptions Settings => new()
    {
        Start = start,
        End = end,
        TrimSilence = trimSilence,
        Speed = speed,
        SnapshotTimes = [.. points],
        SplitPoints = [.. points],
        DiscardedClips = [.. discarded.Order()],
        JoinSplitClips = joinClips,
        TargetLoudness = loudness,
        LoudnessRange = loudnessRange,
        TruePeak = peak,
        Channels = channels,
        LeftVolume = left,
        RightVolume = right,
        SwapChannels = swap && channels == 2,
        SampleDepth = depth,
        RedactionMode = Tool == "redactVideo" ? redactStyle : bleepMode,
        Regions = [.. areas.Select(a => new MediaRegion(Math.Floor(a.Area.X), Math.Floor(a.Area.Y), Math.Floor(a.Area.Width), Math.Floor(a.Area.Height), a.Start, a.End))],
        AudioRedactions = [.. bleeps],
        Visualization = visualization,
        VisualizationImage = image,
        VisualizationColor = color.ToLowerInvariant(),
        Width = Tool == "audioToVideo" ? width / 2 * 2 : null,
        Height = Tool == "audioToVideo" ? height / 2 * 2 : null,
    };

    public override string? Validate()
    {
        if (Tool is "trimVideo" or "trimAudio" && end - start < Timeline.MinRange) return L10n.T(Copy.Range1);
        if (Tool == "splitVideo" && points.Count == 0) return L10n.T(Copy.NoSplit);
        return null;
    }

    public override void Reset()
    {
        (start, end, speed, loudness, loudnessRange, peak, left, right) = (0, info?.Duration ?? 0, 1, -16, 11, -1.5, 1, 1);
        (trimSilence, joinClips, swap, channels, depth, bleepMode, visualization, color, image) = (false, false, false, 2, "matchSource", "bleep", "waveform", "E4BD59", null);
        (width, height) = (1920, 1080);
        points.Clear();
        discarded.Clear();
        bleeps.Clear();
        areas.Clear();
        selected = -1;
        redactStyle = "solid";
        timeline.Ranges = [];
        timeline.SelectedPoint = -1;
        timeline.SetRange(0, info?.Duration ?? 0);
        if (Tool == "splitVideo") SetEqual(2);
        timeline.Points = [.. points];
        Live();
        RenderVisual();
    }

    public override EditorJob Job()
    {
        var o = Settings;
        var tool = Tool;
        var engine = tools.Engine;
        return (files, destination, progress, token) => engine.ToolAsync(tool, files, destination, o, progress, token);
    }

    // ---- Redact Video: areas on the frame, each covering its own time range (the In/Out range when drawn).

    private readonly List<(Rect Area, double Start, double End)> areas = [];
    private int selected = -1;
    private Rect? drawing;
    private bool moving;
    private Point anchor;
    private Rect before;
    private string redactStyle = "solid";

    public int AreaCount => areas.Count;

    /// Adds an area (video pixels) over the current In/Out range.
    public void AddArea(Rect area)
    {
        var clipped = area.Intersect(new Rect(Source!.Size));
        if (clipped.Width < 2 || clipped.Height < 2) return;
        areas.Add((clipped, start, end));
        selected = areas.Count - 1;
        Change();
        refresh?.Invoke();
    }

    // Choosing an area shows its times on the timeline; moving the range retimes it.
    private void Retime()
    {
        if (Tool == "redactVideo" && selected >= 0 && selected < areas.Count) areas[selected] = (areas[selected].Area, start, end);
    }

    public override void Press(Point point, KeyModifiers modifiers, double tolerance)
    {
        anchor = point;
        selected = areas.FindLastIndex(a => a.Area.Contains(point));
        if (selected >= 0)
        {
            moving = true;
            before = areas[selected].Area;
            timeline.SetRange(areas[selected].Start, areas[selected].End);
            (start, end) = (timeline.Start, timeline.End);
        }
        else drawing = new Rect(point, point);
        Change();
        refresh?.Invoke();
    }

    public override void Drag(Point point, KeyModifiers modifiers)
    {
        if (!double.IsFinite(point.X)) return;
        if (moving && selected >= 0) areas[selected] = (CropGeometry.Move(before, anchor, point, Source!.Size), areas[selected].Start, areas[selected].End);
        else if (drawing is not null) drawing = CropGeometry.Selection(anchor, point, Source!.Size, null);
    }

    public override void Release(Point point, KeyModifiers modifiers)
    {
        Drag(point, modifiers);
        var drawn = drawing;
        drawing = null;
        moving = false;
        if (drawn is { Width: >= 2, Height: >= 2 } area) AddArea(area);
        else { Change(); refresh?.Invoke(); }
    }

    public override StandardCursorType Cursor(Point point, double tolerance) => areas.Any(a => a.Area.Contains(point)) ? StandardCursorType.SizeAll : StandardCursorType.Cross;

    public override void Draw(DrawingContext context, Matrix view, double zoom)
    {
        var now = player?.Position ?? 0;
        for (var n = 0; n < areas.Count; n++)
        {
            var (area, from, to) = areas[n];
            var active = now >= from && now <= to;
            var r = area.TransformToAABB(view);
            if (active) context.FillRectangle(new SolidColorBrush(redactStyle == "solid" ? Avalonia.Media.Color.FromArgb(0xE0, 0, 0, 0) : Avalonia.Media.Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), r);
            context.DrawRectangle(null, n == selected ? new Pen(Ui.Gold, 2) : new Pen(Brushes.White, 1, new DashStyle([4, 3], 0)), r);
        }
        if (drawing is { } d) context.DrawRectangle(new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x40, 0xE3, 0xB8, 0x59)), new Pen(Ui.Gold, 2), d.TransformToAABB(view));
    }

    /// A full-size frame a third of the way in, as a PNG in a fresh temporary folder (Crop and Redact Video draw on it).
    public static async Task<string> StillFrameAsync(MediaTools tools, string path, CancellationToken token)
    {
        var info = await tools.Engine.InspectAsync(path, token);
        if (info.Video is null) throw new ArgumentException("This file has no video stream.");
        var folder = Directory.CreateTempSubdirectory("nashi-frame-").FullName;
        var png = System.IO.Path.Combine(folder, "frame.png");
        try { await ProcessRunner.RunAsync(tools.Ffmpeg, ["-hide_banner", "-nostdin", "-v", "error", "-ss", MediaTools.N(info.Duration / 3), "-i", path, "-map", "0:v:0", "-frames:v", "1", "-update", "1", png], token); }
        catch { Directory.Delete(folder, true); throw; }
        return png;
    }

    /// Removes a StillFrameAsync frame and its folder.
    public static void DeleteStill(string? still)
    {
        if (still is null) return;
        try { Directory.Delete(System.IO.Path.GetDirectoryName(still)!, true); }
        catch (IOException) { }
    }

    public override void Dispose()
    {
        DeleteStill(still);
        lifetime.Cancel();
        player?.Dispose();
    }
}
