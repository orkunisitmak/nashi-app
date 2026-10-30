using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
namespace Nashi.Windows;

/// A compact file stack that opens into a narrow, temporary shelf.
public sealed class FloatShelf:Window
{
    private readonly ListBox files=new(){SelectionMode=SelectionMode.Extended,BorderThickness=new(0),Background=Brushes.Transparent,Padding=new(8,4,8,4)};
    private readonly List<string> paths=[];
    private readonly Func<IReadOnlyList<string>,string,Task> action;
    private readonly Action<IReadOnlyList<string>> reuse;
    private readonly Grid root=new(),header=new(){Height=136};
    private readonly Border glass=new(){CornerRadius=new(24),BorderThickness=new(1)};
    private readonly Canvas stack=new(){Width=100,Height=100,Background=Brushes.Transparent,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,8,0,0)};
    private readonly Button count=new(){Height=24,Width=104,FontSize=11,Padding=new(0),Background=Brushes.Transparent,BorderThickness=new(0),HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,110,0,0)};
    private readonly Border countPlate=new(){Width=104,Height=24,CornerRadius=new(11),VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,110,0,0),IsHitTestVisible=false};
    private readonly TextBlock status=new(){FontSize=9,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    private readonly Button close,options;
    private readonly DispatcherTimer hover=new();
    private bool icons=true,interacting,dragging,hoverInside;
    private Point mouseOrigin;
    private string? edge;
    internal bool IsExpanded{get;private set;}
    internal IReadOnlyList<string> Paths=>paths;

    public FloatShelf(IEnumerable<string> initial,Func<IReadOnlyList<string>,string,Task> action,Action<IReadOnlyList<string>> reuse)
    {
        this.action=action;this.reuse=reuse;Theme.Attach(this);Title="Float · Nashi";
        Width=112;Height=136;MinWidth=112;MinHeight=136;Topmost=true;ShowInTaskbar=false;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.NoResize;AllowDrop=true;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        root.RowDefinitions.Add(new(){Height=new(136)});root.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=new(0)});
        Content=new Grid{Children={glass,root}};root.Children.Add(header);header.Children.Add(stack);header.Children.Add(countPlate);header.Children.Add(count);
        close=Control("×","Close Float without deleting files",Close);close.HorizontalAlignment=HorizontalAlignment.Left;close.Margin=new(6,6,0,0);header.Children.Add(close);
        options=Control("···","Float options",Options);options.HorizontalAlignment=HorizontalAlignment.Right;options.Margin=new(0,6,6,0);header.Children.Add(options);
        Grid.SetRow(files,1);root.Children.Add(files);Grid.SetRow(status,2);root.Children.Add(status);
        count.Click+=(_,_)=>SetExpanded(!IsExpanded);
        header.MouseLeftButtonDown+=(_,e)=>{if(e.OriginalSource==header){MoveShelf();e.Handled=true;}};
        stack.MouseLeftButtonDown+=(_,e)=>{mouseOrigin=e.GetPosition(stack);dragging=false;interacting=true;hover.Stop();stack.CaptureMouse();e.Handled=true;};
        stack.MouseMove+=(_,e)=>
        {
            if(!stack.IsMouseCaptured||e.LeftButton!=MouseButtonState.Pressed||(e.GetPosition(stack)-mouseOrigin).Length<4)return;
            dragging=true;stack.ReleaseMouseCapture();
            // The wire clip is the move grip. The artwork carries the actual files.
            if(mouseOrigin.X>62&&mouseOrigin.Y<60)MoveShelf();else Carry(stack,paths.ToArray());e.Handled=true;
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
    private Button Control(string text,string help,Action click)
    {
        var b=new Button{Content=text,ToolTip=L10n.Text(help),Width=24,Height=24,Padding=new(0),FontSize=16,VerticalAlignment=VerticalAlignment.Top,HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center,Background=Brushes.Transparent,BorderThickness=new(0)};
        System.Windows.Automation.AutomationProperties.SetName(b,L10n.Text(help));b.Click+=(_,_)=>click();return b;
    }
    private void ScheduleHover(bool inside){hover.Stop();hoverInside=inside;hover.Interval=TimeSpan.FromMilliseconds(inside?200:650);hover.Start();}
    private void MoveShelf(){interacting=true;hover.Stop();try{DragMove();edge=DesktopGeometry.Snap(this,null,true);}finally{interacting=false;ScheduleHover(false);}}
    private void Carry(DependencyObject source,string[] selected)
    {
        if(selected.Length==0){interacting=false;return;}interacting=true;hover.Stop();
        try{DragDrop.DoDragDrop(source,new DataObject(DataFormats.FileDrop,selected),DragDropEffects.Copy|DragDropEffects.Move);}finally{interacting=false;Reload();ScheduleHover(false);}
    }
    internal void Add(IEnumerable<string> incoming)
    {
        foreach(var p in incoming.Where(p=>File.Exists(p)||Directory.Exists(p)).Take(1000)){var full=Path.GetFullPath(p);if(!paths.Contains(full,StringComparer.OrdinalIgnoreCase))paths.Add(full);}Reload();
    }
    private string[] Selection()=>files.SelectedItems.Count>0?files.SelectedItems.Cast<ListBoxItem>().Select(i=>(string)i.Tag).ToArray():paths.Where(p=>File.Exists(p)||Directory.Exists(p)).ToArray();
    private void Reload()
    {
        var selected=files.SelectedItems.Cast<ListBoxItem>().Select(i=>(string)i.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);files.Items.Clear();
        foreach(var path in paths)
        {
            var column=new StackPanel{Orientation=icons?Orientation.Vertical:Orientation.Horizontal};column.Children.Add(FloatArtwork.Image(path,icons?60:32));
            column.Children.Add(new TextBlock{Text=Path.GetFileName(path),TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,TextAlignment=icons?TextAlignment.Center:TextAlignment.Left,FontSize=11,MaxHeight=32,Width=icons?112:204,Margin=icons?new(0,6,0,0):new(8,0,0,0),VerticalAlignment=VerticalAlignment.Center});
            files.Items.Add(new ListBoxItem{Content=column,Tag=path,ToolTip=path,Padding=new(4),Margin=new(0,2,0,2),HorizontalContentAlignment=HorizontalAlignment.Center,IsSelected=selected.Contains(path)});
        }
        DrawStack();UpdateLabels();if(IsExpanded)SetExpanded(true,true);
    }
    private void DrawStack()
    {
        stack.Children.Clear();var top=paths.TakeLast(3).ToArray();
        for(var index=0;index<top.Length;index++)
        {
            var offset=top.Length-index-1;var image=FloatArtwork.Image(top[index],72);image.RenderTransformOrigin=new(.5,.5);image.RenderTransform=new RotateTransform(offset*-7);image.IsHitTestVisible=false;Canvas.SetLeft(image,14-offset*2);Canvas.SetTop(image,17+offset*2);stack.Children.Add(image);
        }
        if(top.Length==0)stack.Children.Add(new TextBlock{Text="＋",FontSize=44,Width=100,TextAlignment=TextAlignment.Center,Margin=new(0,20,0,0),IsHitTestVisible=false});
        const string wire="M76,71 L57,28 C47,6 70,-1 78,19 L94,57 C102,74 87,78 81,63 L66,28";
        foreach(var (color,width) in new[]{("#55000000",4.5),("#C2CAC5",2.6),("#CCFFFFFF",.7)})stack.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse(wire),Stroke=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),StrokeThickness=width,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,IsHitTestVisible=false});
    }
    private void UpdateLabels(){count.Content=(IsExpanded?"⌃  ":"⌄  ")+L10n.Text(paths.Count==1?"1 item":$"{paths.Count} items");count.ToolTip=L10n.Text(IsExpanded?"Collapse Float":"Expand Float");status.Text=L10n.Text(files.SelectedItems.Count>0?$"{files.SelectedItems.Count} selected":"Space · Preview");Title=$"Float · {paths.Count} · Nashi";}
    private void Present()
    {
        glass.Visibility=IsExpanded?Visibility.Visible:Visibility.Collapsed;glass.Background=new LinearGradientBrush(Theme.Dark?Color.FromArgb(248,42,49,34):Color.FromArgb(248,250,231,185),Theme.Dark?Color.FromArgb(248,26,39,30):Color.FromArgb(248,227,235,211),90);glass.SetResourceReference(Border.BorderBrushProperty,"Line");
        countPlate.Background=IsExpanded?Brushes.Transparent:Theme.Brush("Surface");
        root.RowDefinitions[0].Height=new(IsExpanded?140:136);header.Height=IsExpanded?140:136;root.RowDefinitions[2].Height=new(IsExpanded?22:0);
        files.Visibility=status.Visibility=close.Visibility=options.Visibility=IsExpanded?Visibility.Visible:Visibility.Collapsed;UpdateLabels();
    }
    internal void SetExpanded(bool value,bool forceLayout=false)
    {
        if(IsExpanded==value&&!forceLayout)return;hover.Stop();var oldWidth=ActualWidth>0?ActualWidth:Width;var oldHeight=ActualHeight>0?ActualHeight:Height;
        IsExpanded=value;Present();var work=DesktopGeometry.WorkArea(this);var width=value?(icons?156:284):112;var height=value?Math.Min(work.Height-16,162+Math.Max(1,Math.Min(paths.Count,5))*(icons?112:52)):136;
        var x=Math.Clamp(Left+(oldWidth-width)/2,work.Left,work.Right-width);var y=Math.Clamp(Top,work.Top,work.Bottom-height);
        if(edge=="Right")x=work.Right-width;else if(edge=="Left")x=work.Left;
        if(edge=="Bottom")y=work.Bottom-height;else if(edge=="Top")y=work.Top-(value?0:96);
        Animate(WidthProperty,width);Animate(HeightProperty,height);Animate(LeftProperty,x);Animate(TopProperty,y);if(value)files.Focus();
    }
    private void Animate(DependencyProperty property,double value)
    {
        var previous=(double)GetValue(property);BeginAnimation(property,null);SetValue(property,value);
        if(WheelPresentation.Preferences.Animations&&SystemParameters.ClientAreaAnimation&&double.IsFinite(previous)&&IsVisible)BeginAnimation(property,new DoubleAnimation(previous,value,TimeSpan.FromMilliseconds(180)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop});
    }
    private void Options(){var menu=Menu();menu.PlacementTarget=options;menu.IsOpen=true;}
    private ContextMenu Menu()
    {
        var menu=new ContextMenu();menu.Opened+=(_,_)=>{interacting=true;hover.Stop();};menu.Closed+=(_,_)=>{interacting=false;ScheduleHover(false);};
        foreach(var (label,id) in new[]{("Open","open"),("Quick Preview","preview"),("Show in Explorer","reveal"),("Copy","copy"),("Copy Path","copyPath"),("Share…","share"),("Remove from Float","remove"),("Use in Nashi","reuse")})
        {var item=new MenuItem{Header=L10n.Text(label)};item.Click+=async(_,_)=>{try{if(id=="open")foreach(var path in Selection())MainWindow.OpenUrl(path);else if(id=="preview")Preview();else if(id=="reveal")foreach(var path in Selection())MainWindow.Reveal(path);else if(id=="remove")Remove();else if(id=="reuse")reuse(Selection());else await action(Selection(),id);}catch(Exception error)when(ActionError(error)){Report(error);}};menu.Items.Add(item);}
        menu.Items.Add(new Separator());
        foreach(var iconMode in new[]{true,false}){var item=new MenuItem{Header=L10n.Text(iconMode?"Icons":"List"),IsCheckable=true,IsChecked=icons==iconMode};item.Click+=(_,_)=>{icons=iconMode;Reload();SetExpanded(false);SetExpanded(true);};menu.Items.Add(item);}
        var add=new MenuItem{Header=L10n.Text("Add Files…")};add.Click+=(_,_)=>Choose();menu.Items.Add(add);
        var clear=new MenuItem{Header=L10n.Text("Clear Float")};clear.Click+=(_,_)=>{paths.Clear();Reload();};menu.Items.Add(clear);
        var pin=new MenuItem{Header=L10n.Text("Attach to Edge")};foreach(var side in new[]{"Top","Right","Bottom","Left","Detach"}){var item=new MenuItem{Header=L10n.Text(side),IsCheckable=true,IsChecked=edge==(side=="Detach"?null:side)};item.Click+=(_,_)=>{edge=side=="Detach"?null:side;DesktopGeometry.Snap(this,edge);};pin.Items.Add(item);}menu.Items.Add(pin);MainWindow.ThemePopupMenu(menu);return menu;
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
    internal static FrameworkElement Image(string path,int size)
    {
        if(File.Exists(path)&&new[]{".png",".jpg",".jpeg",".bmp",".gif",".webp"}.Contains(Path.GetExtension(path).ToLowerInvariant()))
        {
            try{var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=128;bitmap.UriSource=new Uri(path);bitmap.EndInit();bitmap.Freeze();return new Image{Source=bitmap,Width=size,Height=size,Stretch=Stretch.Uniform};}catch(Exception error)when(error is IOException or NotSupportedException or System.IO.FileFormatException){}
        }
        var folder=Directory.Exists(path);var card=new Border{Width=size,Height=size,CornerRadius=new(folder?5:8),Background=folder?new SolidColorBrush(Color.FromRgb(73,177,214)):Theme.Brush("Sand"),BorderBrush=Theme.Brush("Line"),BorderThickness=new(1)};
        card.Child=new TextBlock{Text=folder?"▰":Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),FontSize=Math.Max(9,size/6d),FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(3)};return card;
    }
}
