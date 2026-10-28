using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Nashi.Core;
namespace Nashi.Windows;
public sealed partial class MainWindow
{
    private readonly TextBlock webSummary=new(){Text="Paste a public media link to begin.",TextWrapping=TextWrapping.Wrap,Margin=new(4,12,4,12)};
    private readonly ComboBox webQuality=new(){ItemsSource=new[]{"Best available","Up to 1080p","Up to 720p"},SelectedIndex=0};
    private readonly StackPanel photoChoices=new();private readonly HashSet<int> selectedWebPhotos=[];private CancellationTokenSource? inspection;private string? inspectedLink,webPayloadKind;
    private void BuildWebOptions()
    {
        var buttons=new WrapPanel();buttons.Children.Add(Button("Find media",async()=>await InspectWebLinkAsync()));buttons.Children.Add(Button("Paste link",()=>{try{link.Text=Clipboard.GetText();}catch(Exception e)when(ClipboardImport.IsClipboardError(e)){webSummary.Text="Clipboard is busy. Try Paste again.";}}));webContent.Children.Add(buttons);webContent.Children.Add(webSummary);
        var quality=new StackPanel();quality.Children.Add(Text("Video quality",12,"Muted"));quality.Children.Add(webQuality);webContent.Children.Add(quality);downloadKind.SelectionChanged+=(_,_)=>quality.Visibility=downloadKind.SelectedItem as string=="video"?Visibility.Visible:Visibility.Collapsed;webContent.Children.Add(photoChoices);
        var destination=new DockPanel{Margin=new(0,14,0,4)};var location=Text(preferences.Value.DownloadFolder,12,"Muted");var change=Button("Change…",()=>{var picker=new OpenFolderDialog{Title=L10n.Text("Save downloaded media to…")};if(picker.ShowDialog(webWindow??this)==true){preferences.Save(preferences.Value with{DownloadFolder=picker.FolderName});location.Text=picker.FolderName;}});DockPanel.SetDock(change,Dock.Right);destination.Children.Add(change);destination.Children.Add(location);webContent.Children.Add(Card(destination));
        webContent.Children.Add(Text("Instagram photo posts open a photo selection. Reel links offer video and audio. Public downloadable media only.",11,"Muted"));
        link.TextChanged+=(_,_)=>{inspection?.Cancel();inspectedLink=null;photoChoices.Children.Clear();selectedWebPhotos.Clear();var payload=WebMediaPayload.Parse(link.Text);if(payload is not null&&payload.Kind!=webPayloadKind){webPayloadKind=payload.Kind;SetDownloadKind(payload);}L10n.SetText(webSummary,"Choose Find media to inspect this link.");};
    }
    private async Task InspectWebLinkAsync()
    {
        if(busy)return;inspection?.Cancel();var pending=new CancellationTokenSource();inspection=pending;var input=link.Text;var payload=WebMediaPayload.Parse(input);if(payload is null){webSummary.Text="Paste one complete public media link.";return;}
        SetDownloadKind(payload,downloadKind.SelectedItem as string);photoChoices.Children.Clear();selectedWebPhotos.Clear();webSummary.Text="Finding the media…";
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
        }catch(OperationCanceledException){}catch(Exception error){if(!pending.IsCancellationRequested)webSummary.Text=error.Message;}finally{if(inspection==pending)inspection=null;pending.Dispose();}
    }
}
