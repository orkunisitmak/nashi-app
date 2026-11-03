using System.Windows;
namespace Nashi.Windows;
/// <summary>Explorer › Send to › Nashi: the shortcut's life cycle in a test folder, and a Send to launch opening the wheel.</summary>
internal static class SendToChecks
{
    internal static void Run(string root,Action<bool,string> check)
    {
        var folder=Path.Combine(root,"SendTo");Directory.CreateDirectory(folder);var original=SendToMenu.Folder;SendToMenu.Folder=folder;
        try
        {
            var executable=Environment.ProcessPath!;
            check(!SendToMenu.Enabled,"Send to must start turned off");
            SendToMenu.Set(true,executable);
            check(SendToMenu.Enabled&&string.Equals(ShellShortcut.Resolve(SendToMenu.Shortcut),executable,StringComparison.OrdinalIgnoreCase)&&ShellShortcut.Arguments(SendToMenu.Shortcut)==SendToMenu.Argument,"Turning Send to on must add a Nashi shortcut that asks for the wheel");
            check(Directory.GetFiles(folder).Length==1,"Send to must add exactly one shortcut");
            // A moved portable folder: the shortcut follows this copy of Nashi.
            var moved=Path.Combine(root,"Moved Nashi");Directory.CreateDirectory(moved);var copy=Path.Combine(moved,"Nashi.exe");File.WriteAllText(copy,"stand-in");
            SendToMenu.Refresh(copy);check(string.Equals(ShellShortcut.Resolve(SendToMenu.Shortcut),copy,StringComparison.OrdinalIgnoreCase),"A moved Nashi must update its Send to shortcut");
            SendToMenu.Set(false);check(!File.Exists(SendToMenu.Shortcut)&&!SendToMenu.Enabled,"Turning Send to off must remove the shortcut");
            SendToMenu.Refresh(copy);check(!File.Exists(SendToMenu.Shortcut),"Refreshing must not bring back a removed shortcut");
            // Someone else's "Nashi.lnk" (here to another program) is never treated as Nashi's or removed.
            var other=Path.Combine(root,"Other app");Directory.CreateDirectory(other);var notNashi=Path.Combine(other,"Viewer.exe");File.WriteAllText(notNashi,"x");
            ShellShortcut.Create(SendToMenu.Shortcut,notNashi);
            check(!SendToMenu.Enabled,"Another program's shortcut must not count as Nashi's");
            SendToMenu.Set(false);check(File.Exists(SendToMenu.Shortcut),"Another program's shortcut must not be removed");File.Delete(SendToMenu.Shortcut);
            var (wheel,paths)=SendToMenu.Split([SendToMenu.Marker,"a.png","b.png"]);check(wheel&&paths.SequenceEqual(["a.png","b.png"]),"The Send to marker must be removed from the paths");
            var (none,plain)=SendToMenu.Split(["a.png"]);check(!none&&plain.SequenceEqual(["a.png"]),"A normal launch must not open the wheel");
            var (flag,args)=SendToMenu.Split([SendToMenu.Argument,"c.pdf"]);check(flag&&args.SequenceEqual(["c.pdf"]),"The Send to argument must be removed from the paths");

            // A Send to launch adds the files and opens the tools wheel; a normal launch does not.
            var picture=Path.Combine(root,"Send to picture.png");ImageEngine.Save(System.Windows.Media.Imaging.BitmapSource.Create(4,4,96,96,System.Windows.Media.PixelFormats.Bgra32,null,new byte[64],16),picture,"png");
            var window=new MainWindow(checkForUpdates:false,offerToolSetup:false);window.Show();DesktopInput.Pump();
            try
            {
                window.AcceptLaunch([picture]);DesktopInput.Pump(200);
                check(!Application.Current.Windows.OfType<WheelWindow>().Any(w=>w.IsVisible),"A normal launch must not open the wheel");
                window.AcceptLaunch([SendToMenu.Marker,picture]);DesktopInput.Pump(300);
                var opened=Application.Current.Windows.OfType<WheelWindow>().FirstOrDefault(w=>w.IsVisible);
                check(opened is not null,"Send to must open the tools wheel for the files");
                opened?.Close();
            }
            finally{window.Close();}
        }
        finally{SendToMenu.Folder=original;}
    }
}
