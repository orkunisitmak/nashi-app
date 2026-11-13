using System.Diagnostics;
namespace Nashi.Linux;

/// The wheel's soft tick (the same WheelSelection.wav as macOS and Windows), played through
/// PipeWire, PulseAudio or ALSA, whichever is present. Never blocks and never throws.
public static class Sound
{
    public static bool Enabled{get;set;}=true;
    private static readonly string File=Path.Combine(AppContext.BaseDirectory,"WheelSelection.wav");
    private static readonly string? Player=new[]{"pw-play","paplay","aplay"}.Select(LinuxShell.FindInPath).FirstOrDefault(p=>p is not null);
    private static long last;
    public static void Tick()
    {
        if(!Enabled||Player is null||!System.IO.File.Exists(File))return;
        var now=Environment.TickCount64;if(now-Interlocked.Read(ref last)<45)return;Interlocked.Exchange(ref last,now);
        try
        {
            var info=new ProcessStartInfo(Player){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};
            if(Path.GetFileName(Player)=="aplay")info.ArgumentList.Add("-q");
            if(Path.GetFileName(Player)=="pw-play"){info.ArgumentList.Add("--volume");info.ArgumentList.Add("0.35");}
            info.ArgumentList.Add(File);
            var process=Process.Start(info);if(process is not null)_=process.WaitForExitAsync().ContinueWith(_=>process.Dispose());
        }
        catch(Exception error)when(error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException){}
    }
}
