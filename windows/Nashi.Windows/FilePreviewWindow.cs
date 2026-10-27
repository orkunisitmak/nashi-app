using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using Nashi.Core;
namespace Nashi.Windows;

// One preview lifecycle for Library and Float. Each navigation cancels the previous load.
internal sealed class FilePreviewWindow:Window
{
    private readonly string[] paths;private int index;
    private readonly Toolchain tools;private readonly OfficeRuntime office;private readonly string temporary=Path.Combine(Path.GetTempPath(),"Nashi-quick-preview-"+Guid.NewGuid().ToString("N"));private CancellationTokenSource lifetime=new();
    private readonly ContentControl content=new();private readonly TextBlock caption=new(){TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
    private MediaElement? player;private WaveformView? wave;private bool playing;private readonly Slider scrub=new(){Minimum=0,Maximum=1,MinWidth=140};
    private PdfRasterDocument? pdfDocument;
    private readonly System.Windows.Threading.DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(150)};
    private readonly DockPanel transport=new();private bool updating;
    internal string PreviewStatus=>content.Content is TextBlock text?text.Text:content.Content?.GetType().Name??"Empty";
    /// Set by callers that mean "play this" (the play button on a desk card).
    internal bool AutoPlay{get;init;}
    internal string CurrentPath=>paths[index];internal bool Playing=>playing;
    internal FilePreviewWindow(Window owner,IEnumerable<string> paths,Toolchain? tools=null,OfficeRuntime? office=null,string? start=null)
    {
        this.tools=tools??new Toolchain(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi","Tools","v1"));
        this.office=office??new OfficeRuntime(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi","Tools"));
        this.paths=paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();if(this.paths.Length==0)throw new ArgumentException("Choose a file to preview.");if(start is not null)index=Math.Max(0,Array.FindIndex(this.paths,p=>string.Equals(p,start,StringComparison.OrdinalIgnoreCase)));
        Owner=owner;Title="Preview · Nashi";Width=Math.Min(1040,SystemParameters.WorkArea.Width*.8);Height=Math.Min(720,SystemParameters.WorkArea.Height*.85);MinWidth=560;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;Theme.Attach(this);
        var root=new DockPanel{Margin=new(20,16,20,16)};Content=root;var header=new DockPanel{Margin=new(0,0,0,12)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        // File arrows only exist when there is somewhere to go.
        var navigation=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(navigation,Dock.Right);header.Children.Add(navigation);
        if(this.paths.Length>1){navigation.Children.Add(Round("caret-left","Previous file",()=>Navigate(-1)));navigation.Children.Add(Round("caret-right","Next file",()=>Navigate(1)));}
        var open=MainWindow.Button("Open",()=>{if(File.Exists(CurrentPath)||Directory.Exists(CurrentPath))MainWindow.OpenUrl(CurrentPath);});open.Margin=new(8,0,0,0);navigation.Children.Add(open);
        caption.FontSize=15;caption.FontWeight=FontWeights.SemiBold;header.Children.Add(caption);
        // Transport: play, elapsed, a timeline that fills the width, length, mute.
        var bar=new Border{CornerRadius=new(12),BorderThickness=new(1),Padding=new(8,6,12,6),Margin=new(0,12,0,0),Child=transport};bar.SetResourceReference(Border.BackgroundProperty,"Surface");bar.SetResourceReference(Border.BorderBrushProperty,"Line");
        playButton=Round("play-fill","Play / pause",Toggle,40);DockPanel.SetDock(playButton,Dock.Left);transport.Children.Add(playButton);
        elapsed.Margin=new(10,0,12,0);DockPanel.SetDock(elapsed,Dock.Left);transport.Children.Add(elapsed);
        muteButton=Round("speaker-high","Mute",()=>{if(player is null)return;player.IsMuted=!player.IsMuted;muteButton!.Content=Icons.Create(player.IsMuted?"speaker-slash":"speaker-high",15);},34);DockPanel.SetDock(muteButton,Dock.Right);transport.Children.Add(muteButton);
        length.Margin=new(12,0,8,0);DockPanel.SetDock(length,Dock.Right);transport.Children.Add(length);
        scrub.VerticalAlignment=VerticalAlignment.Center;scrub.IsMoveToPointEnabled=true;AutomationProperties.SetName(scrub,"Timeline");transport.Children.Add(scrub);
        foreach(var label in new[]{elapsed,length}){label.FontSize=12;label.VerticalAlignment=VerticalAlignment.Center;label.SetResourceReference(TextBlock.ForegroundProperty,"Muted");}
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);footer.Children.Add(bar);
        hint.FontSize=11;hint.Margin=new(2,10,0,0);hint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");footer.Children.Add(hint);
        var stage=new Border{CornerRadius=new(12),ClipToBounds=true,Child=content};stage.SetResourceReference(Border.BackgroundProperty,"Inset");root.Children.Add(stage);
        content.MouseLeftButtonUp+=(_,e)=>{if(player is not null&&ReferenceEquals(e.OriginalSource,player)){Toggle();e.Handled=true;}};
        // Dragging the timeline pauses, seeks at most every 80 ms, and resumes on release; the clock
        // stops pushing the thumb meanwhile so the two never fight.
        scrub.AddHandler(System.Windows.Controls.Primitives.Thumb.DragStartedEvent,new System.Windows.Controls.Primitives.DragStartedEventHandler((_,_)=>{scrubbing=true;resume=playing;if(playing)player?.Pause();}));
        scrub.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,new System.Windows.Controls.Primitives.DragCompletedEventHandler((_,_)=>{scrubbing=false;Seek(scrub.Value,true);if(resume&&player is not null)player.Play();}));
        scrub.ValueChanged+=(_,_)=>{if(!updating&&player is not null)Seek(scrub.Value,!scrubbing);};
        seeker.Tick+=(_,_)=>{if(pendingSeek is {} target&&player is not null){pendingSeek=null;player.Position=TimeSpan.FromSeconds(target);}else seeker.Stop();};
        timer.Tick+=(_,_)=>{if(player is null)return;if(!scrubbing&&pendingSeek is null){updating=true;scrub.Value=player.Position.TotalSeconds;updating=false;}elapsed.Text=Clock(scrubbing?scrub.Value:player.Position.TotalSeconds);if(wave is not null)wave.Progress=scrub.Value/Math.Max(.001,scrub.Maximum);};
        PreviewKeyDown+=(_,e)=>{var key=e.Key==Key.System?e.SystemKey:e.Key;var modifiers=Keyboard.Modifiers;if(key==Key.Escape){Close();e.Handled=true;return;}if(ClipboardImport.IsEditing(Keyboard.FocusedElement as DependencyObject)&&Keyboard.FocusedElement is not TextBox{IsReadOnly:true})return;
            if(modifiers is not (ModifierKeys.None or ModifierKeys.Alt or ModifierKeys.Shift))return;
            if(key==Key.Space&&modifiers==ModifierKeys.None){if(player is null)Close();else Toggle();e.Handled=true;}
            else if(key is Key.Left or Key.Right){var direction=key==Key.Left?-1:1;if(player is not null&&modifiers!=ModifierKeys.Alt)Seek(Math.Clamp(player.Position.TotalSeconds+direction*(modifiers==ModifierKeys.Shift?.1:5),0,scrub.Maximum),true);else Navigate(direction);e.Handled=true;}};
        Loaded+=async(_,_)=>await Load();Closed+=(_,_)=>{lifetime.Cancel();lifetime.Dispose();timer.Stop();seeker.Stop();CloseMedia();pdfDocument?.Dispose();pdfDocument=null;_ = Cleanup();};
    }
    private Button? playButton,muteButton;private readonly TextBlock elapsed=new(){Text="0:00"},length=new(){Text="0:00"},hint=new(){TextWrapping=TextWrapping.Wrap};
    private bool scrubbing,resume;private double? pendingSeek;private readonly System.Windows.Threading.DispatcherTimer seeker=new(){Interval=TimeSpan.FromMilliseconds(80)};
    private static Button Round(string icon,string label,Action action,double size=34)
    {
        var button=new Button{Style=(Style)Application.Current.FindResource("IconButton"),Width=size,Height=size,Content=Icons.Create(icon,size>=40?16:15),Margin=new(4,0,0,0)};L10n.Bind(button,ToolTipProperty,label);AutomationProperties.SetName(button,label);button.Click+=(_,_)=>action();return button;
    }
    /// The first seek of a burst lands at once; the rest are coalesced so decoding keeps up.
    private void Seek(double seconds,bool now)
    {
        if(player is null)return;if(now){seeker.Stop();pendingSeek=null;player.Position=TimeSpan.FromSeconds(seconds);elapsed.Text=Clock(seconds);return;}
        if(seeker.IsEnabled)pendingSeek=seconds;else{player.Position=TimeSpan.FromSeconds(seconds);seeker.Start();}elapsed.Text=Clock(seconds);
    }
    private static string Clock(double seconds){var time=TimeSpan.FromSeconds(Math.Max(0,seconds));return time.TotalHours>=1?time.ToString(@"h\:mm\:ss"):time.ToString(@"m\:ss");}
    private void Hint(bool media)=>L10n.SetText(hint,media?(paths.Length>1?"Space play/pause · ←/→ 5 s · Shift+←/→ fine step · Alt+←/→ next file · Esc closes":"Space play/pause · ←/→ 5 s · Shift+←/→ fine step · Esc closes"):(paths.Length>1?"←/→ next file · Esc closes":"Esc closes"));
    internal void Navigate(int delta){if(paths.Length<2)return;index=(index+delta+paths.Length)%paths.Length;_ = Load();}
    private void CloseMedia(){player?.Stop();player?.Close();player=null;wave=null;playing=false;((FrameworkElement)transport.Parent).Visibility=Visibility.Collapsed;timer.Stop();seeker.Stop();pendingSeek=null;}
    private void Toggle(){if(player is null)return;if(playing)player.Pause();else player.Play();playing=!playing;Paint();}
    private void Paint(){if(playButton is not null)playButton.Content=Icons.Create(playing?"pause-fill":"play-fill",16);}
    /// Audio has nothing to look at, so the stage shows the sound's waveform; the player stays
    /// invisible underneath and the waveform doubles as a large timeline.
    private Grid Sound(string path,MediaElement current,CancellationToken token)
    {
        current.Opacity=0;current.Width=1;current.Height=1;current.IsHitTestVisible=false;
        var view=new WaveformView{Margin=new(28,36,28,36),Visibility=Visibility.Collapsed};AutomationProperties.SetName(view,"Waveform");wave=view;
        var note=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};var icon=Icons.Create("music-note",34);icon.Opacity=.5;icon.HorizontalAlignment=HorizontalAlignment.Center;note.Children.Add(icon);
        var status=L10n.Label(new TextBlock{Text=tools.Ready?"Drawing the waveform…":"Set up media tools to see the waveform.",FontSize=12,Margin=new(0,10,0,0),TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap});status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");note.Children.Add(status);
        view.Seek+=fraction=>{if(current!=player)return;var seconds=fraction*scrub.Maximum;updating=true;scrub.Value=seconds;updating=false;Seek(seconds,true);};
        if(tools.Ready)_ = Draw();
        async Task Draw()
        {
            var peaks=await Waveform.PeaksAsync(path,tools.Executable("ffmpeg"));if(token.IsCancellationRequested||current!=player)return;
            if(peaks is null){L10n.SetText(status,"This sound’s waveform could not be drawn.");return;}
            view.Peaks=peaks;view.Visibility=Visibility.Visible;note.Visibility=Visibility.Collapsed;
        }
        return new Grid{Children={current,note,view}};
    }
    private async Task Cleanup(){for(var n=0;n<10;n++){try{if(Directory.Exists(temporary))Directory.Delete(temporary,true);return;}catch(IOException){await Task.Delay(250);}catch(UnauthorizedAccessException){return;}}}
    private async Task Load()
    {
        lifetime.Cancel();lifetime.Dispose();lifetime=new();var token=lifetime.Token;CloseMedia();pdfDocument?.Dispose();pdfDocument=null;content.Content=L10n.Label(new TextBlock{Text="Preparing preview…",Margin=new(20)});Hint(false);
        var path=CurrentPath;caption.Text=$"{Path.GetFileName(path)}  ·  {index+1} / {paths.Length}";Title=Path.GetFileName(path)+" · Preview · Nashi";
        try
        {
            if(Directory.Exists(path)){content.Content=L10n.Label(new TextBlock{Text="Folder · "+Path.GetFileName(path)+"\nUse Open to view its contents.",TextWrapping=TextWrapping.Wrap});return;}
            if(!File.Exists(path))throw new FileNotFoundException("This file has moved or was removed.");
            if(new[]{"docx","doc","rtf","rtfd","pages","xlsx","xls","ods","pptx","ppt","odp","epub"}.Contains(FeatureCatalog.Extension(path)))
            {
                if(office.Executable is null){content.Content=L10n.Label(new TextBlock{Text="Set up office tools in Nashi to preview this document here, or use Open.",TextWrapping=TextWrapping.Wrap});return;}
                Directory.CreateDirectory(temporary);path=(await new DocumentConversion(office,tools).ConvertAsync([path],"pdf",temporary,new(),null,token))[0];token.ThrowIfCancellationRequested();
            }
            if(FeatureCatalog.IsImage(path))content.Content=new Image{Source=ImageEngine.Load(path),Stretch=Stretch.Uniform};
            else if(FeatureCatalog.Extension(path)=="pdf")
            {
                // Load pages on demand rather than decoding every page before showing the first.
                var pdf=await Task.Run(()=>PdfRasterDocument.Open(path),token);if(token.IsCancellationRequested){pdf.Dispose();return;}pdfDocument=pdf;
                var pageIndex=0;var panel=new DockPanel();var controls=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(controls,Dock.Bottom);panel.Children.Add(controls);var image=new Image{Stretch=Stretch.Uniform};panel.Children.Add(image);
                async Task Page(int delta){try{token.ThrowIfCancellationRequested();pageIndex=Math.Clamp(pageIndex+delta,0,pdf.PageCount-1);var requested=pageIndex;var png=await Task.Run(()=>pdf.RenderPage(requested,144,token),token);if(!token.IsCancellationRequested&&requested==pageIndex)image.Source=ImageEngine.FromPng(png);}catch(Exception error){if(!token.IsCancellationRequested&&ReferenceEquals(pdfDocument,pdf))content.Content=L10n.Label(new TextBlock{Text="Preview unavailable. "+error.Message,TextWrapping=TextWrapping.Wrap});}}
                controls.Children.Add(MainWindow.Button("Previous page",async()=>{try{await Page(-1);}catch(OperationCanceledException){}}));controls.Children.Add(MainWindow.Button("Next page",async()=>{try{await Page(1);}catch(OperationCanceledException){}}));controls.Children.Add(new TextBlock{Text=$"{pdf.PageCount} pages",VerticalAlignment=VerticalAlignment.Center});content.Content=panel;await Page(0);
            }
            else if(FeatureCatalog.IsVideo(path)||FeatureCatalog.IsAudio(path))
            {
                player=new MediaElement{Source=new Uri(path),LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Close,Stretch=Stretch.Uniform,ScrubbingEnabled=!FeatureCatalog.IsAudio(path)};var current=player;content.Content=FeatureCatalog.IsAudio(path)?Sound(path,current,token):player;((FrameworkElement)transport.Parent).Visibility=Visibility.Visible;Hint(true);
                current.MediaOpened+=(_,_)=>{if(current!=player)return;scrub.Maximum=current.NaturalDuration.HasTimeSpan?Math.Max(.001,current.NaturalDuration.TimeSpan.TotalSeconds):1;length.Text=Clock(scrub.Maximum);};current.MediaEnded+=(_,_)=>{if(current==player){current.Pause();current.Position=TimeSpan.Zero;playing=false;Paint();}};
                var triedProxy=false;current.MediaFailed+=async(_,e)=>{if(current!=player)return;if(!triedProxy&&tools.Ready){triedProxy=true;try{Directory.CreateDirectory(temporary);var proxy=Path.Combine(temporary,Guid.NewGuid().ToString("N")+(FeatureCatalog.IsAudio(path)?".wav":".mp4"));var args=new List<string>{"-v","error","-nostdin","-i",path};if(FeatureCatalog.IsAudio(path))args.AddRange(["-vn","-c:a","pcm_s16le"]);else args.AddRange(["-vf","scale=w='min(1280,iw)':h=-2","-c:v","libx264","-preset","veryfast","-crf","25","-pix_fmt","yuv420p","-c:a","aac","-movflags","+faststart"]);args.Add(proxy);await ProcessRunner.RunAsync(tools.Executable("ffmpeg"),args,token);if(!token.IsCancellationRequested&&current==player){current.Source=new Uri(proxy);current.Play();current.Pause();}return;}catch(OperationCanceledException){return;}catch(Exception){}}if(!token.IsCancellationRequested&&current==player){CloseMedia();content.Content=L10n.Label(new TextBlock{Text="Set up media tools for this codec, or use Open to preview in its default app.",TextWrapping=TextWrapping.Wrap});}};if(AutoPlay){current.Play();playing=true;}else{current.Play();current.Pause();}Paint();timer.Start();
            }
            else if(new[]{"txt","md","csv","tsv","srt","vtt","json","html","xml","log"}.Contains(FeatureCatalog.Extension(path)))
            {
                if(new FileInfo(path).Length>2_000_000)throw new InvalidDataException("This text is too large to preview. Use Open to read the complete file.");
                var text=await File.ReadAllTextAsync(path,token);if(!token.IsCancellationRequested)content.Content=new TextBox{Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            }
            else content.Content=L10n.Label(new TextBlock{Text="Use Open to preview this document in its default app.",TextWrapping=TextWrapping.Wrap});
        }
        catch(OperationCanceledException){}
        catch(Exception error){if(!token.IsCancellationRequested)content.Content=L10n.Label(new TextBlock{Text="Preview unavailable. "+error.Message,TextWrapping=TextWrapping.Wrap});}
    }
}
