using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Nashi.Windows;
internal sealed class TrayHost:IDisposable
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private sealed class Notice
    {
        public uint Size;public IntPtr Window;public uint Id,Flags,Callback;public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip="Nashi";
        public uint State,StateMask;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info="";public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string InfoTitle="";public uint InfoFlags;public Guid Guid;public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)]private struct BitmapHeader{public uint Size;public int Width,Height;public ushort Planes,BitCount;public uint Compression,SizeImage;public int XPels,YPels;public uint ColorsUsed,ColorsImportant;}
    [StructLayout(LayoutKind.Sequential)]private struct IconInfo{[MarshalAs(UnmanagedType.Bool)]public bool IsIcon;public uint X,Y;public IntPtr Mask,Color;}
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern bool Shell_NotifyIcon(uint action,Notice data);
    [DllImport("gdi32.dll")]private static extern IntPtr CreateDIBSection(IntPtr dc,ref BitmapHeader info,uint usage,out IntPtr bits,IntPtr section,uint offset);
    [DllImport("gdi32.dll")]private static extern IntPtr CreateBitmap(int width,int height,uint planes,uint count,IntPtr bits);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll")]private static extern IntPtr CreateIconIndirect(ref IconInfo info);
    [DllImport("user32.dll")]private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr window);
    private TrayIconRecovery? recovery;private readonly HwndSource source;private readonly Notice notice;private readonly Action open;private readonly Func<ContextMenu> menu;private readonly uint taskbarCreated=RegisterWindowMessage("TaskbarCreated");
    internal void UpdateAvailable(string version){notice.Tip="Nashi · Update "+version+" available";if(notice.Tip.Length>127)notice.Tip=notice.Tip[..127];notice.Flags=4;Shell_NotifyIcon(1,notice);}
    public TrayHost(Window window,Action open,Func<ContextMenu> menu)
    {
        this.open=open;this.menu=menu;var handle=new WindowInteropHelper(window).Handle;source=HwndSource.FromHwnd(handle);source.AddHook(Hook);notice=new(){Size=(uint)Marshal.SizeOf<Notice>(),Window=handle,Id=1,Flags=7,Callback=0x8000+710,Icon=MakeIcon()};if(!Shell_NotifyIcon(0,notice)){Dispose();throw new InvalidOperationException("Windows could not add Nashi to the system tray.");}recovery=new(()=>{notice.Flags=7;return Shell_NotifyIcon(0,notice);},ScheduleRestore,open);
    }
    private static IntPtr MakeIcon()
    {
        var original=FileGarden.Artwork("NashiMark");var scaled=new TransformedBitmap(original,new ScaleTransform(32d/original.PixelWidth,32d/original.PixelHeight));var bitmap=new FormatConvertedBitmap(scaled,PixelFormats.Pbgra32,null,0);var bytes=new byte[32*32*4];bitmap.CopyPixels(bytes,32*4,0);var header=new BitmapHeader{Size=40,Width=32,Height=-32,Planes=1,BitCount=32};var color=CreateDIBSection(IntPtr.Zero,ref header,0,out var bits,IntPtr.Zero,0);var mask=CreateBitmap(32,32,1,1,IntPtr.Zero);
        try{if(color==IntPtr.Zero||bits==IntPtr.Zero||mask==IntPtr.Zero)throw new InvalidOperationException("Windows could not create a tray icon.");Marshal.Copy(bytes,0,bits,bytes.Length);var info=new IconInfo{IsIcon=true,Mask=mask,Color=color};var icon=CreateIconIndirect(ref info);if(icon==IntPtr.Zero)throw new InvalidOperationException("Windows could not create a tray icon.");return icon;}finally{if(color!=IntPtr.Zero)DeleteObject(color);if(mask!=IntPtr.Zero)DeleteObject(mask);}
    }
    private IntPtr Hook(IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled){if(taskbarCreated!=0&&message==(int)taskbarCreated){recovery?.Begin();}else if(message==0x8000+710){if(l.ToInt32()==0x203)open();else if(l.ToInt32()==0x205){var popup=menu();popup.Placement=System.Windows.Controls.Primitives.PlacementMode.MousePoint;popup.IsOpen=true;if(PresentationSource.FromVisual(popup) is HwndSource menuSource)SetForegroundWindow(menuSource.Handle);}handled=true;}return IntPtr.Zero;}
    private static void ScheduleRestore(Action retry)
    {
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};
        timer.Tick+=(_,_)=>{timer.Stop();retry();};timer.Start();
    }
    public void Dispose(){recovery?.Dispose();source.RemoveHook(Hook);Shell_NotifyIcon(2,notice);if(notice.Icon!=IntPtr.Zero){DestroyIcon(notice.Icon);notice.Icon=IntPtr.Zero;}}
}
