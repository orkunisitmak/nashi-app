using System.Windows;
using Nashi.Core;
namespace Nashi.Windows;

internal static class WebDropChecks
{
    internal static void Run(string root,Action<bool,string> check)
    {
        var directory=Path.Combine(root,"web-drop-"+Guid.NewGuid().ToString("N"));
        var library=new LibraryStore(Path.Combine(directory,"library"));var destination=Path.Combine(directory,"downloads");
        new PreferencesStore(library.DirectoryPath).Save(new(){KeepInTray=false,Feedback=false,OutputMode="ask",DownloadFolder=destination});
        var window=new MainWindow(library:library,checkForUpdates:false,offerToolSetup:false);
        const string image="https://raw.githubusercontent.com/orkunisitmak/nashi-app/4f051ab04bc48ea2efb5338abed4990536cb4d97/Resources/Brand/NashiMark.png";
        try
        {
            window.Show();window.OnGlobalDrop("download:auto",new DataObject(DataFormats.UnicodeText,image),null);DesktopInput.Pump(250);
            check(window.DownloadWindow!=window,"A Home link drop still opens download options");window.DownloadWindow.Close();
            window.OnGlobalDrop("download:photos",new DataObject(DataFormats.UnicodeText,"https://www.instagram.com/p/Fixture/"),null);DesktopInput.Pump(250);
            check(window.DownloadWindow!=window,"Instagram photos still open the photo selection");window.DownloadWindow.Close();
            for(var i=0;i<2;i++)
            {
                // Exercise both an empty Home and a stale local-file selection; neither may open a folder picker.
                if(i==1){var source=Path.Combine(directory,"unrelated.txt");File.WriteAllText(source,"Original kept");window.AcceptLaunch([source]);}
                window.OnGlobalDrop("download:image",new DataObject(DataFormats.UnicodeText,image),null);
                var until=DateTime.UtcNow.AddSeconds(20);while(library.Entries.Count<=i&&DateTime.UtcNow<until)DesktopInput.Pump(50);
                var outputs=library.Entries.SelectMany(entry=>entry.AvailablePaths).ToArray();
                check(outputs.Length==i+1&&outputs.All(path=>Path.GetDirectoryName(path)==destination),"A wheel download starts immediately in the saved download folder, with or without local files");
                check(window.DownloadWindow==window,"An explicit wheel download does not reopen download options");
                check(outputs.All(path=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).Equals("27128b31fa9f3b7cf6c21c4b377be387b1503159b0227335144a17c2badf489a",StringComparison.OrdinalIgnoreCase)),"Wheel downloads retain the original image bytes");
            }
        }
        finally
        {
            foreach(var child in Application.Current.Windows.OfType<Window>().Where(candidate=>candidate.Owner==window).ToArray())child.Close();
            window.Close();
        }
    }
}
