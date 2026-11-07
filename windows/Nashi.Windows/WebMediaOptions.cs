using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Nashi.Core;
namespace Nashi.Windows;
public sealed partial class MainWindow
{
    private readonly TextBlock webSummary=new(){Text="Paste a public media link to begin.",TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};
    private readonly ComboBox webQuality=new(){ItemsSource=new[]{"Best available","Up to 1080p","Up to 720p"},SelectedIndex=0,MinWidth=160};
    private readonly StackPanel photoChoices=new();private readonly HashSet<int> selectedWebPhotos=[];private CancellationTokenSource? inspection;private string? inspectedLink,webPayloadKind;
    private void BuildWebOptions()
    {
        // The link and a paste button.
        link.Style=Ui.Style("Field");Ui.SetIcon(link,"link",15);L10n.Bind(link,Ui.HintProperty,"Paste a public video, image or audio page link");link.MinHeight=42;link.Margin=new(0);Ui.SetRadius(link,14);
        var row=new DockPanel();var paste=Button("Paste",()=>{try{link.Text=Clipboard.GetText();}catch(Exception e)when(ClipboardImport.IsClipboardError(e)){L10n.SetText(webSummary,"Clipboard is busy. Try Paste again.");}});paste.Style=Ui.Style("Big");paste.MinHeight=42;Ui.SetRadius(paste,14);Ui.SetIcon(paste,"paste");paste.Margin=new(8,0,0,0);DockPanel.SetDock(paste,Dock.Right);row.Children.Add(paste);row.Children.Add(link);webContent.Children.Add(row);
        // What the link points to.
        L10n.SetText(webSummary,webSummary.Text);var summary=new DockPanel();var icon=Ui.TileIcon("play");icon.Margin=new(0,0,12,0);DockPanel.SetDock(icon,Dock.Left);summary.Children.Add(icon);var find=Button("Find media",async()=>await InspectWebLinkAsync());find.Margin=new(12,0,0,0);find.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(find,Dock.Right);summary.Children.Add(find);summary.Children.Add(webSummary);
        var summaryCard=Card(summary,18,new(12));summaryCard.Margin=new(0,14,0,12);webContent.Children.Add(summaryCard);
        // Format, quality and destination as settings-style rows.
        var rows=new StackPanel();var group=new Border{Child=rows,CornerRadius=new(16),Padding=new(16,2,16,2),BorderThickness=new(1)};group.SetResourceReference(Border.BackgroundProperty,"Well");group.SetResourceReference(Border.BorderBrushProperty,"Line");webContent.Children.Add(group);
        DockPanel Row(string title,FrameworkElement control){if(rows.Children.Count>0){var line=new Border{Height=1};line.SetResourceReference(Border.BackgroundProperty,"Line");rows.Children.Add(line);}var item=new DockPanel{MinHeight=46};control.VerticalAlignment=VerticalAlignment.Center;control.Margin=new(0);DockPanel.SetDock(control,Dock.Right);item.Children.Add(control);item.Children.Add(Text(title,13));rows.Children.Add(item);return item;}
        downloadKind.Style=Ui.Style("SegmentedCombo");downloadKind.DropDownOpened+=(_,_)=>downloadKind.IsDropDownOpen=false;Row("Format",downloadKind);var quality=Row("Video quality",webQuality);var qualityLine=rows.Children[rows.Children.Count-2];downloadKind.SelectionChanged+=(_,_)=>quality.Visibility=qualityLine.Visibility=downloadKind.SelectedItem as string=="video"?Visibility.Visible:Visibility.Collapsed;
        var place=new StackPanel{Orientation=Orientation.Horizontal};var folderIcon=Icons.Make("folder",14,"Muted");folderIcon.Margin=new(0,0,6,0);place.Children.Add(folderIcon);var location=Text(Path.GetFileName(preferences.Value.DownloadFolder.TrimEnd('\\')),12,"Muted",localize:false);location.MaxWidth=220;location.TextWrapping=TextWrapping.NoWrap;location.TextTrimming=TextTrimming.CharacterEllipsis;location.ToolTip=preferences.Value.DownloadFolder;place.Children.Add(location);
        var change=Button("Change…",()=>{var picker=new OpenFolderDialog{Title=L10n.Text("Save downloaded media to…")};if(picker.ShowDialog(webWindow??this)==true){preferences.Save(preferences.Value with{DownloadFolder=picker.FolderName});location.Text=Path.GetFileName(picker.FolderName.TrimEnd('\\'));location.ToolTip=picker.FolderName;}});change.MinHeight=28;change.FontSize=12;change.Margin=new(10,0,0,0);place.Children.Add(change);Row("Save to",place);
        photoChoices.Margin=new(0,10,0,0);webContent.Children.Add(photoChoices);

        link.TextChanged+=(_,_)=>{inspection?.Cancel();inspectedLink=null;photoChoices.Children.Clear();selectedWebPhotos.Clear();var payload=WebMediaPayload.Parse(link.Text);if(payload is not null&&payload.Kind!=webPayloadKind){webPayloadKind=payload.Kind;SetDownloadKind(payload);}L10n.SetText(webSummary,"Choose Find media to inspect this link.");};
    }
    private async Task InspectWebLinkAsync()
    {
        if(busy)return;inspection?.Cancel();var pending=new CancellationTokenSource();inspection=pending;var input=link.Text;var payload=WebMediaPayload.Parse(input);if(payload is null){L10n.SetText(webSummary,"Paste one complete public media link.");return;}
        SetDownloadKind(payload,downloadKind.SelectedItem as string);photoChoices.Children.Clear();selectedWebPhotos.Clear();L10n.SetText(webSummary,"Finding the media…");
        try
        {
            var engine=new WebDownload(tools);
            if(payload.Kind=="instagramPhotos")
            {
                if(!tools.Ready)throw new InvalidOperationException("Set up media tools once to inspect Instagram posts.");var photos=await engine.InspectPhotosAsync(payload.Url,pending.Token);if(input!=link.Text||pending.IsCancellationRequested)return;
                webSummary.Text=$"{photos.Length} photos · full-frame images · saved in post order";var row=new WrapPanel();row.Children.Add(Button("All",()=>{foreach(var box in photoChoices.Children.OfType<CheckBox>())box.IsChecked=true;}));row.Children.Add(Button("None",()=>{foreach(var box in photoChoices.Children.OfType<CheckBox>())box.IsChecked=false;}));photoChoices.Children.Add(row);
                foreach(var photo in photos){selectedWebPhotos.Add(photo.Id);var box=new CheckBox{Content=$"Photo {photo.Id}  ·  {photo.Width} × {photo.Height}",IsChecked=true,Margin=new(4,9,4,9)};box.Checked+=(_,_)=>selectedWebPhotos.Add(photo.Id);box.Unchecked+=(_,_)=>selectedWebPhotos.Remove(photo.Id);photoChoices.Children.Add(box);}
            }
            else if(payload.Kind=="image"||downloadKind.SelectedItem as string=="thumbnail")L10n.SetText(webSummary,payload.Kind=="image"?"Original image · ready to download":"YouTube thumbnail · best available full-size image");
            else
            {
                if(!tools.Ready)throw new InvalidOperationException("Set up media tools once to inspect this media.");var info=await engine.InspectAsync(payload.Url,pending.Token);if(input!=link.Text||pending.IsCancellationRequested)return;webSummary.Text=info.Title+"\n"+info.Site+(info.Duration is {} seconds?" · "+TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss"):"");if(info.AudioOnly){downloadKind.ItemsSource=new[]{"audio"};downloadKind.SelectedIndex=0;}
            }
            inspectedLink=input;
        }catch(OperationCanceledException){}catch(Exception error){if(!pending.IsCancellationRequested)L10n.SetText(webSummary,error.Message);}finally{if(inspection==pending)inspection=null;pending.Dispose();}
    }
}
