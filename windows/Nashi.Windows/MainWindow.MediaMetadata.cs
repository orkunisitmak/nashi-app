using System.Windows;
using Nashi.Core;
namespace Nashi.Windows;
public sealed partial class MainWindow
{
    private async Task EditMediaMetadataAsync(string[] selected)
    {
        if(!tools.Ready){MessageBox.Show(this,L10n.Text("Choose Set up media tools once to inspect and edit media tags."),L10n.Text("Media setup"));return;}var removeAll=selected.Length>1;var updates=new Dictionary<string,string>();
        if(selected.Length==1){var values=await MediaMetadata.Read(tools,selected[0]);var editor=new MetadataEditor(this,values,media:true);if(editor.ShowDialog()!=true)return;removeAll=editor.RemoveAll;updates=removeAll?[]:editor.Updates;}
        await RunAsync((destination,p,t)=>new MediaEngine(tools.Executable("ffmpeg"),tools.Executable("ffprobe")).ToolAsync("removeMetadata",selected,destination,new(){RemoveAllMetadata=removeAll,Metadata=updates},p,t),"Metadata");
    }
}
