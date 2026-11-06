using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nashi.Core;
namespace Nashi.Windows;
public sealed partial class MainWindow
{
    private Guid? selectedLibraryId;private Point libraryDragStart;
    private readonly Dictionary<Guid,Border> libraryCards=[];
    private void ShowToolSetupMenu()
    {
        if(busy)return;
        toolSetupOffered=true;
        if(toolSetupWindow is not null){toolSetupWindow.Activate();return;}
        toolSetupWindow=new ToolSetupWindow(this,tools,officeRuntime,SetBusy);
        try{toolSetupWindow.ShowDialog();}finally{toolSetupWindow=null;ShowSetupState();}
    }
    private void OfferFirstRunTools()
    {
        if(toolSetupOffered || busy || tools.Ready && officeRuntime.Executable is not null)return;
        toolSetupOffered=true;ShowToolSetupMenu();
    }
    // The selected files as a small fanned stack of prints. It is still the file list: Ctrl+A selects, Delete removes.
    private void BuildFileStack()
    {
        list.ItemsSource=files;list.SelectionMode=SelectionMode.Extended;list.Width=66;list.Height=56;list.AlternationCount=3;list.VerticalAlignment=VerticalAlignment.Center;
        list.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(Grid)));var host=new FrameworkElementFactory(typeof(ItemsPresenter));list.Template=new ControlTemplate(typeof(ListBox)){VisualTree=host};
        var art=new FrameworkElementFactory(typeof(ContentControl));art.SetBinding(ContentControl.ContentProperty,new System.Windows.Data.Binding{Converter=new StackArt()});list.ItemTemplate=new DataTemplate{VisualTree=art};
        var item=new Style(typeof(ListBoxItem));item.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty,null));item.Setters.Add(new Setter(FrameworkElement.WidthProperty,44d));item.Setters.Add(new Setter(FrameworkElement.HeightProperty,44d));item.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Left));item.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Top));item.Setters.Add(new Setter(UIElement.RenderTransformOriginProperty,new Point(.5,.5)));
        var face=new FrameworkElementFactory(typeof(Border),"Face");face.SetValue(Border.CornerRadiusProperty,new CornerRadius(11));face.SetValue(Border.BorderThicknessProperty,new Thickness(0));face.SetValue(Border.BorderBrushProperty,Brushes.Transparent);face.SetValue(UIElement.EffectProperty,new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=14,ShadowDepth=6,Direction=270,Opacity=.22});face.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var template=new ControlTemplate(typeof(ListBoxItem)){VisualTree=face};var picked=new Trigger{Property=ListBoxItem.IsSelectedProperty,Value=true};picked.Setters.Add(new Setter(Border.BorderThicknessProperty,new Thickness(2),"Face"));picked.Setters.Add(new Setter(Border.BorderBrushProperty,new DynamicResourceExtension("Solid"),"Face"));template.Triggers.Add(picked);item.Setters.Add(new Setter(Control.TemplateProperty,template));
        foreach(var (index,angle,x,y) in new[]{(0,-8d,0d,8d),(1,-1d,10d,4d),(2,7d,20d,8d)}){var fan=new Trigger{Property=ItemsControl.AlternationIndexProperty,Value=index};fan.Setters.Add(new Setter(UIElement.RenderTransformProperty,new RotateTransform(angle)));fan.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(x,y,0,0)));item.Triggers.Add(fan);}
        list.ItemContainerStyle=item;
    }
    private sealed class StackArt:System.Windows.Data.IValueConverter
    {
        public object Convert(object value,Type type,object parameter,System.Globalization.CultureInfo culture)=>Ui.Thumb((string)value,44);
        public object ConvertBack(object value,Type type,object parameter,System.Globalization.CultureInfo culture)=>throw new NotSupportedException();
    }
    private bool Selected(Border card)=>card.Tag is LibraryEntry entry&&entry.Id==selectedLibraryId;
    private void ConfigureLibraryRow(Border card,LibraryEntry entry)
    {
        libraryCards[entry.Id]=card;card.Tag=entry;card.Focusable=true;card.FocusVisualStyle=null;card.ToolTip=L10n.Text("Space: preview · Enter: use in Nashi");
        Mark(card);
        card.GotKeyboardFocus+=(_,_)=>SelectCard(entry.Id);
        card.PreviewMouseLeftButtonDown+=(_,e)=>{libraryDragStart=e.GetPosition(card);SelectCard(entry.Id);if(!IsButton(e.OriginalSource as DependencyObject)){card.Focus();if(e.ClickCount==2){PreviewEntry(entry);e.Handled=true;}}};
        card.MouseMove+=(_,e)=>{if(e.LeftButton==MouseButtonState.Pressed&&(e.GetPosition(card)-libraryDragStart).Length>8&&entry.AvailablePaths.Length>0&&!IsButton(e.OriginalSource as DependencyObject)){DragDrop.DoDragDrop(card,new DataObject(DataFormats.FileDrop,entry.AvailablePaths),DragDropEffects.Copy);}};
    }
    // The selected row sits on a quiet fill with a fine edge, as in the design; focus follows the selection.
    private void Mark(Border card)
    {
        var selected=Selected(card);if(selected)card.SetResourceReference(Border.BackgroundProperty,"Sel");else card.Background=card.IsMouseOver?Theme.Brush("Hover"):Brushes.Transparent;
        if(selected)card.SetResourceReference(Border.BorderBrushProperty,"LineStrong");else card.BorderBrush=Brushes.Transparent;
        if(card.Child is DockPanel{Children.Count:>0} row)row.Children[0].Opacity=selected||card.IsMouseOver||card.IsKeyboardFocusWithin?1:0;
    }
    private static bool IsButton(DependencyObject? value){while(value is not null){if(value is Button)return true;value=value is Visual?VisualTreeHelper.GetParent(value):LogicalTreeHelper.GetParent(value);}return false;}
    private void SelectCard(Guid id){selectedLibraryId=id;foreach(var card in libraryCards.Values)Mark(card);}
    private void SelectLibrary(int direction)
    {
        // Rows still waiting for the typing pause are shown first, so the focused row is the one that stays.
        if(searchTyping.IsEnabled)RenderLibrary();
        var entries=SearchLibrary(search.Text).ToArray();if(entries.Length==0)return;var old=Array.FindIndex(entries,e=>e.Id==selectedLibraryId);var next=old<0?0:Math.Clamp(old+direction,0,entries.Length-1);SelectCard(entries[next].Id);if(libraryCards.TryGetValue(entries[next].Id,out var card)){card.Focus();card.BringIntoView();}
    }
    private LibraryEntry? SelectedLibrary()=>SearchLibrary(search.Text).FirstOrDefault(e=>e.Id==selectedLibraryId)??SearchLibrary(search.Text).FirstOrDefault();
    private void PreviewSelectedLibrary(){if(SelectedLibrary() is {} entry)PreviewEntry(entry);}
    private void PreviewEntry(LibraryEntry entry){if(entry.AvailablePaths.Length>0)new FilePreviewWindow(this,entry.AvailablePaths).Show();else L10n.SetText(status,"These files have moved or were deleted.");}
    private void ReuseSelectedLibrary(){if(busy||SelectedLibrary() is not {} entry)return;files.Clear();Add(entry.AvailablePaths);ShowLibrary(false);}
    // While something is dragged over the window, the drop card's edge turns coral and says what will happen.
    private void ShowDropTarget(int? count)
    {
        if(count is null){dropCard.SetResourceReference(Border.BorderBrushProperty,"Line");dropCard.BorderThickness=new(1);L10n.SetText(dropTitle,"Drop files here");L10n.SetText(dropSubtitle,"Images, video, audio, PDFs, documents and archives.");return;}
        dropCard.SetResourceReference(Border.BorderBrushProperty,"Coral");dropCard.BorderThickness=new(2);
        if(count>0){L10n.SetText(dropTitle,"Release to add");L10n.SetText(dropSubtitle,count==1?"1 item":$"{count} items");}
    }
    private string? SelectionSize()
    {
        long bytes=0;foreach(var path in files){try{if(File.Exists(path))bytes+=new FileInfo(path).Length;}catch(Exception error)when(error is IOException or UnauthorizedAccessException){}}
        if(bytes<=0)return null;var culture=CultureInfo.GetCultureInfo(L10n.Language=="tr"?"tr-TR":"en-US");
        return bytes<1024*1024?Math.Max(1,bytes/1024).ToString(culture)+" KB":bytes<1024L*1024*1024?(bytes/1048576d).ToString("0.#",culture)+" MB":(bytes/1073741824d).ToString("0.##",culture)+" GB";
    }
    private void BuildSelectionActions()
    {
        legend.Children.Clear();
        void Legend(string key,string label){var item=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(8,0,8,0)};item.Children.Add(Ui.Keycap(key));var text=Text(label,12,"Muted");text.Margin=new(6,0,0,0);item.Children.Add(text);legend.Children.Add(item);}
        Legend(preferences.Value.ConvertModifier,"Convert");Legend(preferences.Value.ToolsModifier,"Tools");Legend(preferences.Value.ActionsModifier,"Actions");
        var dot=new System.Windows.Shapes.Ellipse{Width=3,Height=3,Margin=new(8,0,8,0),VerticalAlignment=VerticalAlignment.Center};dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,"Faint");legend.Children.Add(dot);
        var hold=Text("Hold while dragging in File Explorer",12,"Muted");hold.Margin=new(8,0,8,0);legend.Children.Add(hold);
        if(files.Count==0)return;
        var stack=new StackPanel();actions.Children.Add(stack);var tiles=new Grid();for(var n=0;n<3;n++)tiles.ColumnDefinitions.Add(new());stack.Children.Add(tiles);
        var formats=FeatureCatalog.Formats(files);var tools=Choices();
        void Tile(int column,string title,string subtitle,string glyph,IEnumerable<(string,Action)> commands)
        {
            var label=new DockPanel();var icon=Ui.TileIcon(glyph);icon.Margin=new(0,0,11,0);DockPanel.SetDock(icon,Dock.Left);label.Children.Add(icon);var chevron=Icons.Make("chevDown",12,"Faint");chevron.Margin=new(6,0,0,0);DockPanel.SetDock(chevron,Dock.Right);label.Children.Add(chevron);
            var caption=new StackPanel{VerticalAlignment=VerticalAlignment.Center};caption.Children.Add(Text(title,13,weight:FontWeights.SemiBold));var sub=Text(subtitle,11.5,"Muted");sub.TextWrapping=TextWrapping.NoWrap;sub.TextTrimming=TextTrimming.CharacterEllipsis;caption.Children.Add(sub);label.Children.Add(caption);
            var button=Button("",()=>{});button.Style=Ui.Style("Tile");button.Content=label;button.Margin=new(column==0?0:4,0,column==2?0:4,0);var menu=new ContextMenu();foreach(var (name,action) in commands){var item=new MenuItem();L10n.Bind(item,HeaderedItemsControl.HeaderProperty,name);item.Click+=(_,_)=>action();menu.Items.Add(item);}button.IsEnabled=menu.Items.Count>0;button.Click+=(_,_)=>{menu.PlacementTarget=button;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom;menu.IsOpen=true;};Grid.SetColumn(button,column);tiles.Children.Add(button);
        }
        Tile(0,"Convert",formats.Length==1?"1 format":$"{formats.Length} formats","convert",formats.Select(f=>(f.ToUpperInvariant(),(Action)(async()=>await EditAsync(new("convert:"+f,"Convert","↗"))))));
        Tile(1,"Tools",tools.Count==1?"1 tool":$"{tools.Count} tools","sliders",tools.Select(t=>(t.Title,(Action)(async()=>await EditAsync(t)))));
        Tile(2,"Actions","Copy, move, share","share",new[]{("Share","share"),("Float","float"),("Copy","copy"),("Move","move"),("Copy Path","copyPath"),("Terminal","terminal")}.Select(t=>(t.Item1,(Action)(async()=>await PerformFileAction(t.Item2,files.ToArray())))));
        var quick=new DockPanel{Margin=new(0,14,0,0)};if(openWheelButton is not null){(openWheelButton.Parent as Panel)?.Children.Remove(openWheelButton);openWheelButton.Margin=new(0);DockPanel.SetDock(openWheelButton,Dock.Right);quick.Children.Add(openWheelButton);}
        var picks=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};var label=Text("Quick convert",12,"Muted");label.Margin=new(2,0,7,0);picks.Children.Add(label);foreach(var format in formats.Take(4)){var chip=Button(format.ToUpperInvariant(),async()=>await EditAsync(new("convert:"+format,"Convert","↗")));chip.Style=Ui.Style("Chip");picks.Children.Add(chip);}
        if(formats.Length==0)label.Visibility=Visibility.Collapsed;quick.Children.Add(picks);stack.Children.Add(quick);
    }
}
