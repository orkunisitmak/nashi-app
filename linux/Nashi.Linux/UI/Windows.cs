using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Nashi.Core;
namespace Nashi.Linux.UI;

/// Edit the common tags of one file, or remove everything. Same tags as Windows (MetadataService.EditableTags).
public sealed class MetadataEditor:Window
{
    private readonly Dictionary<string,TextBox> fields=[];private readonly CheckBox removeAll=new(){Content=L10n.T("Remove all metadata"),IsChecked=true};
    public bool RemoveAll=>removeAll.IsChecked==true;
    public Dictionary<string,string> Updates=>fields.Where(f=>f.Value.Tag as string!=f.Value.Text).ToDictionary(f=>f.Key,f=>f.Value.Text??"");
    public MetadataEditor(IReadOnlyDictionary<string,string> values)
    {
        Title=L10n.T("Metadata")+" · Nashi";Width=620;Height=640;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var stack=new StackPanel{Margin=new(24),Spacing=6};stack.Children.Add(Ui.Title("Metadata",24));stack.Children.Add(Ui.Text("Remove everything, or change the tags below. The original file is never changed.",13,"muted"));stack.Children.Add(removeAll);
        var editable=new StackPanel{Spacing=0,IsEnabled=false};removeAll.IsCheckedChanged+=(_,_)=>editable.IsEnabled=!RemoveAll;
        foreach(var tag in MetadataService.EditableTags){var value=values.GetValueOrDefault(tag,"");var box=new TextBox{Text=value,Tag=value};fields[tag]=box;editable.Children.Add(Ui.Field(tag,box));}
        stack.Children.Add(editable);
        var other=values.Where(v=>!MetadataService.EditableTags.Contains(v.Key)).Take(200).ToList();
        if(other.Count>0){var expander=new Expander{Header=L10n.T("All tags")+$" ({values.Count})",Content=new TextBlock{Text=string.Join("\n",other.Select(v=>v.Key+": "+v.Value)),FontSize=11,TextWrapping=TextWrapping.Wrap}};stack.Children.Add(expander);}
        var save=Ui.Primary("Save",()=>Close(true));save.IsDefault=true;var cancel=Ui.Button("Cancel",()=>Close(false));cancel.IsCancel=true;stack.Children.Add(Ui.Row(0,save,cancel));
        Content=new ScrollViewer{Content=stack};
    }
}

/// Float: a small window that stays on top, holding files to drag into other apps.
public sealed class FloatShelf:Window
{
    private readonly List<string> paths=[];private readonly StackPanel rows=new(){Spacing=4};private readonly MainWindow main;
    public FloatShelf(IEnumerable<string> initial,MainWindow main)
    {
        this.main=main;Title=L10n.T("Float")+" · Nashi";Width=320;Height=360;Topmost=true;CanResize=true;ShowInTaskbar=false;
        var header=new DockPanel();var use=Ui.Button("Use in Nashi",()=>main.UseFiles(paths),"chip");DockPanel.SetDock(use,Dock.Right);header.Children.Add(use);header.Children.Add(Ui.Text("Drag files out",13,"title"));
        DragDrop.SetAllowDrop(this,true);AddHandler(DragDrop.DropEvent,(_,e)=>Add(e.DataTransfer.TryGetFiles()?.Select(f=>f.TryGetLocalPath()).OfType<string>()??[]));
        Content=new DockPanel{Margin=new(14),Children={header.Also(h=>DockPanel.SetDock(h,Dock.Top)),new ScrollViewer{Margin=new(0,10,0,0),Content=rows}}};Add(initial);
    }
    public void Add(IEnumerable<string> more){foreach(var path in more)if(!paths.Contains(path)&&(File.Exists(path)||Directory.Exists(path)))paths.Add(path);Render();}
    private void Render()
    {
        rows.Children.Clear();
        foreach(var path in paths.ToList())
        {
            var row=new Border{Classes={"inset"},Padding=new(10,8),Cursor=new Cursor(StandardCursorType.Hand)};var remove=Ui.Button("✕",()=>{paths.Remove(path);Render();},"chip","Remove");DockPanel.SetDock(remove,Dock.Right);
            row.Child=new DockPanel{Children={remove,Ui.Text(Path.GetFileName(path),12,null,false)}};
            row.PointerPressed+=async(_,e)=>
            {
                if(!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)return;
                if(e.ClickCount==2){LinuxShell.Open(path);return;}
                IStorageItem? item=Directory.Exists(path)?await StorageProvider.TryGetFolderFromPathAsync(path):await StorageProvider.TryGetFileFromPathAsync(path);if(item is null)return;
                var data=new DataTransfer();var entry=DataTransferItem.CreateFile(item);entry.Set(ClipboardFiles.UriList,System.Text.Encoding.UTF8.GetBytes(new Uri(path).AbsoluteUri+"\r\n"));data.Add(entry);
                await DragDrop.DoDragDropAsync(e,data,DragDropEffects.Copy|DragDropEffects.Move);
            };
            rows.Children.Add(row);
        }
        if(paths.Count==0)rows.Children.Add(Ui.Text("Drop files here to keep them handy.",12,"muted"));
    }
}

public sealed class SettingsWindow:Window
{
    public SettingsWindow(MainWindow main)
    {
        Title=L10n.T("Settings")+" · Nashi";Width=560;SizeToContent=SizeToContent.Height;CanResize=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var p=main.preferences.Value;
        var language=Ui.Choice([("en","English"),("tr","Türkçe")],p.Language);var appearance=Ui.Choice([("system","System"),("light","Light"),("dark","Dark")],main.library.Appearance);
        var output=Ui.Choice([("beside","Beside the original"),("folder","In a folder"),("ask","Ask every time")],p.OutputMode);var folder=new TextBox{Text=p.OutputFolder,Watermark=L10n.T("Choose a folder")};
        var downloads=new TextBox{Text=string.IsNullOrWhiteSpace(p.DownloadFolder)||p.DownloadFolder==new Preferences().DownloadFolder&&!Directory.Exists(p.DownloadFolder)?AppPaths.Downloads:p.DownloadFolder};
        var reveal=new CheckBox{Content=L10n.T("Show new files in the file manager when finished"),IsChecked=p.RevealFinished};
        var floating=Ui.Choice([("createNew","Open a new Float"),("addToExisting","Add to the open Float")],p.FloatBehavior);
        Control Browse(TextBox box)=>new DockPanel{Children={Ui.Button("Choose…",async()=>{if(await Dialogs.Folder(this,"Choose a folder") is {} chosen)box.Text=chosen;},"chip").Also(b=>{DockPanel.SetDock(b,Dock.Right);b.Margin=new(8,0,0,0);}),box}};
        var problem=new TextBlock{Foreground=Brushes.IndianRed,TextWrapping=TextWrapping.Wrap};
        var save=Ui.Primary("Save",()=>
        {
            try
            {
                var mode=Ui.Value(output);if(mode=="folder"&&!Directory.Exists(folder.Text))throw new ArgumentException("Choose an existing output folder.");
                main.preferences.Save(p with{Language=Ui.Value(language),OutputMode=mode,OutputFolder=folder.Text??"",DownloadFolder=downloads.Text??AppPaths.Downloads,RevealFinished=reveal.IsChecked==true,FloatBehavior=Ui.Value(floating)});
                main.library.SetAppearance(Ui.Value(appearance));Close(true);
            }
            catch(Exception error)when(error is ArgumentException or IOException or UnauthorizedAccessException){problem.Text=L10n.T(error.Message);}
        });
        save.IsDefault=true;var cancel=Ui.Button("Cancel",()=>Close(false));cancel.IsCancel=true;
        Content=new StackPanel{Margin=new(24),Spacing=2,Children=
        {
            Ui.Title("Settings",24).Also(t=>t.Margin=new(0,0,0,14)),
            Ui.Wrap(Ui.Field("Language",language),new Control{Width=16},Ui.Field("Appearance",appearance)),
            Ui.Field("Save new files",output),Ui.Field("Output folder",Browse(folder)),Ui.Field("Save downloaded media to…",Browse(downloads)),
            Ui.Field("Float",floating),reveal,
            Ui.Text(L10n.T("Library and settings:")+" "+main.library.DirectoryPath,11,"muted",false).Also(t=>t.Margin=new(0,12,0,0)),
            problem,Ui.Row(0,save,cancel).Also(r=>r.Margin=new(0,10,0,0))
        }};
    }
}

/// Finish setup: what is ready, and how to get the rest. Media tools are downloaded with
/// pinned SHA-256 hashes (Nashi.Core Toolchain); LibreOffice and Tesseract come from the distribution.
public sealed class SetupWindow:Window
{
    private readonly MainWindow main;private readonly StackPanel body=new(){Spacing=10};
    public SetupWindow(MainWindow main)
    {
        this.main=main;Title=L10n.T("Finish setup")+" · Nashi";Width=620;SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        Content=new ScrollViewer{Content=new StackPanel{Margin=new(24),Spacing=10,Children={Ui.Title("Finish setup",26),Ui.Text("Core image and PDF tools work right away. The helpers below unlock media, downloads and office documents.",13,"muted"),body}}};Render();
    }
    private void Render()
    {
        body.Children.Clear();
        body.Children.Add(Item("Media tools","FFmpeg, yt-dlp and Deno: audio and video editing, conversions and web downloads.",main.HasMedia&&main.HasDownloader,
            main.HasMedia&&main.HasDownloader?null:Ui.Primary("Download missing tools",InstallMediaAsync),
            main.HasMedia?null:LinuxShell.InstallCommand("ffmpeg")));
        var office=main.office.Executable;
        body.Children.Add(Item("Office tools",office is null?"LibreOffice converts documents, slides and spreadsheets. Install it from your distribution.":"LibreOffice · "+office,office is not null,null,office is null?LinuxOffice.InstallHint:null));
        body.Children.Add(Item("Text recognition","Tesseract reads scanned PDF pages for text exports (optional).",Engine.PdfDocumentMedia.OcrAvailable,null,Engine.PdfDocumentMedia.OcrAvailable?null:LinuxShell.InstallCommand("tesseract-ocr")));
        var exif=main.metadata.Executable;
        body.Children.Add(Item("Metadata tools","ExifTool reads, edits and removes photo and PDF metadata (needs Perl).",exif is not null,exif is null&&LinuxShell.FindInPath("perl") is not null?Ui.Button("Download ExifTool",async()=>{if(await main.SetupStepAsync("Set up metadata tools",(p,t)=>main.metadata.InstallAsync(p,t)))Render();}):null,LinuxShell.FindInPath("perl") is null?LinuxShell.InstallCommand("perl"):null));
        body.Children.Add(Ui.Row(0,Ui.Button("Check again",Render),Ui.Primary("Done",()=>Close())).Also(r=>r.Margin=new(0,8,0,0)));
        body.Children.Add(Ui.Text(L10n.T("Downloaded tools live in")+" "+AppPaths.Tools+". "+L10n.T("Exact versions, hashes and licenses: docs/dependencies.md."),11,"muted",false));
    }
    private static Control Item(string title,string text,bool ready,Control? action,string? command)
    {
        var stack=new StackPanel{Spacing=6};
        var head=new DockPanel();var badge=new Border{Classes={"pill"},Child=Ui.Text(ready?"Ready":"Missing",11)};if(!ready)badge.Classes.Add("warn");DockPanel.SetDock(badge,Dock.Right);head.Children.Add(badge);head.Children.Add(Ui.Text(title,16,"title"));
        stack.Children.Add(head);stack.Children.Add(Ui.Text(text,12,"muted",text.Contains(" · ")?false:true));
        if(command is not null)
        {
            var box=new TextBox{Text=command,IsReadOnly=true,FontFamily=new FontFamily("monospace"),FontSize=12};
            var copy=Ui.Button("Copy",async()=>{if(TopLevel.GetTopLevel(box)?.Clipboard is {} clipboard)await clipboard.SetTextAsync(command);},"chip");DockPanel.SetDock(copy,Dock.Right);copy.Margin=new(8,0,0,0);
            stack.Children.Add(new DockPanel{Children={copy,box}});
        }
        if(action is not null)stack.Children.Add(action);
        return Ui.Card(stack);
    }
    public async Task InstallMediaAsync()
    {
        if(await main.SetupStepAsync("Set up media tools",(p,t)=>main.tools.InstallAsync(p,t))){main.SetStatus("Media tools ready");main.Build();}
        if(IsVisible)Render();
    }
}
