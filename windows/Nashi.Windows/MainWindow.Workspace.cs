using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Nashi.Core;
namespace Nashi.Windows;

/// Library is one working area: a desk holding every kept file at once ("All files"),
/// with each saved desk or batch as a collection above it. The Home desk is kept in
/// Library live, so nothing is lost when Nashi closes or is ended.
public sealed partial class MainWindow
{
    private readonly ObservableCollection<string> workspaceFiles=[];
    private FileDesk? workspaceDesk;
    private readonly StackPanel workspacePanel=new(),libraryBrowse=new();
    private readonly TextBlock workspaceTitle=Text("",22,localize:false),workspaceDetail=Text("",12,"Muted",localize:false),workspaceCount=Text("",14,localize:false);
    private Popup? infoPopup;private bool infoPinned;
    /// null: every Library file together; otherwise one collection.
    private Guid? workspaceEntry;private bool loadingWorkspace,quietWorkspace;
    /// The space on Home: its Library entry mirrors the desk (files, card spots, history).
    /// Clear starts a new one; opening a space from Library brings it back here.
    private Guid deskSession=Guid.NewGuid();private bool restoringSpace;
    internal bool WorkspaceOpen=>LibraryVisible;
    internal IReadOnlyList<string> WorkspaceFiles=>workspaceFiles;
    private bool LibraryBrowsing=>LibraryVisible&&workspaceDesk?.IsKeyboardFocusWithin!=true;
    private bool InWorkspace=>LibraryVisible&&workspaceDesk is not null;
    private FileDesk ActiveDesk=>InWorkspace?workspaceDesk!:desk;
    private IReadOnlyList<string> ActiveFiles=>InWorkspace?workspaceFiles:files;
    private void BuildWorkspace()
    {
        workspaceDesk=new(workspaceFiles,preferences,DeskChoices,(choice,paths)=>_=DeskChosenAsync(choice,paths),()=>busy,DuplicateFile){Preview=PreviewFile,Paste=PasteClipboard};
        var header=new DockPanel{Margin=new(0,18,0,0)};
        var send=Button("",()=>{if(busy||workspaceFiles.Count==0)return;var picked=workspaceDesk.Selection.Count>0?workspaceDesk.Selection.ToArray():workspaceFiles.ToArray();Add(picked);ShowLibrary(false);});send.Content=Icons.Labeled("house","Send to Home",14);send.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(send,Dock.Right);header.Children.Add(send);
        var titles=new StackPanel{VerticalAlignment=VerticalAlignment.Center};workspaceTitle.TextTrimming=TextTrimming.CharacterEllipsis;workspaceTitle.TextWrapping=TextWrapping.NoWrap;workspaceTitle.FontWeight=FontWeights.SemiBold;titles.Children.Add(workspaceTitle);titles.Children.Add(workspaceDetail);header.Children.Add(titles);
        workspacePanel.Children.Add(header);
        var toolbar=new DockPanel{Margin=new(20,14,14,6)};workspaceCount.FontWeight=FontWeights.SemiBold;workspaceCount.VerticalAlignment=VerticalAlignment.Center;toolbar.Children.Add(workspaceCount);
        var empty=Text("Nothing here yet. Files you add, make or download land in Library.",14,"Muted");empty.HorizontalAlignment=HorizontalAlignment.Center;empty.VerticalAlignment=VerticalAlignment.Center;
        workspaceDesk.SetChrome(toolbar,empty);workspaceDesk.Margin=new(0,10,0,0);workspacePanel.Children.Add(workspaceDesk);
        workspaceFiles.CollectionChanged+=(_,e)=>{workspaceCount.Text=L10n.Format(workspaceFiles.Count==1?"1 file":"{0} files",workspaceFiles.Count);if(!loadingWorkspace)KeepWorkspace(e);};
        files.CollectionChanged+=(_,_)=>KeepDesk();desk.Moved+=KeepDesk;
        // A file deleted in Explorer while Nashi was in the background shows as deleted on return.
        Activated+=(_,_)=>{if(files.Count==0)return;desk.CheckMissing();QueueActions();};
        libraryPage.Children.Add(libraryBrowse);libraryPage.Children.Add(workspacePanel);
    }
    /// Writes the Home space to Library on every change (files and where cards sit): Alt+F4,
    /// a crash or Task Manager lose nothing.
    private void KeepDesk()
    {
        if(restoringSpace)return;
        try{library.KeepSpace(deskSession,files,desk.Spots);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){L10n.SetText(status,"Library could not be saved. Check access to your Nashi data folder.");}
        if(LibraryVisible)RenderLibrary();
    }
    private void ClearDesk()
    {
        if(busy||files.Count==0)return;deskSession=Guid.NewGuid();files.Clear();Refresh();L10n.SetText(status,"Space saved in Library. The desk is ready for a new one.");Notifications.Shared.Message("Space saved","Open it again from Library, just as you left it.","broom");
    }
    /// Brings a space back to Home exactly as it was left: its files, where each card sat, and
    /// cards for files deleted since, marked so you can see what is gone.
    private void OpenSpace(LibraryEntry entry)
    {
        if(busy)return;
        if(entry.Id!=deskSession)
        {
            restoringSpace=true;
            try{deskSession=entry.Id;files.Clear();foreach(var path in entry.Paths)if(!files.Contains(path))files.Add(path);desk.Restore(entry.Spots);}
            finally{restoringSpace=false;}
            Refresh();
        }
        ShowLibrary(false);var gone=desk.CheckMissing();
        if(gone==0)L10n.SetText(status,"Space opened, just as you left it.");
        else{var note=L10n.Format(gone==1?"1 file in this space was deleted.":"{0} files in this space were deleted.",gone);status.Text=note;Notifications.Shared.Message(L10n.Text("Some files are gone"),note,"warning");}
    }
    private void OpenEntry(LibraryEntry entry){if(entry.IsSpace)OpenSpace(entry);else OpenWorkspace(entry);}
    /// Edits on the Library desk change the Library itself.
    private void KeepWorkspace(System.Collections.Specialized.NotifyCollectionChangedEventArgs change)
    {
        try
        {
            var entry=workspaceEntry is {} id?library.Entries.FirstOrDefault(e=>e.Id==id):null;
            if(entry is not null)library.Keep(entry.Id,entry.Action,workspaceFiles);
            else if(change.Action==System.Collections.Specialized.NotifyCollectionChangedAction.Remove&&change.OldItems is {} gone)library.Forget(gone.Cast<string>());
            else if(change.Action==System.Collections.Specialized.NotifyCollectionChangedAction.Add&&change.NewItems is {} added&&!quietWorkspace)library.Record("Added",added.Cast<string>());
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){L10n.SetText(status,"Library could not be saved. Check access to your Nashi data folder.");}
        RenderLibrary();
    }
    private string[] AllLibraryFiles()
    {
        var query=search.Text.Trim();
        return library.Entries.SelectMany(e=>e.AvailablePaths.Where(p=>query.Length==0||Path.GetFileName(p).Contains(query,StringComparison.OrdinalIgnoreCase)||AppLocalization.Matches(e.Action,query,L10n.Language))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private void LoadWorkspace(IEnumerable<string> paths){loadingWorkspace=true;try{workspaceFiles.Clear();foreach(var path in paths)workspaceFiles.Add(path);}finally{loadingWorkspace=false;}}
    private void OpenWorkspace(LibraryEntry entry)
    {
        if(busy||workspaceDesk is null)return;var existing=entry.AvailablePaths;
        if(existing.Length==0){L10n.SetText(status,"These files have moved or were deleted.");RenderLibrary();return;}
        workspaceEntry=entry.Id;selectedLibraryId=entry.Id;LoadWorkspace(existing);
        workspaceTitle.Text=entry.Name;workspaceDetail.Text=L10n.Text(entry.Action)+"  ·  "+entry.Created.ToLocalTime().ToString("g");
        if(!LibraryVisible)ShowLibrary(true);else RenderLibrary();if(IsLoaded)Theme.Entrance(workspaceDesk);
    }
    private void OpenAllFiles()
    {
        workspaceEntry=null;selectedLibraryId=null;LoadWorkspace(AllLibraryFiles());
        L10n.SetText(workspaceTitle,"All files");L10n.SetText(workspaceDetail,"Everything you added, made or downloaded, together on one desk.");
    }
    /// Keeps the Library desk in step with the Library without reshuffling cards that did not change.
    private void SyncWorkspace()
    {
        if(workspaceDesk is null)return;
        if(workspaceEntry is {} id){if(library.Entries.All(e=>e.Id!=id))OpenAllFiles();return;}
        var all=AllLibraryFiles();if(!all.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(workspaceFiles)||workspaceTitle.Text.Length==0)OpenAllFiles();
    }
    /// Kept for callers that used to leave a collection: Library now falls back to All files.
    private void CloseWorkspace(){if(workspaceEntry is not null){OpenAllFiles();if(LibraryVisible)RenderLibrary();}}
    private Border AllFilesCard()
    {
        var row=new DockPanel();var count=AllLibraryFiles().Length;
        var well=new Border{Width=44,Height=44,CornerRadius=new(10),Margin=new(0,0,12,0),Child=Icons.Create("squares-four",20,"Gold")};well.SetResourceReference(Border.BackgroundProperty,"Sage");DockPanel.SetDock(well,Dock.Left);row.Children.Add(well);
        var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var name=Text("All files",13);name.FontWeight=FontWeights.SemiBold;text.Children.Add(name);text.Children.Add(Text(L10n.Format(count==1?"1 file":"{0} files",count),11,"Muted",localize:false));row.Children.Add(text);
        var card=Card(row);card.Padding=new(12,10,12,10);card.CornerRadius=new(12);card.Width=220;card.Margin=new(0,0,10,0);card.Cursor=Cursors.Hand;card.BorderBrush=Theme.Brush(workspaceEntry is null?"Gold":"Line");card.BorderThickness=new(workspaceEntry is null?2:1);
        AutomationProperties.SetName(card,"All files");card.MouseLeftButtonUp+=(_,_)=>{OpenAllFiles();RenderLibrary();};return card;
    }
    /// Library rows: a preview of the batch, its name and when it was made, and three quiet actions.
    private Border LibraryRow(LibraryEntry entry,bool compact=false)
    {
        var row=new DockPanel();var available=entry.AvailablePaths;var buttons=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(buttons,Dock.Right);row.Children.Add(buttons);
        void Tool(string icon,string tip,string name,Action action,bool enabled)
        {
            var button=new Button{Style=(Style)FindResource("IconButton"),Content=Icons.Create(icon,15),Width=34,Height=34,Margin=new(6,0,0,0),IsEnabled=enabled};button.Click+=(_,e)=>{action();e.Handled=true;};L10n.Bind(button,ToolTipProperty,tip);AutomationProperties.SetName(button,name);buttons.Children.Add(button);
        }
        Tool("eye","Preview","Preview "+entry.Name,()=>PreviewEntry(entry),available.Length>0);
        Tool("folder-open","Show in Explorer","Show "+entry.Name+" in Explorer",()=>{var existing=entry.AvailablePaths;if(existing.Length>0)Reveal(existing[0]);else{L10n.SetText(status,"This file has moved or was deleted.");RenderLibrary();}},available.Length>0);
        if(entry.IsSpace)Tool("house","Open on Home","Open "+entry.Name+" on Home",()=>OpenSpace(entry),!busy);
        else Tool("arrow-u-up-left","Use in Nashi","Use "+entry.Name+" again",()=>{if(busy)return;var existing=entry.AvailablePaths.ToArray();if(existing.Length==0){L10n.SetText(status,"These files have moved or were deleted.");RenderLibrary();return;}Add(existing);CloseWorkspace();ShowLibrary(false);L10n.SetText(status,existing.Length==entry.Paths.Length?"Ready for another round.":"Available files selected. Some files have moved or were deleted.");},!busy&&available.Length>0);
        var thumb=Preview(entry,compact?44:60);thumb.Margin=new(0,0,14,0);DockPanel.SetDock(thumb,Dock.Left);row.Children.Add(thumb);
        var text=new StackPanel{Margin=new(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};var name=Text(entry.Name,13,localize:false);name.FontWeight=FontWeights.SemiBold;name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;name.ToolTip=string.Join(Environment.NewLine,entry.Paths);text.Children.Add(name);
        var info=new WrapPanel{Margin=new(0,3,0,0)};
        if(entry.IsSpace)
        {
            // A space: when it was last worked in, what it holds, what was done there, and what is gone.
            var steps=entry.History?.Length??0;var gone=entry.Paths.Length-available.Length;
            var tag=new Border{CornerRadius=new(5),Padding=new(6,0,6,1),Margin=new(0,0,6,0),Child=Text(entry.Id==deskSession?"Space · on Home":"Space",10.5,"Gold")};tag.SetResourceReference(Border.BackgroundProperty,"Sage");info.Children.Add(tag);
            info.Children.Add(Text((entry.Touched??entry.Created).ToLocalTime().ToString("g"),11,"Muted",localize:false));
            info.Children.Add(Text("  ·  "+L10n.Format(entry.Paths.Length==1?"1 file":"{0} files",entry.Paths.Length),11,"Muted",localize:false));
            if(steps>0)info.Children.Add(Text("  ·  "+L10n.Format(steps==1?"1 step":"{0} steps",steps),11,"Muted",localize:false));
            if(gone>0)info.Children.Add(Text("  ·  "+L10n.Format(gone==1?"1 deleted":"{0} deleted",gone),11,"Gold",localize:false));
            name.ToolTip=string.Join(Environment.NewLine,entry.Paths.Select(p=>File.Exists(p)||Directory.Exists(p)?p:"✕ "+p).Concat(entry.History is {Length:>0} h?new[]{""}.Concat(h.TakeLast(8).Select(e=>e.At.ToLocalTime().ToString("t")+"  "+L10n.Text(e.Action)+(e.Outputs>0?"  → "+e.Outputs:""))):Enumerable.Empty<string>()));
        }
        else
        {
            info.Children.Add(Text(entry.Action,11,"Muted"));info.Children.Add(Text("  ·  "+entry.Created.ToLocalTime().ToString("g"),11,"Muted",localize:false));if(entry.Paths.Length>1)info.Children.Add(Text("  ·  "+L10n.Format("{0} files",entry.Paths.Length),11,"Muted",localize:false));if(available.Length<entry.Paths.Length)info.Children.Add(Text("  ·  "+L10n.Text("Files missing"),11,"Muted",localize:false));
        }
        text.Children.Add(info);row.Children.Add(text);
        var card=Card(row);card.Padding=new(12,10,12,10);card.CornerRadius=new(12);card.Cursor=Cursors.Hand;L10n.Bind(card,ToolTipProperty,entry.IsSpace?"Open this space on Home":"Open as a desk");
        if(compact){card.BorderThickness=new(0);card.Margin=new(0,0,0,2);card.MouseLeftButtonUp+=(_,e)=>{if(!IsButton(e.OriginalSource as DependencyObject))OpenEntry(entry);};}
        else{card.Width=340;card.Margin=new(0,0,10,0);ConfigureLibraryRow(card,entry);if(workspaceEntry==entry.Id){card.BorderBrush=Theme.Brush("Gold");card.BorderThickness=new(2);}}
        return card;
    }
    /// The first image of a batch (or the file's own icon), with a count when there are several.
    private static FrameworkElement Preview(LibraryEntry entry,double size)
    {
        var well=new Grid{Width=size,Height=size,Clip=new RectangleGeometry(new(0,0,size,size),10,10)};var frame=new Border{Width=size,Height=size,CornerRadius=new(10),Child=well};frame.SetResourceReference(Border.BackgroundProperty,"Sand");
        var icon=new Image{Width=size*.42,Height=size*.42,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};FileArtwork.Bind(icon,entry.Paths[0]);well.Children.Add(icon);
        if((entry.AvailablePaths.FirstOrDefault(FeatureCatalog.IsImage)??entry.AvailablePaths.FirstOrDefault(File.Exists)) is {} image)_=Fill();
        async Task Fill(){if(await Thumbnails.LoadAsync(image,(int)(size*2)) is {} bitmap){well.Children.Insert(0,new Image{Source=bitmap,Stretch=Stretch.UniformToFill});icon.Visibility=Visibility.Collapsed;}}
        if(entry.Paths.Length>1)
        {
            var count=new Border{CornerRadius=new(6),Padding=new(5,1,5,1),Margin=new(3),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Child=Text(entry.Paths.Length.ToString(),10,"Ink",localize:false)};count.SetResourceReference(Border.BackgroundProperty,"Surface");well.Children.Add(count);
        }
        return frame;
    }
    /// Shortcut and feature help: shows while hovering the info button, stays after a click until you click elsewhere.
    private void BuildInfo()
    {
        if(infoButton is null)return;
        infoPopup=new Popup{PlacementTarget=infoButton,Placement=PlacementMode.Bottom,AllowsTransparency=true,StaysOpen=true,PopupAnimation=PopupAnimation.Fade,VerticalOffset=8};
        infoPopup.Opened+=(_,_)=>{if(infoPopup.Child is FrameworkElement panel)infoPopup.HorizontalOffset=infoButton.ActualWidth-panel.ActualWidth;};
        infoPopup.Closed+=(_,_)=>{infoPinned=false;infoPopup.StaysOpen=true;};
        void LeaveSoon(){var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(220)};timer.Tick+=(_,_)=>{timer.Stop();if(!infoPinned&&!infoButton.IsMouseOver&&infoPopup.Child?.IsMouseOver!=true)infoPopup.IsOpen=false;};timer.Start();}
        infoButton.MouseEnter+=(_,_)=>{if(!infoPopup.IsOpen){infoPopup.Child=InfoPanel();infoPopup.IsOpen=true;}};
        infoButton.MouseLeave+=(_,_)=>LeaveSoon();
        infoButton.Click+=(_,_)=>
        {
            if(infoPinned){infoPopup.IsOpen=false;return;}
            // A non-staying popup closes on the next click outside it; reopen so it takes that mode.
            infoPopup.IsOpen=false;infoPopup.Child=InfoPanel();infoPopup.StaysOpen=false;infoPopup.IsOpen=true;infoPinned=true;
        };
    }
    private FrameworkElement InfoPanel()
    {
        var p=preferences.Value;var columns=new Grid();columns.ColumnDefinitions.Add(new());columns.ColumnDefinitions.Add(new(){Width=new(28)});columns.ColumnDefinitions.Add(new());
        StackPanel Section(StackPanel host,string title,params (string Key,string Label)[] rows)
        {
            var heading=Text(title,13);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new(0,host.Children.Count==0?0:14,0,8);host.Children.Add(heading);
            foreach(var (key,label) in rows)
            {
                var row=new DockPanel{Margin=new(0,0,0,6)};var chip=new Border{CornerRadius=new(6),Padding=new(7,2,7,2),Margin=new(0,0,10,0),MinWidth=92,Child=Text(key,11,"Ink",localize:false)};chip.SetResourceReference(Border.BackgroundProperty,"Inset");DockPanel.SetDock(chip,Dock.Left);row.Children.Add(chip);var text=Text(label,12);text.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(text);host.Children.Add(row);
            }
            return host;
        }
        var left=new StackPanel();var right=new StackPanel();Grid.SetColumn(right,2);columns.Children.Add(left);columns.Children.Add(right);
        Section(left,"On the desk",(p.ConvertModifier,"Convert"),(p.ToolsModifier,"Tools"),(p.ActionsModifier,"File actions"),("Drag + key","Wheel at the pointer"),("Tap the key","Wheel for the selected cards"),("Ctrl+Click","Select several cards"),("Enter","Open tools"),("Ctrl+D","Duplicate a card"),("Delete","Remove a card"));
        string KeyOf(string id)=>Shortcuts.Display(Shortcuts.Gesture(p,id));
        Section(left,"Anywhere",(KeyOf("open"),"Choose files"),(KeyOf("paste"),"Paste files, text, an image or link"),(KeyOf("tools"),"Open tools"),(KeyOf("home")+" / "+KeyOf("library"),"Home / Library"),(KeyOf("find"),"Find in Library"),(KeyOf("download"),"Use a copied download link"),(KeyOf("updates"),"Check for updates"));
        Section(right,"Wheel",("Arrows / Tab","Choose"),("Enter","Open"),("Esc","Close"));
        Section(right,"Media",("Space","Play / pause"),("← / →","1 second"),("Shift+← / →","One frame"),("I / O","Set range"));
        Section(right,"Crop",("Shift","Lock the ratio"),("Enter","Save"));
        var features=Text("Convert, edit and compress photos, video, audio and PDFs, and download public media from a link. Files stay on your computer.",12,"Muted");features.Margin=new(0,14,0,0);right.Children.Add(features);
        var panel=new Border{Width=640,CornerRadius=new(12),BorderThickness=new(1),Padding=new(20,18,20,18),Child=columns};panel.SetResourceReference(Border.BackgroundProperty,"Surface");panel.SetResourceReference(Border.BorderBrushProperty,"Line");
        panel.Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=24,ShadowDepth=4,Direction=270,Opacity=.22,Color=Color.FromRgb(0x08,0x0A,0x0D)};
        panel.MouseLeave+=(_,_)=>{if(!infoPinned&&infoButton?.IsMouseOver!=true&&infoPopup is not null)infoPopup.IsOpen=false;};
        var outer=new Border{Padding=new(0,0,12,16),Child=panel};outer.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty,FontFamily);outer.SetValue(System.Windows.Documents.TextElement.FontSizeProperty,13.0);outer.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,"Ink");return outer;
    }
}
