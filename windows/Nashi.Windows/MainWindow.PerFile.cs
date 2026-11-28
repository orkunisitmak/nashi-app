using System.IO;
using System.Windows;
using Nashi.Core;
namespace Nashi.Windows;

public sealed partial class MainWindow
{
    /// Crop, cover and annotate with several images selected: each image opens in its own editor
    /// ("Crop Image 2/3"), so every one keeps its own area or drawing. Each confirmed image is exported at once
    /// into a private folder, so no full-size bitmap is kept while the next editors are open. Nothing is
    /// published until every editor is confirmed; Cancel at any step discards all of it.
    private async Task EditImagesOneByOneAsync(ToolChoice choice,string[] selected)
    {
        var holding=Path.Combine(Path.GetTempPath(),"Nashi-batch-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(holding);
        try
        {
            var results=new List<string>();var encoder=tools.Ready?tools.Executable("ffmpeg"):null;
            for(var n=0;n<selected.Length;n++)
            {
                var file=selected[n];var editor=new ToolEditor(this,choice.Id,choice.Title,file,null,ImageEngine.Load(file),[file],null);
                editor.ConfigureProfiles(preferences,null);editor.ConfigureMediaRuntime(tools);
                editor.Title=L10n.Text(choice.Title)+" "+(n+1)+"/"+selected.Length+" · Nashi";
                if(editor.ShowDialog()!=true)return;
                var options=editor.ImageOptions with{EncoderPath=encoder};
                SetBusy(true);
                try{results.AddRange(await OnStaAsync(()=>ImageEngine.Apply(choice.Id,[file],holding,options)));}
                finally{SetBusy(false);}
            }
            await RunAsync((destination,p,t)=>Task.Run<IReadOnlyList<string>>(()=>
            {
                using var tx=new ExportTransaction(destination);
                // Copy, not move: the files then take the permissions of the destination folder, not of the temp folder.
                var staged=results.Select(path=>{t.ThrowIfCancellationRequested();var target=SafePaths.Unique(tx.StagingDirectory,Path.GetFileName(path));File.Copy(path,target);return target;}).ToList();
                return tx.Publish(staged,t);
            },t),choice.Title);
        }
        finally{await DeleteFolderAsync(holding);}
    }
    // A file can stay locked for a moment after it was written (antivirus, indexing): retry a few times, never throw.
    private static async Task DeleteFolderAsync(string folder)
    {
        for(var attempt=0;attempt<5;attempt++)
        {
            try{if(Directory.Exists(folder))Directory.Delete(folder,true);return;}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException){await Task.Delay(150*(attempt+1));}
        }
    }
}
