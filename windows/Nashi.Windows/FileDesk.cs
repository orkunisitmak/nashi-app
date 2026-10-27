using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;

/// The in-app desk from the web playground: every selected file is a card that
/// can be moved around. Dragging a card and pressing a configured modifier opens
/// the wheel at the pointer; releasing over a slice runs it on the dragged
/// selection. With cards selected, tapping the modifier alone opens the same
/// wheel at the card. Dragging a card past the desk edge hands the files to
/// Explorer. Enter or a double-click opens the tools wheel without dragging.
internal sealed class FileDesk:Border
{
    private const double CardWidth=168,CardHeight=204,Gap=16,MinimumArea=440;
    private readonly ObservableCollection<string> files;
    private readonly PreferencesStore preferences;
    private readonly Func<string,IReadOnlyList<string>,IReadOnlyList<ToolChoice>> choices;
    private readonly Action<ToolChoice,IReadOnlyList<string>> chosen;
    private readonly Func<bool> busy;
    private readonly Func<string,string?> duplicate;
    private readonly Canvas canvas=new(){ClipToBounds=true,Background=Brushes.Transparent};
    private readonly Grid area=new(),chrome=new();
    private readonly Dictionary<string,Border> cards=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,Point> moved=new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> selected=new(StringComparer.OrdinalIgnoreCase);
    private Border? pressed;private Point pressPoint,lastScreen;private bool dragging,toggleOnRelease,addedOnDown;private Dictionary<string,Point> origins=[];
    private WheelWindow? wheel;private string? wheelMode;private IReadOnlyList<string> wheelPaths=[];
    private readonly HashSet<string> tapHeld=[],tapChord=[];private bool tapSpoiled;private DateTime tapStart;
    private Point? pendingSpot;private int pendingCount;private UIElement? empty;
    internal bool Dragging=>pressed is not null&&dragging;
    internal IReadOnlyList<string> Selection=>files.Where(selected.Contains).ToArray();
    internal event Action? SelectionChanged;
    /// Cards were dragged to a new spot (the space remembers where they sit).
    internal event Action? Moved;
    private readonly HashSet<string> missing=new(StringComparer.OrdinalIgnoreCase);
    /// Where every card sits, by path, as x,y: dragged cards where they were dropped, the rest
    /// where the desk last laid them out. A space restores all of them exactly.
    internal IReadOnlyDictionary<string,double[]> Spots=>files.Distinct(StringComparer.OrdinalIgnoreCase).Select(p=>(p,spot:moved.TryGetValue(p,out var m)?m:placed.TryGetValue(p,out var l)?l:(Point?)null)).Where(x=>x.spot is not null).ToDictionary(x=>x.p,x=>new[]{Math.Round(x.spot!.Value.X,1),Math.Round(x.spot!.Value.Y,1)},StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,Point> placed=new(StringComparer.OrdinalIgnoreCase);
    /// A file moved elsewhere: its new path keeps the old card's spot.
    internal void Carry(string from,string to)
    {
        if(moved.TryGetValue(from,out var spot))moved[to]=spot;else if(cards.TryGetValue(from,out var card))moved[to]=Position(card);
    }
    /// Puts every card back where a space left it.
    internal void Restore(IReadOnlyDictionary<string,double[]>? spots)
    {
        moved.Clear();if(spots is not null)foreach(var (path,spot) in spots)if(spot is {Length:2})moved[path]=new(spot[0],spot[1]);Arrange(false);
    }
    /// Re-checks which files still exist; a card whose file was deleted (or came back) is redrawn
    /// in place. Returns how many cards point at a deleted file.
    internal int CheckMissing()
    {
        foreach(var path in files.ToArray())
        {
            if(!cards.TryGetValue(path,out var old))continue;var gone=!System.IO.File.Exists(path)&&!System.IO.Directory.Exists(path);if(gone==missing.Contains(path))continue;
            var spot=Position(old);var index=canvas.Children.IndexOf(old);canvas.Children.Remove(old);var card=Card(path);cards[path]=card;canvas.Children.Insert(Math.Max(0,index),card);Canvas.SetLeft(card,spot.X);Canvas.SetTop(card,spot.Y);
        }
        Paint();return files.Count(missing.Contains);
    }
    /// Opens a file in the previewer (plays video and audio).
    internal Action<string,bool>? Preview{get;set;}
    /// Page commands offered by the right-click menu on an empty spot.
    internal Action? AddFiles{get;set;}
    internal Action? Paste{get;set;}
    internal Action? Clear{get;set;}
    private Point marqueeStart;private System.Windows.Shapes.Rectangle? marquee;private HashSet<string> marqueeBase=[];
    internal FileDesk(ObservableCollection<string> files,PreferencesStore preferences,Func<string,IReadOnlyList<string>,IReadOnlyList<ToolChoice>> choices,Action<ToolChoice,IReadOnlyList<string>> chosen,Func<bool> busy,Func<string,string?> duplicate)
    {
        this.files=files;this.preferences=preferences;this.choices=choices;this.chosen=chosen;this.busy=busy;this.duplicate=duplicate;
        CornerRadius=new(16);BorderThickness=new(1);Margin=new(0,18,0,0);ClipToBounds=true;Child=chrome;
        SetResourceReference(BackgroundProperty,"Desk");SetResourceReference(BorderBrushProperty,"Line");
        chrome.RowDefinitions.Add(new(){Height=GridLength.Auto});chrome.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        // A faint dot grid says "place things anywhere" without drawing boxes; it reads in both themes.
        var dot=new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x3A,0x80,0x88,0x92)),null,new EllipseGeometry(new Point(1,1),1,1));dot.Freeze();
        area.Background=new DrawingBrush(dot){TileMode=TileMode.Tile,Viewport=new(0,0,20,20),ViewportUnits=BrushMappingMode.Absolute,Viewbox=new(0,0,20,20),ViewboxUnits=BrushMappingMode.Absolute,Stretch=Stretch.None};
        area.Height=MinimumArea;area.Children.Add(canvas);Grid.SetRow(area,1);chrome.Children.Add(area);
        AutomationProperties.SetAutomationId(this,"FileDesk");AutomationProperties.SetName(this,"Your files");
        files.CollectionChanged+=Changed;canvas.SizeChanged+=(_,_)=>Arrange(false);
        // Press on an empty spot and drag: a selection rectangle, like Explorer (Ctrl adds to the selection).
        canvas.MouseLeftButtonDown+=(_,e)=>
        {
            if(e.Source!=canvas||busy())return;marqueeStart=e.GetPosition(canvas);marqueeBase=(Keyboard.Modifiers&ModifierKeys.Control)!=0?new(selected,StringComparer.OrdinalIgnoreCase):new(StringComparer.OrdinalIgnoreCase);
            if(marqueeBase.Count==0&&selected.Count>0){selected.Clear();Paint();}
            marquee=new System.Windows.Shapes.Rectangle{StrokeThickness=1,RadiusX=4,RadiusY=4,IsHitTestVisible=false,Visibility=Visibility.Collapsed};marquee.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"Gold");marquee.Fill=new SolidColorBrush(Color.FromArgb(0x26,0xC8,0xE6,0x4C));
            canvas.Children.Add(marquee);canvas.CaptureMouse();Focus();e.Handled=true;
        };
        canvas.MouseMove+=(_,e)=>
        {
            if(marquee is null||e.LeftButton!=MouseButtonState.Pressed)return;var point=e.GetPosition(canvas);var box=new Rect(marqueeStart,point);
            if(box.Width+box.Height<6)return;marquee.Visibility=Visibility.Visible;Canvas.SetLeft(marquee,box.X);Canvas.SetTop(marquee,box.Y);marquee.Width=box.Width;marquee.Height=box.Height;
            var inside=cards.Where(pair=>box.IntersectsWith(new Rect(Position(pair.Value),new Size(CardWidth,CardHeight)))).Select(pair=>pair.Key);
            var next=new HashSet<string>(marqueeBase,StringComparer.OrdinalIgnoreCase);next.UnionWith(inside);if(!next.SetEquals(selected)){selected.Clear();selected.UnionWith(next);Paint();}
        };
        canvas.MouseLeftButtonUp+=(_,_)=>EndMarquee();canvas.LostMouseCapture+=(_,_)=>EndMarquee();
        // Right-click: a Windows-style menu for the selection, or desk commands on an empty spot.
        canvas.MouseRightButtonUp+=(_,e)=>
        {
            if(busy())return;var path=(e.OriginalSource as DependencyObject) is {} hit?CardPath(hit):null;
            if(path is not null&&!selected.Contains(path)){selected.Clear();selected.Add(path);Paint();}
            if(path is null&&selected.Count>0){selected.Clear();Paint();}
            var menu=Menu();menu.PlacementTarget=this;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.MousePoint;menu.IsOpen=true;e.Handled=true;
        };
    }
    private void EndMarquee(){if(marquee is null)return;canvas.Children.Remove(marquee);marquee=null;if(canvas.IsMouseCaptured)canvas.ReleaseMouseCapture();}
    private string? CardPath(DependencyObject hit){for(var node=hit;node is not null&&node!=canvas;node=VisualTreeHelper.GetParent(node))if(node is Border{Tag:string path}&&cards.ContainsKey(path))return path;return null;}
    internal void SelectAll(){selected.Clear();selected.UnionWith(files);Paint();}
    private ContextMenu Menu()
    {
        var menu=new ContextMenu();var picked=Selection;var p=preferences.Value;
        MenuItem Item(string header,string? icon,Action action,string? gesture=null,bool enabled=true)
        {
            var item=new MenuItem{Header=L10n.Text(header),InputGestureText=gesture??"",IsEnabled=enabled,Icon=icon is null?null:Icons.Create(icon,14)};item.Click+=(_,_)=>action();AutomationProperties.SetName(item,header);return item;
        }
        if(picked.Count==0)
        {
            if(AddFiles is not null)menu.Items.Add(Item("Add files…","plus",AddFiles,"Ctrl+O"));
            if(Paste is not null)menu.Items.Add(Item("Paste","clipboard-text",Paste,"Ctrl+V"));
            menu.Items.Add(Item("Select all","selection-all",SelectAll,"Ctrl+A",files.Count>0));
            if(Clear is not null){menu.Items.Add(new Separator());menu.Items.Add(Item("Clear desk","broom",Clear,null,files.Count>0));}
            return menu;
        }
        var first=picked[0];
        menu.Items.Add(Item(picked.Count==1?"Preview":"Preview selected","eye",()=>Preview?.Invoke(first,false)));
        menu.Items.Add(new Separator());
        // The same three wheels as the keyboard shortcuts, for the selected cards.
        foreach(var (mode,title,icon,gesture) in new[]{("convert","Convert…","arrows-left-right",p.ConvertModifier),("tools","Tools…","sliders-horizontal",p.ToolsModifier),("actions","File actions…","share-network",p.ActionsModifier)})
            menu.Items.Add(Item(title,icon,()=>OpenWheel(null,mode,picked,true),gesture,choices(mode,picked).Count>0||picked.Count>1));
        if(picked.Count>1)
        {
            var each=new MenuItem{Header=L10n.Text("One by one"),Icon=Icons.Create("list-checks",14)};AutomationProperties.SetName(each,"One by one");
            foreach(var (mode,title) in new[]{("convert","Convert each…"),("tools","Tools for each…"),("actions","File actions for each…")})each.Items.Add(Item(title,null,()=>OneByOne(mode,picked,0)));
            menu.Items.Add(each);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Duplicate","copy-simple",()=>Duplicate(first),"Ctrl+D",picked.Count==1&&File.Exists(first)));
        menu.Items.Add(Item("Show in Explorer","folder-open",()=>MainWindow.Reveal(first),null,File.Exists(first)||Directory.Exists(first)));
        menu.Items.Add(Item(picked.Count==1?"Copy path":"Copy paths","path",()=>{try{Clipboard.SetText(string.Join(Environment.NewLine,picked));}catch(System.Runtime.InteropServices.COMException){}}));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Remove from desk","x",()=>Remove(picked),"Delete"));
        return menu;
    }
    /// For a selection whose files share no tool (or on request): one wheel per file, in turn.
    /// Escape on any wheel stops the round.
    private void OneByOne(string mode,IReadOnlyList<string> paths,int index)
    {
        while(index<paths.Count&&(!files.Contains(paths[index])||choices(mode,[paths[index]]).Count==0))index++;
        if(index>=paths.Count||busy())return;var path=paths[index];selected.Clear();selected.Add(path);Paint();
        var picked=false;var round=new WheelWindow(Window.GetWindow(this),choices(mode,[path]),c=>{picked=true;chosen(c,[path]);});round.Show();
        if(cards.TryGetValue(path,out var anchor)&&anchor.IsVisible){var center=anchor.PointToScreen(new(CardWidth/2,CardHeight/2));DesktopGeometry.PlaceWheel(round,(int)center.X,(int)center.Y,WheelPresentation.Diameter*preferences.Value.WheelScale);}
        round.Closed+=(_,_)=>
        {
            if(!picked)return;
            // Wait for the chosen tool's window to close before the next wheel appears.
            var owner=Window.GetWindow(this);var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(150)};
            timer.Tick+=(_,_)=>{if(owner is null||!owner.IsVisible){timer.Stop();return;}if(!IsWindowEnabled(new System.Windows.Interop.WindowInteropHelper(owner).Handle)||Application.Current.Windows.OfType<Window>().Any(w=>w!=owner&&w.IsActive))return;timer.Stop();OneByOne(mode,paths,index+1);};timer.Start();
        };
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern bool IsWindowEnabled(IntPtr window);
    /// The page supplies the toolbar above the cards and the empty-desk invitation.
    internal void SetChrome(UIElement toolbar,UIElement invitation)
    {
        chrome.Children.Add(toolbar);empty=invitation;area.Children.Add(invitation);UpdateEmpty();
    }
    private void UpdateEmpty(){if(empty is not null)empty.Visibility=files.Count==0?Visibility.Visible:Visibility.Collapsed;}
    /// The next cards to arrive land at this desk point (a drop, or beside a duplicate).
    internal void ExpectCardsAt(Point point){pendingSpot=point;pendingCount=0;Dispatcher.BeginInvoke(()=>pendingSpot=null,System.Windows.Threading.DispatcherPriority.Background);}
    internal void ExpectBeside(string source){if(cards.TryGetValue(source,out var card))ExpectCardsAt(Position(card)+new Vector(CardWidth+Gap,0));}
    /// The files being worked on give a little shake.
    internal void Shake(IEnumerable<string> paths){foreach(var path in paths.Take(12))if(cards.TryGetValue(path,out var card)&&card.Child is UIElement body)Motion.Shake(body);}
    /// Fresh cards slide out of the card they came from and pop into place.
    internal void Arrive(IEnumerable<string> paths,string? source)
    {
        if(!Motion.On)return;Point? from=source is not null&&cards.TryGetValue(source,out var origin)?Position(origin):null;
        foreach(var path in paths)
        {
            if(!cards.TryGetValue(path,out var card)||path==source)continue;var target=Position(card);var time=Motion.Time(380);var ease=new BackEase{Amplitude=.35,EasingMode=EasingMode.EaseOut};
            if(from is {} start){card.BeginAnimation(Canvas.LeftProperty,new DoubleAnimation(start.X,target.X,time){EasingFunction=ease,FillBehavior=FillBehavior.Stop});card.BeginAnimation(Canvas.TopProperty,new DoubleAnimation(start.Y,target.Y,time){EasingFunction=ease,FillBehavior=FillBehavior.Stop});}
            var scale=(ScaleTransform)card.RenderTransform;scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(.55,1,time){EasingFunction=ease,FillBehavior=FillBehavior.Stop});scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(.55,1,time){EasingFunction=ease,FillBehavior=FillBehavior.Stop});
            card.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,Motion.Time(200)){FillBehavior=FillBehavior.Stop});
        }
    }
    internal Point DeskPoint(DragEventArgs e)=>e.GetPosition(canvas)-new Vector(CardWidth/2,CardHeight/3);
    internal void Select(IEnumerable<string> paths){selected.Clear();foreach(var path in paths)selected.Add(path);Paint();}
    internal bool Contains(DragEventArgs e){var p=e.GetPosition(canvas);return p.X>=0&&p.Y>=0&&p.X<=canvas.ActualWidth&&p.Y<=canvas.ActualHeight;}
    private void Changed(object? sender,NotifyCollectionChangedEventArgs e)
    {
        foreach(var path in cards.Keys.Where(p=>!files.Contains(p)).ToList()){canvas.Children.Remove(cards[path]);cards.Remove(path);moved.Remove(path);placed.Remove(path);selected.Remove(path);missing.Remove(path);}
        foreach(var path in files.Where(p=>!cards.ContainsKey(p)))
        {
            var card=Card(path);cards[path]=card;canvas.Children.Add(card);
            if(pendingSpot is {} spot){moved[path]=spot+new Vector(pendingCount*26,pendingCount*20);pendingCount++;}
        }
        UpdateEmpty();Arrange(false);Paint();
    }
    /// Unmoved cards fill rows from the left (the desk grows to four rows; later
    /// ones fan out on the last row). Moved cards keep their spot, clamped to the desk.
    private void Arrange(bool animate)
    {
        var width=Math.Max(canvas.ActualWidth,CardWidth+2*Gap);var columns=Math.Max(1,(int)((width-Gap)/(CardWidth+Gap)));
        var rows=Math.Clamp((int)Math.Ceiling(files.Count(p=>!moved.ContainsKey(p))/(double)columns),1,4);var height=Math.Max(MinimumArea,Gap+rows*(CardHeight+Gap));if(Math.Abs(area.Height-height)>.5)area.Height=height;var slot=0;
        foreach(var path in files)
        {
            if(!cards.TryGetValue(path,out var card))continue;Point target;
            if(moved.TryGetValue(path,out var spot))target=Clamp(spot);
            else
            {
                // Skip grid slots a moved card already covers, so arrivals never land on top of it.
                Point Slot(int n){var row=Math.Min(n/columns,rows-1);var extra=n-row*columns;var fan=extra/columns;return Clamp(new(Gap+extra%columns*(CardWidth+Gap)+fan*10,Gap+row*(CardHeight+Gap)+fan*6));}
                bool Taken(Point p)=>moved.Any(m=>files.Contains(m.Key)&&Math.Abs(m.Value.X-p.X)<CardWidth*.6&&Math.Abs(m.Value.Y-p.Y)<CardHeight*.6);
                while(slot<columns*rows-1&&Taken(Slot(slot)))slot++;target=Slot(slot);slot++;
            }
            Place(card,target,animate);if(canvas.ActualWidth>0)placed[path]=target;
        }
    }
    private Point Clamp(Point p)=>new(Math.Clamp(p.X,0,Math.Max(0,canvas.ActualWidth-CardWidth)),Math.Clamp(p.Y,0,Math.Max(0,canvas.ActualHeight-CardHeight)));
    private static void Place(Border card,Point target,bool animate)
    {
        if(animate&&Motion.On)
        {
            var ease=new CubicEase{EasingMode=EasingMode.EaseOut};var time=Motion.Time(180);
            card.BeginAnimation(Canvas.LeftProperty,new DoubleAnimation(target.X,time){EasingFunction=ease,FillBehavior=FillBehavior.Stop});card.BeginAnimation(Canvas.TopProperty,new DoubleAnimation(target.Y,time){EasingFunction=ease,FillBehavior=FillBehavior.Stop});
        }
        else{card.BeginAnimation(Canvas.LeftProperty,null);card.BeginAnimation(Canvas.TopProperty,null);}
        Canvas.SetLeft(card,target.X);Canvas.SetTop(card,target.Y);
    }
    private static Point Position(UIElement card)=>new(Canvas.GetLeft(card),Canvas.GetTop(card));
    private Border Card(string path)
    {
        var extension=System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant();var name=System.IO.Path.GetFileName(path);
        var art=new Grid{Height=130,ClipToBounds=true};var artFrame=new Border{CornerRadius=new(10),Child=art,Margin=new(0,0,0,10),ClipToBounds=true};artFrame.SetResourceReference(BackgroundProperty,"Sand");
        var badge=new TextBlock{Text=extension.Length==0?"•":extension,FontSize=20,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};badge.SetResourceReference(TextBlock.ForegroundProperty,"Muted");art.Children.Add(badge);
        var icon=new Image{Width=20,Height=20,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom,Margin=new(9)};FileArtwork.Bind(icon,path);art.Children.Add(icon);
        if(System.IO.File.Exists(path))_=LoadThumbnailAsync(path,art,badge,icon);
        if(FeatureCatalog.IsVideo(path)||FeatureCatalog.IsAudio(path))
        {
            // A round play button over the cover; it opens the previewer, which starts playing.
            var play=new Button{Style=(Style)Application.Current.FindResource("IconButton"),Width=42,Height=42,Content=Icons.Create("play-fill",16),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Focusable=false};L10n.Bind(play,ToolTipProperty,"Play");AutomationProperties.SetName(play,"Play");
            play.Click+=(_,e)=>{Preview?.Invoke(path,true);e.Handled=true;};art.Children.Add(play);
        }
        // A file deleted since it was put here keeps its card, clearly marked, so the space can tell you.
        var gone=!System.IO.File.Exists(path)&&!System.IO.Directory.Exists(path);if(gone)missing.Add(path);else missing.Remove(path);
        if(gone)
        {
            var note=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};var mark=Icons.Create("warning",26,"Gold");mark.HorizontalAlignment=HorizontalAlignment.Center;note.Children.Add(mark);
            var said=L10n.Label(new TextBlock{Text="This file was deleted",FontSize=11.5,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new(10,8,10,0)});said.SetResourceReference(TextBlock.ForegroundProperty,"Ink");note.Children.Add(said);
            var veil=new Border{Child=note};veil.SetResourceReference(BackgroundProperty,"Inset");art.Children.Add(veil);badge.Visibility=icon.Visibility=Visibility.Collapsed;
        }
        var label=new TextBlock{Text=name,FontSize=12,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,TextDecorations=gone?TextDecorations.Strikethrough:null};label.SetResourceReference(TextBlock.ForegroundProperty,gone?"Muted":"Ink");
        var kind=new TextBlock{Text=extension,FontSize=11,Margin=new(0,3,0,0)};kind.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        var content=new StackPanel();content.Children.Add(artFrame);content.Children.Add(label);content.Children.Add(kind);
        // Hover tools sit on the thumbnail's corner: duplicate the file, or take the card off the desk.
        var tools=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,6,6,0),Visibility=Visibility.Collapsed};
        IconButton(tools,"eye","Preview",()=>Preview?.Invoke(path,false),System.IO.File.Exists(path));IconButton(tools,"copy-simple","Duplicate",()=>Duplicate(path),System.IO.File.Exists(path));IconButton(tools,"x","Remove from desk",()=>Remove([path]),true);
        var body=new Grid();body.Children.Add(content);body.Children.Add(tools);
        var card=new Border{Width=CardWidth,Height=CardHeight,CornerRadius=new(12),Padding=new(8),BorderThickness=new(1),Child=body,Tag=path,Focusable=true,Cursor=Cursors.Hand,ToolTip=gone?L10n.Text("This file was deleted")+Environment.NewLine+path:path,RenderTransformOrigin=new(.5,.5),RenderTransform=new ScaleTransform(1,1),Opacity=gone?.8:1};
        card.SetResourceReference(BackgroundProperty,"Surface");card.SetResourceReference(BorderBrushProperty,"Line");card.FocusVisualStyle=null;
        card.Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=14,ShadowDepth=2,Direction=270,Opacity=.16,Color=Color.FromRgb(0x10,0x14,0x1A)};
        void Tools(){tools.Visibility=card.IsMouseOver||card.IsKeyboardFocusWithin?Visibility.Visible:Visibility.Collapsed;}
        card.MouseEnter+=(_,_)=>Tools();card.MouseLeave+=(_,_)=>Tools();card.IsKeyboardFocusWithinChanged+=(_,_)=>Tools();
        AutomationProperties.SetName(card,name);AutomationProperties.SetHelpText(card,"Drag, then press a wheel shortcut. Enter opens tools.");
        card.MouseLeftButtonDown+=Down;card.MouseMove+=Move;card.MouseLeftButtonUp+=Up;card.LostMouseCapture+=(_,_)=>{if(ReferenceEquals(pressed,card))Cancel();};
        card.GotKeyboardFocus+=(_,_)=>{if(!selected.Contains(path)&&pressed is null){selected.Clear();selected.Add(path);}Paint();};card.LostKeyboardFocus+=(_,_)=>Paint();
        card.PreviewKeyDown+=(_,e)=>Key(card,e);
        return card;
    }
    private static void IconButton(Panel host,string icon,string label,Action action,bool enabled)
    {
        var button=new Button{Style=(Style)Application.Current.FindResource("IconButton"),Content=Icons.Create(icon,13),Margin=new(4,0,0,0),IsEnabled=enabled,Focusable=false};L10n.Bind(button,ToolTipProperty,label);AutomationProperties.SetName(button,label);
        button.Click+=(_,e)=>{action();e.Handled=true;};host.Children.Add(button);
    }
    private void Remove(IReadOnlyList<string> paths){if(busy())return;foreach(var path in paths)files.Remove(path);SelectionChanged?.Invoke();}
    private void Duplicate(string path)
    {
        if(busy()||!cards.TryGetValue(path,out var card))return;ExpectCardsAt(Position(card)+new Vector(26,20));
        if(duplicate(path) is {} copy){selected.Clear();selected.Add(copy);if(cards.TryGetValue(copy,out var twin))twin.Focus();Paint();}
    }
    private static async Task LoadThumbnailAsync(string path,Grid art,TextBlock badge,Image icon)
    {
        if(await Thumbnails.LoadAsync(path,320) is not {} bitmap)return;
        art.Children.Insert(0,new Image{Source=bitmap,Stretch=Stretch.UniformToFill});badge.Visibility=icon.Visibility=Visibility.Collapsed;
    }
    private void Paint()
    {
        foreach(var (path,card) in cards)
        {
            var on=selected.Contains(path);card.SetResourceReference(BorderBrushProperty,on||card.IsKeyboardFocused?"Gold":"Line");card.BorderThickness=new(on?2:1);card.Padding=new(on?7:8);
        }
        SelectionChanged?.Invoke();
    }
    private void Down(object sender,MouseButtonEventArgs e)
    {
        if(busy()||sender is not Border{Tag:string path} card)return;tapSpoiled=true;
        if(e.ClickCount==2){selected.Clear();selected.Add(path);Paint();OpenWheel(null,"tools",Selection,true);e.Handled=true;return;}
        // Ctrl-click toggles a card; a plain click on an unselected card selects only it.
        toggleOnRelease=(Keyboard.Modifiers&ModifierKeys.Control)!=0;addedOnDown=!selected.Contains(path);
        if(addedOnDown){if(!toggleOnRelease)selected.Clear();selected.Add(path);}
        pressed=card;pressPoint=e.GetPosition(canvas);dragging=false;card.Focus();card.CaptureMouse();Paint();e.Handled=true;
    }
    private void Move(object sender,MouseEventArgs e)
    {
        if(pressed is null||!ReferenceEquals(sender,pressed)||e.LeftButton!=MouseButtonState.Pressed)return;
        var point=e.GetPosition(canvas);var delta=point-pressPoint;
        if(!dragging)
        {
            if(Math.Abs(delta.X)+Math.Abs(delta.Y)<6)return;
            dragging=true;toggleOnRelease=false;var path=(string)pressed.Tag;if(!selected.Contains(path)){selected.Clear();selected.Add(path);Paint();}
            origins=Selection.Where(cards.ContainsKey).ToDictionary(p=>p,p=>Position(cards[p]),StringComparer.OrdinalIgnoreCase);Lift(pressed,true);
        }
        var screen=pressed.PointToScreen(e.GetPosition(pressed));lastScreen=screen;
        if(UpdateWheel(screen))return;
        // Leaving the desk with no shortcut held hands the files to Explorer or another app.
        if(point.X<-24||point.Y<-24||point.X>canvas.ActualWidth+24||point.Y>canvas.ActualHeight+24){DragOut();return;}
        foreach(var (path,origin) in origins)Place(cards[path],Clamp(origin+delta),false);
    }
    private void Up(object sender,MouseButtonEventArgs e)
    {
        if(pressed is null||!ReferenceEquals(sender,pressed))return;var card=pressed;var path=(string)card.Tag;var wasDragging=dragging;
        ToolChoice? choice=null;IReadOnlyList<string> paths=wheelPaths;
        if(wheel is not null){var index=wheel.SelectedIndex;var list=choices(wheelMode!,paths);if(index>=0&&index<list.Count)choice=list[index];}
        var dropped=wasDragging&&wheel is null;
        Finish(restore:!dropped);
        if(dropped){foreach(var p in origins.Keys)moved[p]=Position(cards[p]);Moved?.Invoke();}
        else if(!wasDragging){if(!toggleOnRelease){selected.Clear();selected.Add(path);}else if(!addedOnDown&&selected.Count>1)selected.Remove(path);Paint();}
        if(choice is not null)chosen(choice,paths);e.Handled=true;
    }
    private void Key(Border card,KeyEventArgs e)
    {
        if(pressed is not null&&dragging)
        {
            if(e.Key==System.Windows.Input.Key.Escape)Finish(restore:true);else UpdateWheel(lastScreen);
            e.Handled=true;return;
        }
        if(busy())return;
        if(e.Key is System.Windows.Input.Key.Delete or System.Windows.Input.Key.Back&&Keyboard.Modifiers==ModifierKeys.None){var index=files.IndexOf((string)card.Tag);Remove(Selection);if(files.Count>0&&cards.TryGetValue(files[Math.Clamp(index,0,files.Count-1)],out var next))next.Focus();e.Handled=true;}
        else if(e.Key==System.Windows.Input.Key.D&&Keyboard.Modifiers==ModifierKeys.Control){Duplicate((string)card.Tag);e.Handled=true;}
        else if(e.Key==System.Windows.Input.Key.A&&Keyboard.Modifiers==ModifierKeys.Control){SelectAll();e.Handled=true;}
        else if(e.Key==System.Windows.Input.Key.Enter&&Keyboard.Modifiers==ModifierKeys.None){OpenWheel(null,"tools",Selection,true);e.Handled=true;}
        else if(e.Key==System.Windows.Input.Key.Space&&Keyboard.Modifiers==ModifierKeys.None){var path=(string)card.Tag;if(!selected.Add(path)&&selected.Count>1)selected.Remove(path);Paint();e.Handled=true;}
        else if(e.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right&&Keyboard.Modifiers==ModifierKeys.None)
        {
            var index=files.IndexOf((string)card.Tag)+(e.Key==System.Windows.Input.Key.Right?1:-1);if(index>=0&&index<files.Count&&cards.TryGetValue(files[index],out var next)){selected.Clear();selected.Add(files[index]);next.Focus();}e.Handled=true;
        }
    }
    /// Opens or swaps the wheel when the held chord changes; once open it stays
    /// until release, so tapping the shortcut is enough.
    private bool UpdateWheel(Point screen)
    {
        var mode=Mode();if(mode is not null&&mode!=wheelMode)OpenWheel(screen,mode,origins.Keys.ToArray(),false);
        if(wheel is null)return false;wheel.HoverScreen(screen);return true;
    }
    /// A lone tap of a wheel shortcut (pressed and released with nothing else in
    /// between) opens that wheel for the selected cards. Clicks and other keys
    /// spoil the tap, so Ctrl-click, Ctrl+V and friends keep working.
    internal bool Tap(KeyEventArgs e,bool down,bool allowed)
    {
        var key=e.Key==System.Windows.Input.Key.System?e.SystemKey:e.Key;
        var name=key switch{System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl=>"Ctrl",System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift=>"Shift",System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt=>"Alt",_=>null};
        if(name is null){if(down)tapSpoiled=true;return false;}
        if(down){if(e.IsRepeat)return false;if(tapHeld.Count==0){tapChord.Clear();tapSpoiled=pressed is not null;tapStart=DateTime.UtcNow;}tapHeld.Add(name);tapChord.Add(name);return false;}
        tapHeld.Remove(name);if(tapHeld.Count>0||tapSpoiled||!allowed||pressed is not null||(DateTime.UtcNow-tapStart).TotalMilliseconds>800)return false;
        var chord=string.Join('+',new[]{"Ctrl","Shift","Alt"}.Where(tapChord.Contains));var p=preferences.Value;var mode=chord==p.ToolsModifier?"tools":chord==p.ConvertModifier?"convert":chord==p.ActionsModifier?"actions":null;
        var paths=Selection;if(mode is null||paths.Count==0||busy())return false;
        if(paths.Count>1&&choices(mode,paths).Count==0){OneByOne(mode,paths,0);return true;}
        OpenWheel(null,mode,paths,true);return true;
    }
    internal void SpoilTap()=>tapSpoiled=true;
    private string? Mode()
    {
        var parts=new List<string>();var m=Keyboard.Modifiers;if(m.HasFlag(ModifierKeys.Control))parts.Add("Ctrl");if(m.HasFlag(ModifierKeys.Shift))parts.Add("Shift");if(m.HasFlag(ModifierKeys.Alt))parts.Add("Alt");
        var chord=string.Join('+',parts);var p=preferences.Value;return chord.Length==0?null:chord==p.ToolsModifier?"tools":chord==p.ConvertModifier?"convert":chord==p.ActionsModifier?"actions":null;
    }
    /// With a pointer the wheel follows the drag; from the keyboard it is an ordinary focused wheel.
    private void OpenWheel(Point? screen,string mode,IReadOnlyList<string> paths,bool keyboard)
    {
        if(paths.Count==0)return;var list=choices(mode,paths);if(list.Count==0){if(keyboard&&paths.Count>1)OneByOne(mode,paths,0);return;}
        if(keyboard)
        {
            // Opens over the first selected card, so the wheel appears where the eye already is.
            var keyboardWheel=new WheelWindow(Window.GetWindow(this),list,c=>chosen(c,paths));keyboardWheel.Show();
            if(cards.TryGetValue(paths[0],out var anchor)&&anchor.IsVisible){var center=anchor.PointToScreen(new(CardWidth/2,CardHeight/2));DesktopGeometry.PlaceWheel(keyboardWheel,(int)center.X,(int)center.Y,WheelPresentation.Diameter*preferences.Value.WheelScale);}
            return;
        }
        wheel?.Close();wheelMode=mode;wheelPaths=paths;wheel=new WheelWindow(Window.GetWindow(this),list,_=>{},externalDrag:true,dragTitle:mode switch{"convert"=>"Convert","actions"=>"File actions",_=>"Tools"});wheel.Show();
        DesktopGeometry.PlaceWheel(wheel,(int)screen!.Value.X,(int)screen.Value.Y,WheelPresentation.Diameter*preferences.Value.WheelScale);
        // The wheel takes over; cards wait at home until the pointer is released.
        foreach(var (path,origin) in origins)Place(cards[path],origin,true);
    }
    private void DragOut()
    {
        var paths=origins.Keys.ToArray();Finish(restore:true);
        try{DragDrop.DoDragDrop(this,new DataObject(DataFormats.FileDrop,paths),DragDropEffects.Copy);}
        catch(Exception error)when(error is System.Runtime.InteropServices.COMException or InvalidOperationException){}
    }
    private void Finish(bool restore)
    {
        var card=pressed;pressed=null;wheel?.Close();wheel=null;wheelMode=null;
        if(card is not null){Lift(card,false);if(card.IsMouseCaptured)card.ReleaseMouseCapture();}
        if(restore&&dragging)foreach(var (path,origin) in origins)if(cards.TryGetValue(path,out var c))Place(c,origin,true);
        dragging=false;
    }
    private void Cancel(){if(pressed is not null&&dragging)Finish(restore:true);else{pressed=null;wheel?.Close();wheel=null;wheelMode=null;}}
    private static void Lift(Border card,bool up)
    {
        Panel.SetZIndex(card,up?10:0);var scale=(ScaleTransform)card.RenderTransform;var to=up?1.04:1;
        if(Motion.On){var time=Motion.Time(120);scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(to,time));scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(to,time));}
        else{scale.ScaleX=scale.ScaleY=to;}
        card.Opacity=up?.92:1;
    }
}
