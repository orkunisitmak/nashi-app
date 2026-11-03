using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
namespace Nashi.Windows;
/// <summary>
/// A global shortcut (Win+Alt+N by default, changeable or off in Settings) that opens Nashi's quick
/// menu at the cursor from any app: copy text from screen, scan QR codes on screen, use a copied link
/// or open Nashi. A shortcut another app already owns is not taken; Settings says so instead.
/// </summary>
internal sealed class QuickMenuHotkey:IDisposable
{
    internal const string Off="Off";
    internal static readonly string[] Choices=["Win+Alt+N","Win+Shift+Q","Ctrl+Shift+Q","Ctrl+Shift+Space",Off];
    [DllImport("user32.dll")]private static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]private static extern bool UnregisterHotKey(IntPtr window,int id);
    private const int Id=7702;private const int WmHotkey=0x0312;
    private readonly HwndSource source;private readonly Action pressed;
    /// <summary>The app's registered shortcut, so Settings can say when another app owns the chosen one.</summary>
    internal static QuickMenuHotkey? Active{get;set;}
    internal string? Shortcut{get;private set;}
    /// <summary>True when the chosen shortcut is registered; false when it is off or another app owns it.</summary>
    internal bool Registered{get;private set;}
    internal IntPtr Handle=>source.Handle;

    internal QuickMenuHotkey(Action pressed)
    {
        this.pressed=pressed;
        // A message-only window receives WM_HOTKEY without appearing anywhere.
        source=new HwndSource(new HwndSourceParameters("Nashi quick menu"){ParentWindow=new IntPtr(-3),Width=0,Height=0,WindowStyle=0});
        source.AddHook(Hook);
    }

    /// <summary>"Win+Alt+N" → MOD_WIN|MOD_ALT and 'N'; null for Off or an unknown key.</summary>
    internal static (uint Modifiers,uint Key)? Parse(string? shortcut)
    {
        if(string.IsNullOrWhiteSpace(shortcut)||shortcut==Off)return null;
        uint modifiers=0x4000;uint key=0;// MOD_NOREPEAT: holding the keys opens the menu once.
        foreach(var part in shortcut.Split('+',StringSplitOptions.TrimEntries))
        {
            switch(part)
            {
                case "Ctrl":modifiers|=0x2;break;case "Alt":modifiers|=0x1;break;case "Shift":modifiers|=0x4;break;case "Win":modifiers|=0x8;break;
                case "Space":key=0x20;break;
                case {Length:1} letter when char.IsAsciiLetterOrDigit(letter[0]):key=char.ToUpperInvariant(letter[0]);break;
                default:return null;
            }
        }
        // A shortcut needs a key and at least one modifier, so typing a letter never opens the menu.
        return key==0||(modifiers&0xF)==0?null:(modifiers,key);
    }

    internal void Apply(string? shortcut)
    {
        if(shortcut==Shortcut&&(Registered||Parse(shortcut) is null))return;
        if(Registered){UnregisterHotKey(source.Handle,Id);Registered=false;}
        Shortcut=shortcut;
        if(Parse(shortcut) is {} parsed)Registered=RegisterHotKey(source.Handle,Id,parsed.Modifiers,parsed.Key);
    }

    private IntPtr Hook(IntPtr window,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message==WmHotkey&&wParam.ToInt32()==Id){handled=true;pressed();}
        return IntPtr.Zero;
    }
    public void Dispose(){if(Registered)UnregisterHotKey(source.Handle,Id);source.RemoveHook(Hook);source.Dispose();}
}

/// <summary>The quick menu: a small card at the cursor. Number keys, arrows and Enter choose; Esc or clicking elsewhere closes it.</summary>
internal sealed class QuickMenuWindow:Window
{
    internal sealed record Entry(string Title,string Symbol,Action Run);
    [StructLayout(LayoutKind.Sequential)]private struct Point32{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct Rect32{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct MonitorInfo{public int Size;public Rect32 Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out Point32 point);
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromPoint(Point32 point,uint flags);
    [DllImport("user32.dll")]private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr window,out Rect32 rect);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr window);
    private readonly IReadOnlyList<Entry> entries;private readonly List<Button> buttons=[];private bool done;
    internal IReadOnlyList<Button> ButtonsForTest=>buttons;

    internal QuickMenuWindow(IReadOnlyList<Entry> entries,string? shortcut)
    {
        this.entries=entries;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;Topmost=true;SizeToContent=SizeToContent.WidthAndHeight;AllowsTransparency=true;Background=Brushes.Transparent;Title="Nashi";
        WindowStartupLocation=WindowStartupLocation.Manual;Left=-32000;Top=-32000;
        var stack=new StackPanel{Margin=new(10)};
        var header=new DockPanel{Margin=new(8,4,8,8)};
        header.Children.Add(new TextBlock{Text="Nashi",FontWeight=FontWeights.Bold,FontSize=15,Foreground=Theme.Brush("Ink")});
        if(shortcut is not null){var hint=new TextBlock{Text=shortcut,FontSize=11,Foreground=Theme.Brush("Muted"),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(hint,Dock.Right);header.Children.Insert(0,hint);}
        stack.Children.Add(header);
        for(var i=0;i<entries.Count;i++)
        {
            var index=i;var row=new DockPanel();
            row.Children.Add(new TextBlock{Text=entries[i].Symbol,Width=26,FontSize=15,Foreground=Theme.Brush("Gold"),VerticalAlignment=VerticalAlignment.Center});
            var number=new TextBlock{Text=(i+1).ToString(),Foreground=Theme.Brush("Muted"),Margin=new(18,0,0,0),VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(number,Dock.Right);row.Children.Insert(0,number);
            row.Children.Add(L10n.Label(new TextBlock{Text=entries[i].Title,FontSize=14,Foreground=Theme.Brush("Ink"),VerticalAlignment=VerticalAlignment.Center}));
            var button=new Button{Content=row,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new(12,9,12,9),Margin=new(0,2,0,2),MinWidth=290,Background=Brushes.Transparent,BorderThickness=new(0),Cursor=Cursors.Hand};
            System.Windows.Automation.AutomationProperties.SetName(button,L10n.Text(entries[i].Title));
            button.Click+=(_,_)=>Choose(index);buttons.Add(button);stack.Children.Add(button);
        }
        Content=new Border{Child=stack,CornerRadius=new(18),Background=Theme.Brush("Surface"),BorderBrush=Theme.Brush("Line"),BorderThickness=new(1),
            Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=24,ShadowDepth=4,Opacity=.28}};
        Loaded+=(_,_)=>{PlaceAtCursor();var handle=new WindowInteropHelper(this).Handle;SetForegroundWindow(handle);Activate();buttons.FirstOrDefault()?.Focus();};
        // Closing from code (the shortcut pressed again) also deactivates the window; it must not close twice.
        Closing+=(_,_)=>done=true;
        Deactivated+=(_,_)=>Dismiss();
        PreviewKeyDown+=(_,e)=>
        {
            if(e.Key==Key.Escape){Dismiss();e.Handled=true;return;}
            var digit=e.Key is >=Key.D1 and <=Key.D9?e.Key-Key.D1:e.Key is >=Key.NumPad1 and <=Key.NumPad9?e.Key-Key.NumPad1:-1;
            if(digit>=0&&digit<entries.Count){Choose(digit);e.Handled=true;return;}
            var focused=buttons.FindIndex(b=>b.IsKeyboardFocused);
            if(e.Key is Key.Down or Key.Up){buttons[((focused<0?0:focused)+(e.Key==Key.Down?1:-1)+buttons.Count)%buttons.Count].Focus();e.Handled=true;}
        };
    }

    /// <summary>Runs an entry after the menu has closed, so screen captures never include it.</summary>
    internal void Choose(int index)
    {
        if(done||index<0||index>=entries.Count)return;done=true;var run=entries[index].Run;Close();
        Dispatcher.BeginInvoke(run,System.Windows.Threading.DispatcherPriority.Background);
    }
    private void Dismiss(){if(done)return;done=true;Close();}

    private void PlaceAtCursor()
    {
        var handle=new WindowInteropHelper(this).Handle;GetCursorPos(out var cursor);var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(MonitorFromPoint(cursor,2),ref info)||!GetWindowRect(handle,out var rect))return;
        var width=rect.Right-rect.Left;var height=rect.Bottom-rect.Top;
        // Just below and right of the cursor, kept inside the work area of the cursor's display.
        var x=Math.Clamp(cursor.X+8,info.Work.Left,Math.Max(info.Work.Left,info.Work.Right-width));var y=Math.Clamp(cursor.Y+8,info.Work.Top,Math.Max(info.Work.Top,info.Work.Bottom-height));
        SetWindowPos(handle,new IntPtr(-1),x,y,0,0,0x0001|0x0040);
    }
}
