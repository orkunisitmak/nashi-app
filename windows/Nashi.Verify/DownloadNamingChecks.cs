using Nashi.Core;
internal static class DownloadNamingChecks
{
    internal static void Run(Action<bool,string> check,string root)
    {
        var preferences=Path.Combine(root,"download-preferences");Directory.CreateDirectory(preferences);
        File.WriteAllText(Path.Combine(preferences,"preferences.json"),"{\"Language\":\"tr\"}");
        var store=new PreferencesStore(preferences);check(store.Value.AskBeforeDownload,"Existing preferences default to asking before downloads");
        store.Save(store.Value with{AskBeforeDownload=false});check(!new PreferencesStore(preferences).Value.AskBeforeDownload,"The direct-download preference survives restart");
        foreach(var value in new[]{"", " ", "../outside", "folder/file", "folder\\file", "CON", "nul.txt", "trailing.", "colon:name", "line\nbreak",new string('a',161)})
        {
            var rejected=false;try{WebDownload.ValidateFileName(value);}catch(ArgumentException){rejected=true;}
            check(rejected,"Unsafe custom download name rejected: "+value);
        }
        foreach(var title in new[]{"YouTube başlığı — İzmir 🍐", "a:b/c\\d?*", "CON", ".", new string('a',158)+"🍐suffix"})
        {
            var suggested=WebDownload.SuggestedFileName(title);WebDownload.ValidateFileName(suggested);
            check(suggested.Length<=160&&!char.IsHighSurrogate(suggested[^1]),"Suggested media title is a valid filename");
        }
        var destination=Path.Combine(root,"named-downloads");Directory.CreateDirectory(destination);File.WriteAllText(Path.Combine(destination,"Özel başlık.mp4"),"original");
        using(var transaction=new ExportTransaction(destination))
        {
            var source=Path.Combine(transaction.StagingDirectory,"Video.mp4");File.WriteAllText(source,"download");
            var output=WebDownload.PublishDownloads(transaction,[source],"Özel başlık.mp4",CancellationToken.None).Single();
            check(Path.GetFileName(output)=="Özel başlık 2.mp4"&&File.ReadAllText(output)=="download"&&File.ReadAllText(Path.Combine(destination,"Özel başlık.mp4"))=="original","Custom names retain Unicode and format, add extensions once and preserve existing files");
        }
        using(var transaction=new ExportTransaction(destination))
        {
            var sources=new[]{"one.jpg","two.png"}.Select(name=>{var path=Path.Combine(transaction.StagingDirectory,name);File.WriteAllText(path,name);return path;}).ToArray();
            var outputs=WebDownload.PublishDownloads(transaction,sources,"Photo set",CancellationToken.None);
            check(outputs.Select(Path.GetFileName).SequenceEqual(new[]{"Photo set 01.jpg","Photo set 02.png"}),"Photo batch names retain order and real formats");
        }
        using(var transaction=new ExportTransaction(destination))using(var cancelled=new CancellationTokenSource())
        {
            var source=Path.Combine(transaction.StagingDirectory,"Original.png");File.WriteAllText(source,"download");cancelled.Cancel();var rejected=false;
            try{WebDownload.PublishDownloads(transaction,[source],"Cancelled",cancelled.Token);}catch(OperationCanceledException){rejected=true;}
            check(rejected&&!File.Exists(Path.Combine(destination,"Cancelled.png")),"Cancelling a renamed download publishes nothing");
        }
        Directory.Delete(preferences,true);Directory.Delete(destination,true);
    }
}
