#if DEBUG
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nashi.Core;

namespace Nashi.Windows;

internal static class ToolButtonsSmokeChecks
{
    internal static int Run(string output)
    {
        output=Path.GetFullPath(output);
        if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new IOException("Choose an empty tool-button smoke-test folder.");
        Directory.CreateDirectory(output);
        var checks=new List<object>();
        var failures=new List<string>();
        var progressPath=Path.Combine(output,"progress.txt");
        var progress=new List<string>();
        void Mark(string value){progress.Add(value);File.WriteAllLines(progressPath,progress);}
        void Check(string name,bool passed,string? detail=null)
        {
            checks.Add(new{name,passed,detail});
            if(!passed)failures.Add(name+(detail is null?"":": "+detail));
        }
        var data=Path.Combine(output,"isolated-data");Directory.CreateDirectory(data);
        var library=new LibraryStore(data);
        new PreferencesStore(data).Save(new Preferences{Language="en",PillModeEnabled=true,PillVisibilityMode="shortcut",PillHotkey="Ctrl+Alt+N",PillOnboardingCompleted=true,AutomaticUpdateChecks=false,KeepInTray=false,Feedback=false,Animations=false});
        var drawing=new DrawingVisual();
        using(var context=drawing.RenderOpen())
        {
            context.DrawRectangle(Brushes.White,null,new Rect(0,0,320,180));
            context.DrawEllipse(Brushes.Goldenrod,null,new Point(160,90),62,62);
            context.DrawEllipse(Brushes.DarkSlateGray,null,new Point(140,80),5,5);
            context.DrawEllipse(Brushes.DarkSlateGray,null,new Point(180,80),5,5);
        }
        var bitmap=new RenderTargetBitmap(320,180,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);
        var image=Path.Combine(output,"Synthetic image.png");ImageEngine.Save(bitmap,image,"png");
        var window=new MainWindow([image],library,checkForUpdates:false,offerToolSetup:false);
        try
        {
            window.Show();DesktopInput.Pump(150);
            window.UIAuditPresentIsland(true,"actions");DesktopInput.Pump(250);
            var toolButtons=FeatureCatalog.ToolNames.OrderBy(pair=>pair.Value,StringComparer.CurrentCultureIgnoreCase).ToArray();
            Check("all-tool-buttons-render",window.UIAuditToolButtons.Count==toolButtons.Length,$"expected {toolButtons.Length}, got {window.UIAuditToolButtons.Count}");
            var imageChoices=FeatureCatalog.Tools([image]).Select(id=>id switch{"cropImage"=>"crop","annotateImage"=>"annotate","mergePDF"=>"mergePDFs",_=>id}).ToHashSet(StringComparer.Ordinal);
            foreach(var (id,title) in toolButtons)
            {
                Mark("clicking "+id);
                window.Activate();DesktopInput.Pump(80);
                var choiceId=id switch{"cropImage"=>"crop","annotateImage"=>"annotate","mergePDF"=>"mergePDFs",_=>id};
                var button=window.UIAuditToolButtons.FirstOrDefault(item=>AutomationProperties.GetName(item)==L10n.Text(title));
                if(button is null){Check("tool-button:"+id,false,"visible button was not found");continue;}
                var opened=ClickAndDismiss(window,button);
                Mark("clicked "+id);
                if(imageChoices.Contains(choiceId))
                {
                    Check("tool-button:"+id,opened is not null,opened is null?"click did not open an editor or result window":null);
                }
                else
                {
                    var picker=opened as NashiFilePickerWindow;
                    string[] expectedFilter=id=="audioToVideo"?new[]{"flac","m4a","mp3","wav"}:id is "organizePDF" or "splitPDF"?["pdf"]:Array.Empty<string>();
                    var correctPicker=picker is not null&&picker.UIAuditAllowedExtensions.SequenceEqual(expectedFilter,StringComparer.OrdinalIgnoreCase);
                    Check("tool-button:"+id,correctPicker,picker is null?"click did not open a compatible file picker":"file picker filters differ from the tool input type");
                }
                DesktopInput.Pump(100);
            }

            foreach(var (key,tab) in new[]{("now","now"),("tray","tray"),("media","media"),("notes","notes"),("todo","todo"),("tools","actions")})
            {
                var clicked=window.UIAuditClickDock(key);DesktopInput.Pump(80);
                Check("dock-button:"+key,clicked&&window.UIAuditIslandTab==tab,$"clicked={clicked}, tab={window.UIAuditIslandTab}");
            }
            var searchClicked=window.UIAuditClickDock("search");var searchOpened=searchClicked&&window.UIAuditActionsPopupOpen;
            Check("dock-button:search",searchOpened,"search did not open the action popover");
            if(searchOpened)DesktopInput.Key(window,Key.Escape);
            SettingsWindow? openedSettings=null;var settingsClosed=false;
            var settingsTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};
            settingsTimer.Tick+=(_,_)=>{openedSettings=Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();if(openedSettings is not null){settingsTimer.Stop();openedSettings.DialogResult=false;settingsClosed=true;}};
            settingsTimer.Start();var settingsClicked=window.UIAuditClickDock("settings");WaitUntil(()=>settingsClosed,TimeSpan.FromSeconds(5));settingsTimer.Stop();
            Check("dock-button:settings",settingsClicked&&openedSettings is not null,"Settings did not open from the dock");

            var mascot=FileGarden.Artwork("NashiMark");
            Check("new-mascot-resource-is-used",mascot.PixelWidth==1254&&mascot.PixelHeight==1254,$"resource dimensions were {mascot.PixelWidth}×{mascot.PixelHeight}");
            var icon=window.Icon as BitmapFrame;
            Check("window-icon-uses-new-mascot",icon?.PixelWidth==mascot.PixelWidth&&icon.PixelHeight==mascot.PixelHeight,$"window icon is {icon?.PixelWidth}×{icon?.PixelHeight}; mascot is {mascot.PixelWidth}×{mascot.PixelHeight}");

            var toolDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi","Tools","v1");
            var toolchain=new Toolchain(toolDirectory,Environment.GetEnvironmentVariable("PATH"));
            if(!toolchain.Ready)throw new InvalidOperationException("Media tools are not set up, so Audio Visualizer cannot be rendered.");
            var audio=Path.Combine(output,"Synthetic tone.wav");
            ProcessRunner.RunAsync(toolchain.Executable("ffmpeg"),["-v","error","-f","lavfi","-i","sine=frequency=440:duration=2","-c:a","pcm_s16le",audio]).GetAwaiter().GetResult();
            window.UIAuditSetFiles([audio]);window.UIAuditPresentIsland(true,"actions");DesktopInput.Pump(120);
            Check("audio-visualizer-fixture-selected",window.SelectedFiles.SequenceEqual([audio])&&File.Exists(audio),$"selected={string.Join(", ",window.SelectedFiles)}, exists={File.Exists(audio)}");
            var visualizer=window.UIAuditToolButtons.FirstOrDefault(item=>AutomationProperties.GetName(item)==L10n.Text("Audio Visualizer"));
            if(visualizer is null)Check("audio-visualizer-render",false,"Audio Visualizer button was not present");
            else
            {
                Mark("clicking audioToVideo with WAV");
                ToolEditor? editor=null;string? previewFailure=null;string? renderedPreviewPath=null;var previewClicked=false;var previewFinished=false;MediaInfo? renderedInfo=null;var started=DateTime.UtcNow;
                var previewTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(120)};
                previewTimer.Tick+=(_,_)=>
                {
                    if(editor is null)
                    {
                        editor=Application.Current.Windows.OfType<ToolEditor>().FirstOrDefault();
                        if(editor is null&&DateTime.UtcNow-started>TimeSpan.FromSeconds(15)){previewFailure="Audio Visualizer editor did not open.";previewFinished=true;previewTimer.Stop();return;}
                        if(editor is null)return;
                        editor.FieldForTest("Width").Text="320";editor.FieldForTest("Height").Text="180";
                        var previewButton=VisualDescendants(editor).OfType<Button>().FirstOrDefault(item=>item.Content as string=="Preview Visualization");
                        if(previewButton is null){previewFailure="Preview Visualization button was missing.";previewFinished=true;previewTimer.Stop();editor.DialogResult=false;return;}
                        previewTimer.Stop();DesktopInput.Click(previewButton);previewClicked=true;previewTimer.Start();return;
                    }
                    if(editor.ProcessedPreviewPath is { } previewPath&&File.Exists(previewPath)&&new FileInfo(previewPath).Length>0)
                    {
                        renderedPreviewPath=Path.Combine(output,"Audio Visualizer preview.mp4");File.Copy(previewPath,renderedPreviewPath,true);
                        previewFinished=true;previewTimer.Stop();editor.DialogResult=false;return;
                    }
                    if(DateTime.UtcNow-started>TimeSpan.FromSeconds(90)){previewFailure=editor.UIAuditPreviewStatus+"; input="+editor.UIAuditInputPath+"; exists="+File.Exists(editor.UIAuditInputPath);previewFinished=true;previewTimer.Stop();editor.DialogResult=false;}
                };
                previewTimer.Start();window.Activate();DesktopInput.Click(visualizer);WaitUntil(()=>previewFinished,TimeSpan.FromSeconds(95));previewTimer.Stop();
                if(renderedPreviewPath is not null)
                {
                    var inspectTask=new MediaEngine(toolchain.Executable("ffmpeg"),toolchain.Executable("ffprobe")).InspectAsync(renderedPreviewPath);
                    WaitUntil(()=>inspectTask.IsCompleted,TimeSpan.FromSeconds(12));
                    if(inspectTask.IsCompleted)try{renderedInfo=inspectTask.GetAwaiter().GetResult();}catch(Exception error){previewFailure=error.Message;}
                    else previewFailure="The generated preview metadata check timed out.";
                }
                var rendered=previewFinished&&previewClicked&&renderedPreviewPath is { } preview&&File.Exists(preview)&&new FileInfo(preview).Length>0;
                Check("audio-visualizer-render",rendered,rendered?$"Generated playable preview: {Path.GetFileName(renderedPreviewPath)}.":previewFailure??"Audio Visualizer did not produce a playable preview.");
                Check("audio-visualizer-preview-has-audio-and-video",renderedInfo?.Audio is not null&&renderedInfo.Video is not null,$"audio={renderedInfo?.Audio is not null}, video={renderedInfo?.Video is not null}");
            }
        }
        finally{window.Close();}
        File.WriteAllText(Path.Combine(output,"report.json"),System.Text.Json.JsonSerializer.Serialize(new{checks=checks.Count,failed=failures.Count,failures,results=checks},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{checks=checks.Count,failed=failures.Count,output}));
        return failures.Count==0?0:1;
    }

    private static Window? ClickAndDismiss(MainWindow owner,Button button)
    {
        Window? opened=null;var finished=false;var started=DateTime.UtcNow;var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};
        timer.Tick+=(_,_)=>
        {
            opened=Application.Current.Windows.OfType<Window>().FirstOrDefault(candidate=>ReferenceEquals(candidate.Owner,owner)&&candidate.IsVisible);
            if(opened is not null){timer.Stop();opened.DialogResult=false;finished=true;return;}
            if(DateTime.UtcNow-started>TimeSpan.FromSeconds(8)){timer.Stop();finished=true;}
        };
        timer.Start();DesktopInput.Click(button);WaitUntil(()=>finished,TimeSpan.FromSeconds(9));timer.Stop();return opened;
    }

    private static bool WaitUntil(Func<bool> read,TimeSpan timeout)
    {
        var until=DateTime.UtcNow+timeout;
        while(DateTime.UtcNow<until)
        {
            DesktopInput.Pump(35);
            if(read())return true;
        }
        return read();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(var index=0;index<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);index++)
        {
            var child=System.Windows.Media.VisualTreeHelper.GetChild(root,index);
            yield return child;
            foreach(var nested in Descendants(child))yield return nested;
        }
    }

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)=>Descendants(root);
}
#endif
