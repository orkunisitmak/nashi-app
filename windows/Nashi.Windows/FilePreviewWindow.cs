using System.Windows;
using System.Windows.Controls;
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
    private MediaElement? player;private bool playing;private readonly Slider scrub=new(){Minimum=0,Maximum=1,MinWidth=140};
    private PdfRasterDocument? pdfDocument;
    private readonly System.Windows.Threading.DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(150)};
    private readonly StackPanel transport=new(){Orientation=Orientation.Horizontal};private bool updating;
    internal string PreviewStatus=>content.Content is TextBlock text?text.Text:content.Content?.GetType().Name??"Empty";
    internal string CurrentPath=>paths[index];internal bool Playing=>playing;
    internal FilePreviewWindow(Window owner,IEnumerable<string> paths,Toolchain? tools=null,OfficeRuntime? office=null)
    {
        this.tools=tools??new Toolchain(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi","Tools","v1"),Environment.GetEnvironmentVariable("PATH"));
        this.office=office??new OfficeRuntime(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi","Tools"));
        this.paths=paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();if(this.paths.Length==0)throw new ArgumentException("Choose a file to preview.");
        Owner=owner;Title="Preview · Nashi";Width=800;Height=620;MinWidth=560;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;Theme.Attach(this);
        var root=new DockPanel{Margin=new(20)};Content=root;var header=new DockPanel{Margin=new(0,0,0,14)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var navigation=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(navigation,Dock.Right);header.Children.Add(navigation);
        navigation.Children.Add(MainWindow.Button("←",()=>Navigate(-1)));navigation.Children.Add(MainWindow.Button("→",()=>Navigate(1)));navigation.Children.Add(MainWindow.Button("Open",()=>{if(File.Exists(CurrentPath)||Directory.Exists(CurrentPath))MainWindow.OpenUrl(CurrentPath);}));header.Children.Add(caption);
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);transport.Children.Add(MainWindow.Button("Play / pause",Toggle));transport.Children.Add(scrub);footer.Children.Add(transport);
        footer.Children.Add(L10n.Label(new TextBlock{Text="←/→ files · Media: Space play/pause, ←/→ 1s, Alt+←/→ files · Esc closes",FontSize=11,Foreground=Theme.Brush("Muted"),Margin=new(0,12,0,0)}));root.Children.Add(content);
        scrub.ValueChanged+=(_,_)=>{if(!updating&&player is not null)player.Position=TimeSpan.FromSeconds(scrub.Value);};
        timer.Tick+=(_,_)=>{if(player is null)return;updating=true;scrub.Value=player.Position.TotalSeconds;updating=false;};
        PreviewKeyDown+=(_,e)=>{var key=e.Key==Key.System?e.SystemKey:e.Key;var modifiers=Keyboard.Modifiers;if(key==Key.Escape){Close();e.Handled=true;return;}if(ClipboardImport.IsEditing(Keyboard.FocusedElement as DependencyObject)&&Keyboard.FocusedElement is not TextBox{IsReadOnly:true})return;
            if(modifiers is not (ModifierKeys.None or ModifierKeys.Alt))return;
            if(key==Key.Space){if(player is null)Close();else Toggle();e.Handled=true;}
            else if(key is Key.Left or Key.Right){var direction=key==Key.Left?-1:1;if(player is not null&&modifiers==ModifierKeys.None){player.Position=TimeSpan.FromSeconds(Math.Clamp(player.Position.TotalSeconds+direction,0,scrub.Maximum));}else Navigate(direction);e.Handled=true;}};
        Loaded+=async(_,_)=>await Load();Closed+=(_,_)=>{lifetime.Cancel();lifetime.Dispose();timer.Stop();CloseMedia();pdfDocument?.Dispose();pdfDocument=null;_ = Cleanup();};
    }
    internal void Navigate(int delta){if(paths.Length<2)return;index=(index+delta+paths.Length)%paths.Length;_ = Load();}
    private void CloseMedia(){player?.Stop();player?.Close();player=null;playing=false;transport.Visibility=Visibility.Collapsed;timer.Stop();}
    private void Toggle(){if(player is null)return;if(playing)player.Pause();else player.Play();playing=!playing;}
    private async Task Cleanup(){for(var n=0;n<10;n++){try{if(Directory.Exists(temporary))Directory.Delete(temporary,true);return;}catch(IOException){await Task.Delay(250);}catch(UnauthorizedAccessException){return;}}}
    private async Task Load()
    {
        lifetime.Cancel();lifetime.Dispose();lifetime=new();var token=lifetime.Token;CloseMedia();pdfDocument?.Dispose();pdfDocument=null;content.Content=L10n.Label(new TextBlock{Text="Preparing preview…",Margin=new(20)});
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
                player=new MediaElement{Source=new Uri(path),LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Close,Stretch=Stretch.Uniform};var current=player;content.Content=player;transport.Visibility=Visibility.Visible;
                current.MediaOpened+=(_,_)=>{if(current!=player)return;scrub.Maximum=current.NaturalDuration.HasTimeSpan?Math.Max(.001,current.NaturalDuration.TimeSpan.TotalSeconds):1;};current.MediaEnded+=(_,_)=>{if(current==player){current.Pause();playing=false;}};
                var triedProxy=false;current.MediaFailed+=async(_,e)=>{if(current!=player)return;if(!triedProxy&&tools.Ready){triedProxy=true;try{Directory.CreateDirectory(temporary);var proxy=Path.Combine(temporary,Guid.NewGuid().ToString("N")+(FeatureCatalog.IsAudio(path)?".wav":".mp4"));var args=new List<string>{"-v","error","-nostdin","-i",path};if(FeatureCatalog.IsAudio(path))args.AddRange(["-vn","-c:a","pcm_s16le"]);else args.AddRange(["-vf","scale=w='min(1280,iw)':h=-2","-c:v","libx264","-preset","veryfast","-crf","25","-pix_fmt","yuv420p","-c:a","aac","-movflags","+faststart"]);args.Add(proxy);await ProcessRunner.RunAsync(tools.Executable("ffmpeg"),args,token);if(!token.IsCancellationRequested&&current==player){current.Source=new Uri(proxy);current.Play();current.Pause();}return;}catch(OperationCanceledException){return;}catch(Exception){}}if(!token.IsCancellationRequested&&current==player){CloseMedia();content.Content=L10n.Label(new TextBlock{Text="Set up media tools for this codec, or use Open to preview in its default app.",TextWrapping=TextWrapping.Wrap});}};current.Play();current.Pause();timer.Start();
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
