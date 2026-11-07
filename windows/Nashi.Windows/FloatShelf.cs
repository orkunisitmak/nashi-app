using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
namespace Nashi.Windows;

/// A compact file stack that opens into a shelf, as in the design. Folded, it is clear glass with the
/// files fanned on it; open, a frosted panel with a title row, the files and a hint. Drag the files to carry
/// them out; drag the glass (or the title row) to move the shelf.
public sealed class FloatShelf:Window
{
    private const double Folded=112,FoldedHeight=136,Open=284;
    private readonly ListBox files=new(){SelectionMode=SelectionMode.Extended,BorderThickness=new(0),Background=Brushes.Transparent,Padding=new(0)};
    private readonly List<string> paths=[];
    private readonly Func<IReadOnlyList<string>,string,Task> action;
    private readonly Action<IReadOnlyList<string>> reuse;
    private readonly Grid root=new(),header=new();
    private readonly RoundBorder glass=new(){Radius=26,BorderThickness=new(1)};
    private readonly Grid frost=new(){Visibility=Visibility.Collapsed};
    // The glass itself: it flows between the folded and open shapes inside the window (the window resizes only once).
    private readonly Grid shell=new(){HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Width=Folded,Height=FoldedHeight};
    private readonly Canvas stack=new(){Width=70,Height=70,Background=Brushes.Transparent,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Cursor=Cursors.Hand};
    private readonly Lens lens=new(){Preset="clear",Radius=26,GlintOnly=true};
    private readonly TextBlock badge=new(){FontSize=11,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center},count=new(){FontSize=11,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    private readonly Border badgePlate=new(){MinWidth=19,Height=19,CornerRadius=new(9.5),Padding=new(6,0,6,0),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,12,12,0),IsHitTestVisible=false};
    private readonly DockPanel titleRow=new(){Margin=new(8,2,0,8)};
    private readonly Border foot=new(){BorderThickness=new(0,1,0,0),Padding=new(8,8,8,2),Margin=new(0,4,0,0)};
    private readonly TextBlock status=new(){FontSize=11,LineHeight=14,TextWrapping=TextWrapping.Wrap};
    private readonly Button close,options;private readonly (Button Button,RoundBorder Drop) gridView,listView;
    private readonly DispatcherTimer hover=new();
    private bool icons,interacting,dragging,hoverInside;
    private Point mouseOrigin;
    private string? edge;
    internal bool IsExpanded{get;private set;}
    internal IReadOnlyList<string> Paths=>paths;

    public FloatShelf(IEnumerable<string> initial,Func<IReadOnlyList<string>,string,Task> action,Action<IReadOnlyList<string>> reuse)
    {
        this.action=action;this.reuse=reuse;Theme.Attach(this);Title="Float · Nashi";
        Width=Folded;Height=FoldedHeight;MinWidth=Folded;MinHeight=FoldedHeight;Topmost=true;ShowInTaskbar=false;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.NoResize;AllowDrop=true;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        // Folded: clear glass that shows what is really behind it, with a lit rim. Open: the frosted panel (what is behind, blurred).
        frost.Children.Add(new Backdrop{Tint="Panel",Radius=24});glass.SetResourceReference(Border.BorderBrushProperty,"Rim");
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        shell.Children.Add(frost);shell.Children.Add(lens);shell.Children.Add(glass);shell.Children.Add(root);Content=new Grid{Children={shell}};Grid.SetRowSpan(header,3);root.Children.Add(header);header.Children.Add(stack);
        badgePlate.SetResourceReference(Border.BackgroundProperty,"Solid");badge.SetResourceReference(TextBlock.ForegroundProperty,"OnSolid");badgePlate.Child=badge;header.Children.Add(badgePlate);
        // The title row: Float, the count, icons or list, more, close.
        close=Control("x","Close Float without deleting files",Close);options=Control("ellipsis","Float options",Options);
        var title=new TextBlock{Text="Float",FontSize=13,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(title,Dock.Left);titleRow.Children.Add(title);
        var countPlate=new Border{MinWidth=19,Height=19,CornerRadius=new(9.5),Padding=new(6,0,6,0),Margin=new(8,0,0,0),VerticalAlignment=VerticalAlignment.Center,Child=count};countPlate.SetResourceReference(Border.BackgroundProperty,"Sel");count.SetResourceReference(TextBlock.ForegroundProperty,"Muted");DockPanel.SetDock(countPlate,Dock.Left);titleRow.Children.Add(countPlate);
        DockPanel.SetDock(close,Dock.Right);titleRow.Children.Add(close);DockPanel.SetDock(options,Dock.Right);titleRow.Children.Add(options);
        var views=new RoundBorder{Radius=99,Padding=new(3),Margin=new(0,0,6,0),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right};views.SetResourceReference(Border.BackgroundProperty,"Sel");var pair=new StackPanel{Orientation=Orientation.Horizontal};views.Child=pair;
        (Button,RoundBorder) View(string icon,string help,bool mode)
        {
            // The chosen view rests on the same glass droplet as every segmented control (36 tall, hanging below the track).
            var drop=new RoundBorder{Radius=99,Height=36,Margin=new(0,0,0,-11),VerticalAlignment=VerticalAlignment.Top,BorderThickness=new(1),IsHitTestVisible=false};drop.SetResourceReference(Border.BackgroundProperty,"Raised");drop.SetResourceReference(Border.BorderBrushProperty,"ThumbRim");drop.SetResourceReference(EffectProperty,"ThumbShadow");
            var b=Control(icon,help,()=>{if(icons==mode)return;icons=mode;Reload();SetExpanded(true,true);});b.Width=34;b.Height=25;b.Margin=new(0);var cell=new Grid{Children={drop,b}};pair.Children.Add(cell);return(b,drop);
        }
        gridView=View("grid","Icons",true);listView=View("list","List",false);titleRow.Children.Add(views);
        Grid.SetRow(titleRow,0);root.Children.Add(titleRow);
        status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");foot.Child=status;foot.SetResourceReference(Border.BorderBrushProperty,"Line");
        Grid.SetRow(files,1);root.Children.Add(files);Grid.SetRow(foot,2);root.Children.Add(foot);
        ScrollViewer.SetHorizontalScrollBarVisibility(files,ScrollBarVisibility.Disabled);
        // Anything that is not a file, a button or the stack moves the shelf: the glass around the stack, the title row, the hint.
        MouseLeftButtonDown+=(_,e)=>{if(e.ButtonState==MouseButtonState.Pressed){MoveShelf();e.Handled=true;}};
        stack.MouseLeftButtonDown+=(_,e)=>{mouseOrigin=e.GetPosition(stack);dragging=false;interacting=true;hover.Stop();stack.CaptureMouse();e.Handled=true;};
        stack.MouseMove+=(_,e)=>
        {
            if(!stack.IsMouseCaptured||e.LeftButton!=MouseButtonState.Pressed||(e.GetPosition(stack)-mouseOrigin).Length<4)return;
            dragging=true;stack.ReleaseMouseCapture();Carry(stack,paths.ToArray());e.Handled=true;
        };
        stack.MouseLeftButtonUp+=(_,e)=>{if(!stack.IsMouseCaptured)return;stack.ReleaseMouseCapture();interacting=false;if(!dragging)SetExpanded(!IsExpanded);e.Handled=true;};
        files.PreviewMouseLeftButtonDown+=(_,e)=>mouseOrigin=e.GetPosition(files);
        files.MouseMove+=(_,e)=>{if(e.LeftButton==MouseButtonState.Pressed&&(e.GetPosition(files)-mouseOrigin).Length>8&&files.SelectedItems.Count>0)Carry(files,Selection());};
        files.SelectionChanged+=(_,_)=>UpdateLabels();
        files.MouseDoubleClick+=(_,_)=>{foreach(var p in Selection())MainWindow.OpenUrl(p);};PreviewKeyDown+=OnKey;
        MouseEnter+=(_,_)=>ScheduleHover(true);MouseLeave+=(_,_)=>ScheduleHover(false);
        hover.Tick+=(_,_)=>{hover.Stop();if(interacting||Mouse.LeftButton==MouseButtonState.Pressed)return;if(hoverInside&&IsMouseOver)SetExpanded(true);else if(!hoverInside&&!IsMouseOver)SetExpanded(false);};
        DragOver+=(_,e)=>{e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
        Drop+=(_,e)=>{if(e.Data.GetData(DataFormats.FileDrop) is string[] incoming)Add(incoming);e.Handled=true;};
        files.ContextMenu=Menu();stack.ContextMenu=Menu();
        LanguageState.Shared.PropertyChanged+=LanguageChanged;Closed+=(_,_)=>{hover.Stop();LanguageState.Shared.PropertyChanged-=LanguageChanged;};
        Add(initial);Present();
    }
    private void LanguageChanged(object? sender,PropertyChangedEventArgs e){Reload();files.ContextMenu=Menu();stack.ContextMenu=Menu();}
    private Button Control(string icon,string help,Action click)
    {
        var b=new Button{Style=Ui.Style("IconPlain"),Content=Icons.Make(icon,15),ToolTip=L10n.Text(help),Width=28,Height=28,Margin=new(2,0,0,0)};
        System.Windows.Automation.AutomationProperties.SetName(b,L10n.Text(help));b.Click+=(_,_)=>click();return b;
    }
    private void ScheduleHover(bool inside){hover.Stop();hoverInside=inside;hover.Interval=TimeSpan.FromMilliseconds(inside?200:650);hover.Start();}
    private void MoveShelf(){interacting=true;hover.Stop();try{DragMove();edge=DesktopGeometry.Snap(this,null,true);}catch(InvalidOperationException){}finally{interacting=false;ScheduleHover(IsMouseOver);}}
    private void Carry(DependencyObject source,string[] selected)
    {
        if(selected.Length==0){interacting=false;return;}interacting=true;hover.Stop();
        try{DragDrop.DoDragDrop(source,new DataObject(DataFormats.FileDrop,selected),DragDropEffects.Copy|DragDropEffects.Move);}finally{interacting=false;Reload();ScheduleHover(false);}
    }
    internal void Add(IEnumerable<string> incoming)
    {
        foreach(var p in ShellShortcut.Resolve(incoming).Where(p=>File.Exists(p)||Directory.Exists(p)).Take(1000)){var full=Path.GetFullPath(p);if(!paths.Contains(full,StringComparer.OrdinalIgnoreCase))paths.Add(full);}Reload();
    }
    private string[] Selection()=>files.SelectedItems.Count>0?files.SelectedItems.Cast<ListBoxItem>().Select(i=>(string)i.Tag).ToArray():paths.Where(p=>File.Exists(p)||Directory.Exists(p)).ToArray();
    // Where a file lives, short: the home folder reads as ~.
    private static string Folder(string path)
    {
        var folder=Path.GetDirectoryName(path)??"";var home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return folder.StartsWith(home,StringComparison.OrdinalIgnoreCase)?"~"+folder[home.Length..]:folder;
    }
    private void Reload()
    {
        var selected=files.SelectedItems.Cast<ListBoxItem>().Select(i=>(string)i.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);files.Items.Clear();
        files.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(icons?typeof(WrapPanel):typeof(StackPanel)));
        for(var n=0;n<paths.Count;n++)
        {
            var path=paths[n];FrameworkElement content;
            if(icons)
            {
                // Icons: a larger thumbnail with the name under it, three to a row.
                var tile=new StackPanel{Width=76};tile.Children.Add(FloatArtwork.Image(path,56));
                tile.Children.Add(new TextBlock{Text=Path.GetFileName(path),FontSize=11,LineHeight=13,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,TextAlignment=TextAlignment.Center,MaxHeight=27,Margin=new(0,6,0,0)});content=tile;
            }
            else
            {
                // List: like the Library — the thumbnail, the name and where it lives, with a hairline between rows.
                var row=new DockPanel{MinHeight=36};var thumb=FloatArtwork.Image(path,36);DockPanel.SetDock(thumb,Dock.Left);row.Children.Add(thumb);
                var words=new StackPanel{Margin=new(12,0,0,0),VerticalAlignment=VerticalAlignment.Center};words.Children.Add(new TextBlock{Text=Path.GetFileName(path),FontSize=13,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis});
                var where=new TextBlock{Text=Folder(path),FontSize=11.5,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,2,0,0)};where.SetResourceReference(TextBlock.ForegroundProperty,"Muted");words.Children.Add(where);row.Children.Add(words);
                if(n>0){var line=new Border{Height=1,Margin=new(46,-8,-10,0),VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false};line.SetResourceReference(Border.BackgroundProperty,"Line");content=new Grid{Children={row,line}};}else content=row;
            }
            files.Items.Add(new ListBoxItem{Content=content,Tag=path,ToolTip=path,Padding=icons?new(6,8,6,8):new(10,7,10,7),HorizontalContentAlignment=icons?HorizontalAlignment.Center:HorizontalAlignment.Stretch,IsSelected=selected.Contains(path)});
        }
        DrawStack();UpdateLabels();if(IsExpanded)SetExpanded(true,true);
    }
    // The last three files, fanned like prints (the design's stack: 50 px, −8°, −1°, 7°).
    private void DrawStack()
    {
        stack.Children.Clear();var top=paths.TakeLast(3).ToArray();var fan=new[]{(0d,8d,-8d),(10d,4d,-1d),(20d,8d,7d)};var first=3-top.Length;
        for(var index=0;index<top.Length;index++)
        {
            var (x,y,angle)=fan[first+index];var image=FloatArtwork.Image(top[index],50);image.RenderTransformOrigin=new(.5,.5);image.RenderTransform=new RotateTransform(angle);image.IsHitTestVisible=false;
            image.Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=14,ShadowDepth=5,Direction=270,Opacity=.24};Canvas.SetLeft(image,x);Canvas.SetTop(image,y);stack.Children.Add(image);
        }
        if(top.Length==0){var add=Icons.Make("plus",34,"Muted");add.IsHitTestVisible=false;Canvas.SetLeft(add,18);Canvas.SetTop(add,18);stack.Children.Add(add);}
    }
    private void UpdateLabels(){badge.Text=count.Text=paths.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);stack.ToolTip=L10n.Text(IsExpanded?"Collapse Float":"Expand Float");status.Text=L10n.Text(files.SelectedItems.Count>0?$"{files.SelectedItems.Count} selected":"Space to preview · drag out to share");Title=$"Float · {paths.Count} · Nashi";}
    private void Present()
    {
        glass.Radius=IsExpanded?24:26;if(IsExpanded)glass.Background=Brushes.Transparent;else glass.SetResourceReference(Border.BackgroundProperty,"Clear");
        frost.Visibility=IsExpanded?Visibility.Visible:Visibility.Collapsed;lens.Visibility=IsExpanded?Visibility.Collapsed:Visibility.Visible;
        root.Margin=IsExpanded?new(10):new(0);header.Visibility=IsExpanded?Visibility.Collapsed:Visibility.Visible;
        titleRow.Visibility=files.Visibility=foot.Visibility=IsExpanded?Visibility.Visible:Visibility.Collapsed;
        gridView.Drop.Visibility=icons?Visibility.Visible:Visibility.Hidden;listView.Drop.Visibility=icons?Visibility.Hidden:Visibility.Visible;
        Ui.SetOn(gridView.Button,icons);Ui.SetOn(listView.Button,!icons);UpdateLabels();
    }
    internal void SetExpanded(bool value,bool forceLayout=false)
    {
        if(IsExpanded==value&&!forceLayout)return;hover.Stop();var changed=IsExpanded!=value;
        var from=new Rect(Left+shell.Margin.Left,Top+shell.Margin.Top,shell.Width,shell.Height);IsExpanded=value;
        var work=DesktopGeometry.WorkArea(this);var shown=Math.Max(1,paths.Count);var content=icons?Math.Ceiling(Math.Min(shown,9)/3d)*104:Math.Min(shown,6)*50;
        var width=value?Open:Folded;var height=value?Math.Min(work.Height-16,content+104):FoldedHeight;
        var x=Math.Clamp(from.X+(from.Width-width)/2,work.Left,work.Right-width);var y=Math.Clamp(from.Y,work.Top,work.Bottom-height);
        if(edge=="Right")x=work.Right-width;else if(edge=="Left")x=work.Left;
        if(edge=="Bottom")y=work.Bottom-height;else if(edge=="Top")y=work.Top-(value?0:96);
        var to=new Rect(x,y,width,height);var moving=WheelPresentation.Preferences.Animations&&SystemParameters.ClientAreaAnimation&&IsVisible&&changed;
        foreach(var property in new[]{WidthProperty,HeightProperty,MarginProperty})shell.BeginAnimation(property,null);
        if(!moving){root.BeginAnimation(OpacityProperty,null);root.Opacity=1;frost.BeginAnimation(OpacityProperty,null);frost.Opacity=1;Fit(to);Present();if(value)files.Focus();return;}
        // The window holds both shapes while the glass flows from one to the other on a spring; then it fits the new one.
        var frame=Rect.Union(from,to);Fit(frame);shell.Margin=new(from.X-frame.X,from.Y-frame.Y,0,0);shell.Width=from.Width;shell.Height=from.Height;
        var time=TimeSpan.FromMilliseconds(460);var grow=new DoubleAnimation(to.Width,time){EasingFunction=Spring.Shared};
        grow.Completed+=(_,_)=>{if(IsExpanded!=value)return;foreach(var property in new[]{WidthProperty,HeightProperty,MarginProperty})shell.BeginAnimation(property,null);Fit(to);};
        shell.BeginAnimation(WidthProperty,grow);shell.BeginAnimation(HeightProperty,new DoubleAnimation(to.Height,time){EasingFunction=Spring.Shared});
        shell.BeginAnimation(MarginProperty,new ThicknessAnimation(new(to.X-frame.X,to.Y-frame.Y,0,0),time){EasingFunction=Spring.Shared});
        // The contents cross over softly: open content fades in as the panel grows; folding, it fades out first.
        if(value)
        {
            Present();root.Opacity=0;root.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(200)){BeginTime=TimeSpan.FromMilliseconds(110)});
            frost.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(220)));files.Focus();
        }
        else
        {
            var fade=new DoubleAnimation(0,TimeSpan.FromMilliseconds(100));frost.BeginAnimation(OpacityProperty,new DoubleAnimation(0,TimeSpan.FromMilliseconds(120)));
            fade.Completed+=(_,_)=>{if(IsExpanded)return;Present();root.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(180)));};
            root.BeginAnimation(OpacityProperty,fade);
        }
    }
    // The window's own rectangle, at once; the glass fills it.
    private void Fit(Rect rect)
    {
        Left=rect.X;Top=rect.Y;Width=rect.Width;Height=rect.Height;
        if(shell.HasAnimatedProperties)return;shell.Margin=new(0);shell.Width=rect.Width;shell.Height=rect.Height;
    }
    private void Options(){var menu=Menu();menu.PlacementTarget=options;menu.IsOpen=true;}
    private ContextMenu Menu()
    {
        var menu=new ContextMenu();menu.Opened+=(_,_)=>{interacting=true;hover.Stop();};menu.Closed+=(_,_)=>{interacting=false;ScheduleHover(false);};
        foreach(var (label,id) in new[]{("Open","open"),("Quick Preview","preview"),("Show in Explorer","reveal"),("Copy","copy"),("Copy Path","copyPath"),("Share…","share"),("Remove from Float","remove"),("Use in Nashi","reuse")})
        {var item=new MenuItem{Header=L10n.Text(label)};item.Click+=async(_,_)=>{try{if(id=="open")foreach(var path in Selection())MainWindow.OpenUrl(path);else if(id=="preview")Preview();else if(id=="reveal")foreach(var path in Selection())MainWindow.Reveal(path);else if(id=="remove")Remove();else if(id=="reuse")reuse(Selection());else await action(Selection(),id);}catch(Exception error)when(ActionError(error)){Report(error);}};menu.Items.Add(item);}
        menu.Items.Add(new Separator());
        foreach(var iconMode in new[]{true,false}){var item=new MenuItem{Header=L10n.Text(iconMode?"Icons":"List"),IsCheckable=true,IsChecked=icons==iconMode};item.Click+=(_,_)=>{icons=iconMode;Reload();SetExpanded(true,true);};menu.Items.Add(item);}
        var add=new MenuItem{Header=L10n.Text("Add Files…")};add.Click+=(_,_)=>Choose();menu.Items.Add(add);
        var clear=new MenuItem{Header=L10n.Text("Clear Float")};clear.Click+=(_,_)=>{paths.Clear();Reload();};menu.Items.Add(clear);
        var pin=new MenuItem{Header=L10n.Text("Attach to Edge")};foreach(var side in new[]{"Top","Right","Bottom","Left","Detach"}){var item=new MenuItem{Header=L10n.Text(side),IsCheckable=true,IsChecked=edge==(side=="Detach"?null:side)};item.Click+=(_,_)=>{edge=side=="Detach"?null:side;DesktopGeometry.Snap(this,edge);};pin.Items.Add(item);}menu.Items.Add(pin);return menu;
    }
    private void Remove(){foreach(var p in files.SelectedItems.Cast<ListBoxItem>().Select(i=>(string)i.Tag).ToArray())paths.Remove(p);Reload();}
    private void Choose(){var picker=new OpenFileDialog{Multiselect=true};if(picker.ShowDialog(this)==true)Add(picker.FileNames);}
    private static bool ActionError(Exception error)=>error is IOException or UnauthorizedAccessException or Win32Exception or System.Runtime.InteropServices.COMException;
    private void Report(Exception error)=>MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Float action unavailable"));
    private async void OnKey(object sender,KeyEventArgs e)
    {
        try{
            if(Keyboard.Modifiers==(ModifierKeys.Control|ModifierKeys.Shift)&&e.Key==Key.C){await action(Selection(),"copyPath");e.Handled=true;return;}
            if(e.Key==Key.Escape){if(files.SelectedItems.Count>0)files.UnselectAll();else SetExpanded(false);e.Handled=true;}
            else if(e.Key==Key.Delete){Remove();e.Handled=true;}
            else if(e.Key==Key.Space){Preview();e.Handled=true;}
            else if(e.Key==Key.Enter){foreach(var p in Selection())MainWindow.OpenUrl(p);e.Handled=true;}
            else if(Keyboard.Modifiers==ModifierKeys.Control){if(e.Key==Key.A){SetExpanded(true);files.SelectAll();e.Handled=true;}else if(e.Key==Key.C){await action(Selection(),"copy");e.Handled=true;}else if(e.Key==Key.V){if(Clipboard.ContainsFileDropList())Add(Clipboard.GetFileDropList().Cast<string>());e.Handled=true;}else if(e.Key==Key.R){foreach(var p in Selection())MainWindow.Reveal(p);e.Handled=true;}else if(e.Key==Key.O){foreach(var p in Selection())MainWindow.OpenUrl(p);e.Handled=true;}}
            UpdateLabels();
        }catch(Exception error)when(ActionError(error)){Report(error);}
    }
    private void Preview(){var selected=Selection();if(selected.Length>0)new FilePreviewWindow(this,selected).Show();}
}

internal static class FloatArtwork
{
    // Files as everywhere else in Nashi (real thumbnails, labeled pages); folders as a folder tile.
    internal static FrameworkElement Image(string path,double size)
    {
        if(!Directory.Exists(path))return Ui.Thumb(path,size);
        var card=new Border{Width=size,Height=size,CornerRadius=new(size/4),Background=new SolidColorBrush(Color.FromRgb(73,177,214)),BorderBrush=Theme.Brush("Line"),BorderThickness=new(1)};
        card.Child=Icons.Make("folder",size/2,"OnSolid");return card;
    }
}
