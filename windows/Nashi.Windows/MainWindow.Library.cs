using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nashi.Core;
namespace Nashi.Windows;
public sealed partial class MainWindow
{
    private readonly TextBlock selectionPill=Text("✦  START HERE",11,"Gold");
    private Guid? selectedLibraryId;private Point libraryDragStart;
    private readonly TextBlock homeTitle=Text("Your files. Their next form.",30);
    private readonly Dictionary<Guid,Border> libraryCards=[];
    private void ShowToolSetupMenu()
    {
        if(busy)return;
        toolSetupOffered=true;
        if(toolSetupWindow is not null){toolSetupWindow.Activate();return;}
        toolSetupWindow=new ToolSetupWindow(this,tools,officeRuntime,SetBusy);
        try{toolSetupWindow.ShowDialog();}finally{toolSetupWindow=null;}
    }
    private void OfferFirstRunTools()
    {
        if(toolSetupOffered || busy || tools.Ready && officeRuntime.Executable is not null)return;
        toolSetupOffered=true;ShowToolSetupMenu();
    }
    private void ConfigureLibraryRow(Border card,LibraryEntry entry)
    {
        libraryCards[entry.Id]=card;card.Tag=entry;card.Focusable=true;card.ToolTip=L10n.Text(entry.IsSpace?"Click or Enter: open this space on Home":"Space: preview · Enter: use in Nashi");
        card.BorderBrush=selectedLibraryId==entry.Id?Theme.Brush("Gold"):Theme.Brush("Line");
        card.GotKeyboardFocus+=(_,_)=>SelectCard(entry.Id);
        card.PreviewMouseLeftButtonDown+=(_,e)=>{libraryDragStart=e.GetPosition(card);SelectCard(entry.Id);if(!IsButton(e.OriginalSource as DependencyObject)){card.Focus();}};
        // A click (not a drag, not a button) opens the batch as its own desk.
        card.MouseLeftButtonUp+=(_,e)=>{if(!IsButton(e.OriginalSource as DependencyObject)&&(e.GetPosition(card)-libraryDragStart).Length<8)OpenEntry(entry);};
        card.MouseMove+=(_,e)=>{if(e.LeftButton==MouseButtonState.Pressed&&(e.GetPosition(card)-libraryDragStart).Length>8&&entry.AvailablePaths.Length>0&&!IsButton(e.OriginalSource as DependencyObject)){DragDrop.DoDragDrop(card,new DataObject(DataFormats.FileDrop,entry.AvailablePaths),DragDropEffects.Copy);}};
    }
    private static bool IsButton(DependencyObject? value){while(value is not null){if(value is Button)return true;value=value is Visual?VisualTreeHelper.GetParent(value):LogicalTreeHelper.GetParent(value);}return false;}
    private void SelectCard(Guid id){selectedLibraryId=id;foreach(var pair in libraryCards)pair.Value.BorderBrush=Theme.Brush(pair.Key==id?"Gold":"Line");}
    private void SelectLibrary(int direction)
    {
        var entries=library.Search(search.Text).ToArray();if(entries.Length==0)return;var old=Array.FindIndex(entries,e=>e.Id==selectedLibraryId);var next=old<0?0:Math.Clamp(old+direction,0,entries.Length-1);SelectCard(entries[next].Id);if(libraryCards.TryGetValue(entries[next].Id,out var card)){card.Focus();card.BringIntoView();}
    }
    private LibraryEntry? SelectedLibrary()=>library.Search(search.Text).FirstOrDefault(e=>e.Id==selectedLibraryId)??library.Search(search.Text).FirstOrDefault();
    private void PreviewSelectedLibrary(){if(SelectedLibrary() is {} entry)PreviewEntry(entry);}
    private void PreviewEntry(LibraryEntry entry){if(entry.AvailablePaths.Length>0)new FilePreviewWindow(this,entry.AvailablePaths).Show();else L10n.SetText(status,"These files have moved or were deleted.");}
    private void ReuseSelectedLibrary(){if(busy||SelectedLibrary() is not {} entry)return;if(entry.IsSpace){OpenSpace(entry);return;}if(files.Count>0){KeepDesk();deskSession=Guid.NewGuid();files.Clear();}Add(entry.AvailablePaths);ShowLibrary(false);}
    private void BuildSelectionActions()
    {
        L10n.SetText(homeTitle,files.Count==0?"Your files. Their next form.":"Let’s make something of it.");
        if(files.Count==0)return;var targets=Targets.ToArray();
        var stack=new StackPanel{Margin=new(0,14,0,0)};actions.Children.Add(stack);var tiles=new Grid();for(var n=0;n<3;n++)tiles.ColumnDefinitions.Add(new());stack.Children.Add(tiles);
        var formats=FeatureCatalog.Formats(targets);var tools=Choices();
        void Tile(int column,string title,string subtitle,string symbol,IEnumerable<(string,Action)> commands)
        {
            var label=new DockPanel();var icon=Icons.Create(symbol,22,"Gold");icon.VerticalAlignment=VerticalAlignment.Center;icon.Margin=new(0,0,14,0);DockPanel.SetDock(icon,Dock.Left);label.Children.Add(icon);var caption=new StackPanel();var heading=Text(title,14);heading.FontWeight=FontWeights.SemiBold;caption.Children.Add(heading);caption.Children.Add(Text(subtitle,11,"Muted"));label.Children.Add(caption);
            var button=Button("",()=>{});button.Content=label;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Padding=new(16,14,16,14);button.SetResourceReference(Control.BackgroundProperty,"Surface");button.Margin=new(column==0?0:6,0,column==2?0:6,0);var menu=new ContextMenu();foreach(var (name,action) in commands){var item=new MenuItem{Header=name};item.Click+=(_,_)=>action();menu.Items.Add(item);}button.IsEnabled=menu.Items.Count>0;button.Click+=(_,_)=>{menu.PlacementTarget=button;menu.IsOpen=true;};Grid.SetColumn(button,column);tiles.Children.Add(button);
        }
        Tile(0,"Convert",$"{formats.Length} ways to change it","arrows-left-right",formats.Select(f=>(f.ToUpperInvariant(),(Action)(async()=>await EditAsync(new("convert:"+f,"Convert","↗"),targets)))));
        Tile(1,"Fine-tune","Edit, compress, and more","sliders-horizontal",tools.Select(t=>(t.Title,(Action)(async()=>await EditAsync(t,targets)))));
        Tile(2,"Move & share","Send it on its way","share-network",new[]{("Share","share"),("Float","float"),("Copy","copy"),("Move","move"),("Copy Path","copyPath"),("Terminal","terminal")}.Select(t=>(t.Item1,(Action)(async()=>await PerformFileAction(t.Item2,targets)))));
        var quick=new WrapPanel{Margin=new(0,10,0,0)};var quickLabel=Text("Quick picks",12,"Muted");quickLabel.Margin=new(2,0,8,0);quick.Children.Add(quickLabel);foreach(var format in formats.Take(4))quick.Children.Add(Button(format.ToUpperInvariant(),async()=>await EditAsync(new("convert:"+format,"Convert","↗"),targets)));if(formats.Length>0)stack.Children.Add(quick);
    }
}
