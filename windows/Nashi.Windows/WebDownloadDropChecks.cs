using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nashi.Core;

namespace Nashi.Windows;

internal static class WebDownloadDropChecks
{
    internal static void Run(string root,Action<bool,string> check)
    {
        const string video="https://www.youtube.com/watch?v=Fixture0001";
        const string photos="https://www.instagram.com/p/Fixture/";
        const string image="https://example.com/fixture.png";
        var directory=Path.Combine(root,"web-drop-"+Guid.NewGuid().ToString("N"));
        var destination=Path.Combine(directory,"downloads");
        var library=new LibraryStore(Path.Combine(directory,"library"));
        new PreferencesStore(library.DirectoryPath).Save(new(){KeepInTray=false,Feedback=false,Animations=false,OutputMode="ask",DownloadFolder=destination});
        var calls=new List<(string Url,string Kind,string Destination,int? Height)>();
        var cancelNext=false;
        var window=new MainWindow(library:library,checkForUpdates:false,offerToolSetup:false)
        {
            // Keep the WPF drop, dispatch, toast, cancellation and Library paths real without relying on an external extractor.
            WebTransferOverride=async(url,kind,folder,_,token,height,_)=>
            {
                calls.Add((url,kind,folder,height));
                using var tx=new ExportTransaction(folder);
                var output=Path.Combine(tx.StagingDirectory,kind=="video"?"fixture.mp4":"fixture.mp3");
                File.WriteAllText(output,"Synthetic "+kind);
                if(cancelNext)await Task.Delay(Timeout.Infinite,token);
                return tx.Publish([output],token);
            }
        };
        void Drop(string action,string url)
        {
            window.OnGlobalDrop(action,new DataObject(DataFormats.UnicodeText,url),null);
            DesktopInput.Pump(250);
        }
        void VerifyChooser(string action,string url)
        {
            var count=calls.Count;
            Drop(action,url);
            var dialog=window.DownloadWindow;
            if(action=="download:auto"&&dialog!=window)
                Descendants(dialog).OfType<ComboBox>().Single(box=>box.Items.Contains("Up to 720p")).SelectedIndex=2;
            check(dialog!=window&&dialog.IsVisible&&window.LinkField.Text==url&&calls.Count==count,
                $"The {action} route did not keep its chooser without downloading");
            if(dialog!=window)dialog.Close();
        }
        try
        {
            window.Show();
            VerifyChooser("download:auto",video);
            VerifyChooser("download:photos",photos);
            VerifyChooser("download:image",image);
            VerifyChooser("download:thumbnail",video);
            VerifyChooser("download:audio",photos); // An unsupported action must not become whichever format is first.

            Drop("download:video",video);
            check(calls.Count==1&&calls[0]==(video,"video",destination,null)&&window.DownloadWindow==window,
                "Video did not dispatch once to the download folder with an empty Home");
            var unrelated=Path.Combine(directory,"original.txt");
            File.WriteAllText(unrelated,"Original kept");
            window.AcceptLaunch([unrelated]);
            Drop("download:audio",video);
            check(calls.Count==2&&calls[1]==(video,"audio",destination,null)&&window.DownloadWindow==window&&window.SelectedFiles.SequenceEqual([unrelated]),
                "Audio did not dispatch once to the download folder while preserving the existing Home selection");
            var outputs=library.Entries.SelectMany(entry=>entry.AvailablePaths).Order().ToArray();
            check(outputs.SequenceEqual(new[]{Path.Combine(destination,"fixture.mp3"),Path.Combine(destination,"fixture.mp4")})&&File.ReadAllText(unrelated)=="Original kept",
                "Direct wheel downloads did not record both outputs without changing the original");

            cancelNext=true;
            Drop("download:audio",video);
            Drop("download:video",video); // A busy window must not start a second download.
            var cancel=Application.Current.Windows.OfType<ToastWindow>().Where(toast=>toast.Owner==window)
                .SelectMany(Descendants).OfType<Button>().SingleOrDefault(button=>button.Content as string=="Cancel");
            check(calls.Count==3&&cancel is not null,"Busy direct download did not keep one cancellable transfer");
            cancel?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DesktopInput.Pump(250);
            check(library.Entries.Count==2&&Directory.GetDirectories(destination,".nashi-*").Length==0,
                "Cancellation recorded a partial result or leaked its staging directory");
        }
        finally
        {
            foreach(var child in Application.Current.Windows.OfType<Window>().Where(candidate=>candidate.Owner==window).ToArray())child.Close();
            window.Close();
        }

        var badLibrary=new LibraryStore(Path.Combine(directory,"invalid-folder-library"));
        new PreferencesStore(badLibrary.DirectoryPath).Save(new(){KeepInTray=false,Feedback=false,DownloadFolder="relative-downloads"});
        var invalidFolder=new MainWindow(library:badLibrary,checkForUpdates:false,offerToolSetup:false){WebTransferOverride=window.WebTransferOverride};
        try
        {
            invalidFolder.Show();
            invalidFolder.OnGlobalDrop("download:video",new DataObject(DataFormats.UnicodeText,video),null);
            DesktopInput.Pump(250);
            check(invalidFolder.DownloadWindow!=invalidFolder&&calls.Count==3,"A relative downloads folder bypassed the chooser");
        }
        finally
        {
            if(invalidFolder.DownloadWindow!=invalidFolder)invalidFolder.DownloadWindow.Close();
            invalidFolder.Close();
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            yield return child;
            foreach(var next in Descendants(child))yield return next;
        }
    }
}
