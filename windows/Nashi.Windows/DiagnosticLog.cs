using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
namespace Nashi.Windows;

// Opt-in local diagnostics: always on in Debug builds, otherwise only with
// --diagnostics or NASHI_DIAGNOSTICS=1. Writes to %LOCALAPPDATA%\Nashi\logs and
// never leaves the machine. Typed characters in text fields are not recorded.
internal static class DiagnosticLog
{
    private static readonly object gate=new();private static StreamWriter? writer;private static readonly HashSet<IntPtr> hooked=[];
    internal static bool Enabled=>writer is not null;
    internal static string? FilePath{get;private set;}
    internal static void Install(string[] args)
    {
#if DEBUG
        var requested=true;
#else
        var requested=args.Contains("--diagnostics")||Environment.GetEnvironmentVariable("NASHI_DIAGNOSTICS")=="1";
#endif
        if(!requested||writer is not null)return;
        try
        {
            var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi","logs");Directory.CreateDirectory(folder);
            foreach(var old in new DirectoryInfo(folder).GetFiles("nashi-*.log").OrderByDescending(f=>f.CreationTimeUtc).Skip(9))try{old.Delete();}catch(IOException){}catch(UnauthorizedAccessException){}
            FilePath=Path.Combine(folder,$"nashi-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
            writer=new StreamWriter(new FileStream(FilePath,FileMode.CreateNew,FileAccess.Write,FileShare.Read)){AutoFlush=true};
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){writer=null;return;}
        Write("start",$"Nashi {typeof(DiagnosticLog).Assembly.GetName().Version} pid={Environment.ProcessId} os={Environment.OSVersion} args=[{string.Join(' ',args.Select(a=>a.StartsWith("--")?a:"<path>"))}] dpi={DpiText()} culture={System.Globalization.CultureInfo.CurrentUICulture.Name}");
        AppDomain.CurrentDomain.UnhandledException+=(_,e)=>Write("crash",$"terminating={e.IsTerminating} {e.ExceptionObject}");
        AppDomain.CurrentDomain.FirstChanceException+=(_,e)=>{if(e.Exception is not OperationCanceledException)Write("first-chance",$"{e.Exception.GetType().Name}: {e.Exception.Message}");};
        TaskScheduler.UnobservedTaskException+=(_,e)=>Write("task-error",e.Exception.ToString());
        Dispatcher.CurrentDispatcher.UnhandledException+=(_,e)=>Write("ui-error",e.Exception.ToString());
        EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler((s,_)=>Attach((Window)s)));
        EventManager.RegisterClassHandler(typeof(UIElement),UIElement.PreviewMouseDownEvent,new MouseButtonEventHandler((s,e)=>{if(ReferenceEquals(s,Window.GetWindow((DependencyObject)s)))LogMouse("mouse-down",(UIElement)s,e);}),true);
        EventManager.RegisterClassHandler(typeof(UIElement),UIElement.MouseUpEvent,new MouseButtonEventHandler((s,e)=>{if(ReferenceEquals(s,Window.GetWindow((DependencyObject)s)))Write("mouse-up",$"{e.ChangedButton} source={Describe(e.OriginalSource)} handled={e.Handled}");}),true);
        EventManager.RegisterClassHandler(typeof(MediaElement),MediaElement.MediaOpenedEvent,new RoutedEventHandler((s,_)=>{var m=(MediaElement)s;Write("media",$"opened {Path.GetFileName(m.Source?.LocalPath)} audio={m.HasAudio} video={m.HasVideo} length={(m.NaturalDuration.HasTimeSpan?m.NaturalDuration.TimeSpan.TotalSeconds:-1):0.0}s volume={m.Volume:0.00} muted={m.IsMuted}");}),true);
        EventManager.RegisterClassHandler(typeof(MediaElement),MediaElement.MediaFailedEvent,new EventHandler<ExceptionRoutedEventArgs>((s,e)=>Write("media",$"failed {Path.GetFileName(((MediaElement)s!).Source?.LocalPath)}: {e.ErrorException?.GetType().Name} {e.ErrorException?.Message}")),true);
        EventManager.RegisterClassHandler(typeof(ButtonBase),ButtonBase.ClickEvent,new RoutedEventHandler((s,e)=>{if(ReferenceEquals(s,e.OriginalSource))Write("click",Describe(s));}),true);
        EventManager.RegisterClassHandler(typeof(Window),UIElement.PreviewKeyDownEvent,new KeyEventHandler((_,e)=>Key(e)),true);
        EventManager.RegisterClassHandler(typeof(UIElement),Keyboard.GotKeyboardFocusEvent,new KeyboardFocusChangedEventHandler((s,e)=>{if(ReferenceEquals(s,e.NewFocus))Write("focus",Describe(e.NewFocus));}),true);
    }
    internal static void Write(string kind,string message)
    {
        if(writer is null)return;
        lock(gate){try{writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId,2}] {kind,-13} {message}");}catch(IOException){}catch(ObjectDisposedException){}}
    }
    private static void Attach(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero||!hooked.Add(handle))return;
        Write("window",$"loaded {window.GetType().Name} \"{window.Title}\" {window.ActualWidth:0}x{window.ActualHeight:0} enabled={window.IsEnabled} hitTest={window.IsHitTestVisible}");
        window.IsEnabledChanged+=(_,e)=>Write("window",$"{window.GetType().Name} enabled={e.NewValue}");
        window.Activated+=(_,_)=>Write("window",$"activated {window.GetType().Name}");window.Deactivated+=(_,_)=>Write("window",$"deactivated {window.GetType().Name}");
        window.Closed+=(_,_)=>{Write("window",$"closed {window.GetType().Name}");hooked.Remove(handle);};
        HwndSource.FromHwnd(handle)?.AddHook((IntPtr _,int message,IntPtr _,IntPtr _,ref bool _)=>{if(message is 0x0201 or 0x0202 or 0x0204 or 0x0021)Write("win32",$"{window.GetType().Name} msg=0x{message:X4}");return IntPtr.Zero;});
        if(System.Windows.Input.Mouse.Captured is {} captured)Write("capture",Describe(captured));
    }
    private static void LogMouse(string kind,UIElement window,MouseButtonEventArgs e)
    {
        var point=e.GetPosition(window);var hit=window.InputHitTest(point) as DependencyObject;
        Write(kind,$"{e.ChangedButton} at {point.X:0},{point.Y:0} source={Describe(e.OriginalSource)} hit={Describe(hit)} chain=[{Chain(hit)}] captured={Describe(System.Windows.Input.Mouse.Captured)} handled={e.Handled}");
    }
    private static void Key(KeyEventArgs e)
    {
        var key=e.Key==System.Windows.Input.Key.System?e.SystemKey:e.Key;
        var text=Keyboard.FocusedElement is TextBoxBase or PasswordBox&&Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift&&key is >=System.Windows.Input.Key.D0 and <=System.Windows.Input.Key.Z or >=System.Windows.Input.Key.NumPad0 and <=System.Windows.Input.Key.Divide or >=System.Windows.Input.Key.Oem1 and <=System.Windows.Input.Key.Oem102 or System.Windows.Input.Key.Space;
        Write("key",$"{(text?"<text>":Keyboard.Modifiers==ModifierKeys.None?key.ToString():Keyboard.Modifiers+"+"+key)} focus={Describe(Keyboard.FocusedElement)} handled={e.Handled}");
    }
    private static string Chain(DependencyObject? element)
    {
        var parts=new List<string>();
        for(var n=0;element is not null&&n<8;n++,element=element is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(element):LogicalTreeHelper.GetParent(element))
            if(element is UIElement ui&&(!ui.IsEnabled||!ui.IsHitTestVisible||ui.Opacity<1||element is ButtonBase or ScrollViewer or Window))parts.Add($"{element.GetType().Name}{(ui.IsEnabled?"":"!disabled")}{(ui.IsHitTestVisible?"":"!nohit")}{(ui.Opacity<1?$"!op{ui.Opacity:0.00}":"")}");
        return string.Join(" < ",parts);
    }
    private static string Describe(object? value)
    {
        if(value is null)return "none";if(value is not DependencyObject element)return value.GetType().Name;
        var name=AutomationProperties.GetName(element);if(string.IsNullOrEmpty(name))name=AutomationProperties.GetAutomationId(element);
        if(string.IsNullOrEmpty(name)&&element is ContentControl{Content:string content})name=content;
        if(string.IsNullOrEmpty(name)&&element is FrameworkElement{Name.Length:>0} named)name=named.Name;
        return string.IsNullOrEmpty(name)?element.GetType().Name:$"{element.GetType().Name}(\"{(name.Length>40?name[..40]+"…":name)}\")";
    }
    private static string DpiText(){var dc=GetDC(IntPtr.Zero);try{return $"{GetDeviceCaps(dc,88)}";}finally{_ = ReleaseDC(IntPtr.Zero,dc);}}
    [DllImport("user32.dll")]private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]private static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32.dll")]private static extern int GetDeviceCaps(IntPtr dc,int index);
}
