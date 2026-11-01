using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
namespace Nashi.Windows;
// Used only by the explicit --smoke-test path on an isolated Windows desktop.
// SendInput exercises WPF's real keyboard routing, including focused text boxes.
// If SendInput is denied in non-interactive developer environments, routed events act as fallback.
internal static class DesktopInput
{
    private static void DispatchAction(Action action)
    {
        if(Application.Current is { } app && app.Dispatcher is { } disp && !disp.HasShutdownStarted)
        {
            if(disp.CheckAccess()) action();
            else disp.BeginInvoke(action);
        }
    }
    private static readonly HashSet<System.Windows.Input.Key> ActiveModifiers = [];
    internal static bool SimulatedShift { get; set; }
    internal static bool SimulatedAlt { get; set; }
    internal static bool SimulatedControl { get; set; }
    internal static Point? SimulatedScreenPosition { get; set; }
    internal static Point? SimulatedLocalPosition { get; set; }
    internal static int SimulatedChecks { get; set; }
    internal static bool IsPrimaryMouseDown { get; set; }
    internal static bool IsShiftPressed => ActiveModifiers.Contains(System.Windows.Input.Key.LeftShift) || ActiveModifiers.Contains(System.Windows.Input.Key.RightShift) || SimulatedShift;
    internal static bool IsAltPressed => ActiveModifiers.Contains(System.Windows.Input.Key.LeftAlt) || ActiveModifiers.Contains(System.Windows.Input.Key.RightAlt) || SimulatedAlt;
    internal static bool IsControlPressed => ActiveModifiers.Contains(System.Windows.Input.Key.LeftCtrl) || ActiveModifiers.Contains(System.Windows.Input.Key.RightCtrl) || SimulatedControl;
    internal static ModifierKeys EffectiveModifiers => Keyboard.Modifiers
        | (IsShiftPressed ? ModifierKeys.Shift : ModifierKeys.None)
        | (IsControlPressed ? ModifierKeys.Control : ModifierKeys.None)
        | (IsAltPressed ? ModifierKeys.Alt : ModifierKeys.None);
    // Without KEYEVENTF_EXTENDEDKEY, arrows and the navigation block arrive as numpad keys; with
    // NumLock on, Windows then releases Shift around them, so Shift+Right reaches WPF as plain Right.
    private static uint KeyFlags(int vk,bool up)=>(up?2u:0u)|(vk is >=0x21 and <=0x28 or 0x2D or 0x2E?1u:0u);
    internal static void ReleaseAll()
    {
        IsPrimaryMouseDown = false;
        SimulatedShift = false;
        SimulatedAlt = false;
        SimulatedControl = false;
        SimulatedScreenPosition = null;
        SimulatedLocalPosition = null;
        SimulatedDroppedFiles = null;
        if(NativeInputAvailable)
        {
            var mouseUp = new Input{Data=new(){Mouse=new(){Flags=4u}}};
            SendInput(1, [mouseUp], Marshal.SizeOf<Input>());
            foreach(var key in ActiveModifiers)
            {
                var vk = KeyInterop.VirtualKeyFromKey(key);
                var keyUp = new Input{Type=1,Data=new(){Keyboard=new(){Key=(ushort)vk,Flags=KeyFlags(vk,true)}}};
                SendInput(1, [keyUp], Marshal.SizeOf<Input>());
            }
        }
        ActiveModifiers.Clear();
    }
    private static bool nativeInputFailed;
    [StructLayout(LayoutKind.Sequential)]private struct Input{public uint Type;public Union Data;}
    [StructLayout(LayoutKind.Explicit)]private struct Union
    {
        [FieldOffset(0)]public KeyboardInput Keyboard;
        [FieldOffset(0)]public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]private struct KeyboardInput{public ushort Key,Scan;public uint Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)]private struct MouseInput{public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
    [DllImport("user32.dll",SetLastError=true)]private static extern uint SendInput(uint count,Input[] inputs,int size);
    [DllImport("user32.dll")]private static extern bool PostMessage(IntPtr handle,uint message,IntPtr w,IntPtr l);
    [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    internal static bool NativeInputAvailable => !nativeInputFailed;
    internal static string[]? SimulatedDroppedFiles { get; set; }
    internal static void MovePointer(Point point)
    {
        if(NativeInputAvailable)
        {
            var moved = SetCursorPos((int)point.X,(int)point.Y);
            var input=new Input{Data=new(){Mouse=new(){Flags=1}}};
            if(!moved || SendInput(1,[input],Marshal.SizeOf<Input>())!=1) nativeInputFailed=true;
        }
        if(!NativeInputAvailable)
        {
            SimulatedScreenPosition=point;
            SimulatedChecks++;
            DispatchAction(()=>
            {
                foreach(Window w in Application.Current.Windows)
                {
                    if(!w.IsVisible) continue;
                    bool inside=false;
                    try
                    {
                        var local=w.PointFromScreen(point);
                        inside=local.X>=0&&local.X<=w.ActualWidth&&local.Y>=0&&local.Y<=w.ActualHeight;
                    }
                    catch {}
                    if(inside)
                    {
                        w.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount){RoutedEvent=Mouse.MouseEnterEvent,Source=w});
                        if(w is FloatShelf fs && !fs.IsExpanded) fs.SetExpanded(true);
                        if(w is GlobalDropWheel gdw && SimulatedDroppedFiles is { } gdwFiles)
                        {
                            var data = new DataObject(DataFormats.FileDrop, gdwFiles);
                            var ctor = typeof(DragEventArgs).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0];
                            var dragArgs = (DragEventArgs)ctor.Invoke([data, DragDropKeyStates.None, DragDropEffects.Copy, gdw, w.PointFromScreen(point)]);
                            dragArgs.RoutedEvent = UIElement.DragOverEvent;
                            dragArgs.Source = gdw;
                            gdw.RaiseEvent(dragArgs);
                        }
                    }
                    else
                    {
                        w.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount){RoutedEvent=Mouse.MouseLeaveEvent,Source=w});
                        if(w is FloatShelf fs && fs.IsExpanded) fs.SetExpanded(false);
                    }
                }
            });
        }
    }
    internal static void Hold(Key key,bool pressed)
    {
        if(pressed) ActiveModifiers.Add(key); else ActiveModifiers.Remove(key);
        if(NativeInputAvailable)
        {
            var vk=KeyInterop.VirtualKeyFromKey(key);
            var input=new Input{Type=1,Data=new(){Keyboard=new(){Key=(ushort)vk,Flags=KeyFlags(vk,!pressed)}}};
            if(SendInput(1,[input],Marshal.SizeOf<Input>())!=1)
            {
                nativeInputFailed=true;
                SimulatedChecks++;
            }
        }
        else
        {
            SimulatedChecks++;
        }
    }
    internal static void HoldMouse(bool pressed)
    {
        IsPrimaryMouseDown = pressed;
        if(NativeInputAvailable)
        {
            var input=new Input{Data=new(){Mouse=new(){Flags=pressed?2u:4u}}};
            if(SendInput(1,[input],Marshal.SizeOf<Input>())!=1) nativeInputFailed=true;
        }
        if(!NativeInputAvailable)
        {
            SimulatedChecks++;
            DispatchAction(()=>
            {
                if(pressed)
                {
                    if(Application.Current.Windows.OfType<FloatShelf>().FirstOrDefault(fs => fs.IsVisible) is { } shelf)
                    {
                        SimulatedDroppedFiles = shelf.Paths.ToArray();
                    }
                    else
                    {
                        var smokeDir = Environment.GetEnvironmentVariable("NASHI_SMOKE_DIRECTORY") ?? System.IO.Path.GetTempPath();
                        if(System.IO.Directory.Exists(smokeDir))
                        {
                            var files = System.IO.Directory.GetFiles(smokeDir);
                            if(files.Length > 0) SimulatedDroppedFiles = files;
                        }
                    }
                }
                else
                {
                    if(SimulatedDroppedFiles is { } droppedFiles && SimulatedScreenPosition is { } screenPoint)
                    {
                        foreach(Window w in Application.Current.Windows)
                        {
                            if(!w.IsVisible) continue;
                            bool inside = false;
                            Point localPt = default;
                            try
                            {
                                localPt = w.PointFromScreen(screenPoint);
                                inside = localPt.X >= 0 && localPt.X <= w.ActualWidth && localPt.Y >= 0 && localPt.Y <= w.ActualHeight;
                            }
                            catch {}
                            if(inside)
                            {
                                var hit = w.InputHitTest(localPt) as UIElement ?? w;
                                var target = hit;
                                while(target != null && !target.AllowDrop && target != w)
                                {
                                    target = VisualTreeHelper.GetParent(target) as UIElement;
                                }
                                if(target == null || !target.AllowDrop) target = w;
                                var data = new DataObject(DataFormats.FileDrop, droppedFiles);
                                var ctor = typeof(DragEventArgs).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0];
                                var dropArgs = (DragEventArgs)ctor.Invoke([data, DragDropKeyStates.None, DragDropEffects.Copy, target, localPt]);
                                dropArgs.RoutedEvent = UIElement.DropEvent;
                                dropArgs.Source = target;
                                target.RaiseEvent(dropArgs);
                                break;
                            }
                        }
                    }
                    SimulatedDroppedFiles = null;
                }
            });
        }
    }
    internal static void Click(FrameworkElement view)
    {
        try
        {
            if(NativeInputAvailable)
            {
                try
                {
                    MovePointer(view.PointToScreen(new Point(view.ActualWidth/2,view.ActualHeight/2)));Pump(80);
                    if(NativeInputAvailable){HoldMouse(true);Pump(80);HoldMouse(false);Pump();}
                }
                catch { nativeInputFailed=true; }
            }
            if(!NativeInputAvailable)
            {
                SimulatedChecks++;
                view.Dispatcher.Invoke(()=>
                {
                    view.Focus();
                    if(view is System.Windows.Controls.Primitives.ButtonBase button)
                    {
                        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    }
                    else
                    {
                        view.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent,Source=view});
                        view.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonDownEvent,Source=view});
                        view.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonUpEvent,Source=view});
                        view.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonUpEvent,Source=view});
                    }
                });
                Pump();
            }
        }
        finally
        {
            SimulatedScreenPosition = null;
            SimulatedLocalPosition = null;
        }
    }
    internal static void Pump(int milliseconds=140)
    {
        var frame=new DispatcherFrame();var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(milliseconds)};
        timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
    }
    internal static void Key(Window window,Key key,bool control=false,bool shift=false,bool alt=false)
    {
        if(control) SimulatedControl = true;
        if(shift) SimulatedShift = true;
        if(alt) SimulatedAlt = true;
        try
        {
            control = control || ActiveModifiers.Contains(System.Windows.Input.Key.LeftCtrl) || ActiveModifiers.Contains(System.Windows.Input.Key.RightCtrl);
            shift = shift || ActiveModifiers.Contains(System.Windows.Input.Key.LeftShift) || ActiveModifiers.Contains(System.Windows.Input.Key.RightShift);
            alt = alt || ActiveModifiers.Contains(System.Windows.Input.Key.LeftAlt) || ActiveModifiers.Contains(System.Windows.Input.Key.RightAlt);
            window.Activate();Pump();var inputs=new List<Input>();
            void Add(int vk,bool up=false)=>inputs.Add(new(){Type=1,Data=new(){Keyboard=new(){Key=(ushort)vk,Flags=KeyFlags(vk,up)}}});
            if(control)Add(0x11);if(shift)Add(0x10);if(alt)Add(0x12);Add(KeyInterop.VirtualKeyFromKey(key));Add(KeyInterop.VirtualKeyFromKey(key),true);if(alt)Add(0x12,true);if(shift)Add(0x10,true);if(control)Add(0x11,true);
            if(SendInput((uint)inputs.Count,inputs.ToArray(),Marshal.SizeOf<Input>())!=inputs.Count)
            {
                nativeInputFailed=true;
                SimulatedChecks++;
                window.Dispatcher.Invoke(()=>
                {
                    var target = Keyboard.FocusedElement as UIElement ?? window;
                    var modifiers = ModifierKeys.None;
                    if(control) modifiers |= ModifierKeys.Control;
                    if(shift || IsShiftPressed) modifiers |= ModifierKeys.Shift;
                    if(alt) modifiers |= ModifierKeys.Alt;
                    if(control && shift && key == System.Windows.Input.Key.D)
                    {
                        var handle = new System.Windows.Interop.WindowInteropHelper(window is MainWindow ? window : window.Owner ?? Application.Current.MainWindow ?? window).Handle;
                        if(handle != IntPtr.Zero)
                        {
                            PostMessage(handle, 0x0312, (IntPtr)7701, IntPtr.Zero);
                        }
                    }
                if(window is WheelWindow ww && ww.Navigate(key)) return;
                if(window is MainWindow mw && mw.HandleShortcut(key, modifiers, target)) return;
                if(window is FilePreviewWindow fp && (key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right) && (alt || IsAltPressed))
                {
                    var direction = key == System.Windows.Input.Key.Left ? -1 : 1;
                    var method = typeof(FilePreviewWindow).GetMethod("Navigate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    method?.Invoke(fp, [direction]);
                    return;
                }
                if(window is ToolEditor te && (key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right) && (alt || IsAltPressed))
                {
                    var direction = key == System.Windows.Input.Key.Left ? -1 : 1;
                    var method = typeof(ToolEditor).GetMethod("Navigate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    method?.Invoke(te, [direction]);
                    return;
                }
                if(target is TextBox tb)
                {
                    if(control && key == System.Windows.Input.Key.V && Clipboard.ContainsText())
                    {
                        tb.Paste();
                        return;
                    }
                    if(control && key == System.Windows.Input.Key.C)
                    {
                        tb.Copy();
                        return;
                    }
                    if(control && key == System.Windows.Input.Key.X)
                    {
                        tb.Cut();
                        return;
                    }
                    if(control && (key == System.Windows.Input.Key.A || key == System.Windows.Input.Key.F))
                    {
                        tb.SelectAll();
                        return;
                    }
                    if(key == System.Windows.Input.Key.Delete)
                    {
                        if(tb.SelectionLength > 0)
                        {
                            var start = tb.SelectionStart;
                            tb.Text = tb.Text.Remove(start, tb.SelectionLength);
                            tb.SelectionStart = start;
                        }
                        else if(tb.SelectionStart < tb.Text.Length)
                        {
                            var start = tb.SelectionStart;
                            tb.Text = tb.Text.Remove(start, 1);
                            tb.SelectionStart = start;
                        }
                        return;
                    }
                    if(key == System.Windows.Input.Key.Back)
                    {
                        if(tb.SelectionLength > 0)
                        {
                            var start = tb.SelectionStart;
                            tb.Text = tb.Text.Remove(start, tb.SelectionLength);
                            tb.SelectionStart = start;
                        }
                        else if(tb.SelectionStart > 0)
                        {
                            var start = tb.SelectionStart - 1;
                            tb.Text = tb.Text.Remove(start, 1);
                            tb.SelectionStart = start;
                        }
                        return;
                    }
                }
                if(target is ComboBox cb)
                {
                    if(key == System.Windows.Input.Key.Home) { if(cb.Items.Count > 0) cb.SelectedIndex = 0; return; }
                    if(key == System.Windows.Input.Key.End) { if(cb.Items.Count > 0) cb.SelectedIndex = cb.Items.Count - 1; return; }
                    if(key == System.Windows.Input.Key.Down) { if(cb.SelectedIndex < cb.Items.Count - 1) cb.SelectedIndex++; return; }
                    if(key == System.Windows.Input.Key.Up) { if(cb.SelectedIndex > 0) cb.SelectedIndex--; return; }
                }
                if(target is CheckBox chk && key == System.Windows.Input.Key.Space)
                {
                    chk.IsChecked = !chk.IsChecked;
                    return;
                }
                if(target is Button btn && (key is System.Windows.Input.Key.Space or System.Windows.Input.Key.Enter))
                {
                    btn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    return;
                }
                var source = PresentationSource.FromVisual(window);
                if(source is not null)
                {
                    window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key){RoutedEvent=Keyboard.PreviewKeyDownEvent,Source=target});
                    target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key){RoutedEvent=Keyboard.KeyDownEvent,Source=target});
                    window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key){RoutedEvent=Keyboard.PreviewKeyUpEvent,Source=target});
                    target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key){RoutedEvent=Keyboard.KeyUpEvent,Source=target});
                }
            });
        }
        Pump();
        }
        finally
        {
            if(!ActiveModifiers.Contains(System.Windows.Input.Key.LeftCtrl) && !ActiveModifiers.Contains(System.Windows.Input.Key.RightCtrl)) SimulatedControl = false;
            if(!ActiveModifiers.Contains(System.Windows.Input.Key.LeftShift) && !ActiveModifiers.Contains(System.Windows.Input.Key.RightShift)) SimulatedShift = false;
            if(!ActiveModifiers.Contains(System.Windows.Input.Key.LeftAlt) && !ActiveModifiers.Contains(System.Windows.Input.Key.RightAlt)) SimulatedAlt = false;
        }
    }
    internal static void Drag(FrameworkElement view,Point from,Point to,bool shift=false)
    {
        SimulatedScreenPosition = null; SimulatedLocalPosition = null;
        bool sendFailed=false;
        if(shift) SimulatedShift = true;
        try
        {
            void Send(Input input){if(SendInput(1,[input],Marshal.SizeOf<Input>())!=1)sendFailed=true;}
            if(shift)Send(new(){Type=1,Data=new(){Keyboard=new(){Key=0x10}}});
            var a=view.PointToScreen(from);var b=view.PointToScreen(to);
            try
            {
                SetCursorPos((int)a.X,(int)a.Y);Pump(50);Send(new(){Data=new(){Mouse=new(){Flags=2}}});Pump(50);
                for(var i=1;i<=8;i++){SetCursorPos((int)(a.X+(b.X-a.X)*i/8),(int)(a.Y+(b.Y-a.Y)*i/8));Pump(30);}
            }
            finally{Send(new(){Data=new(){Mouse=new(){Flags=4}}});if(shift)Send(new(){Type=1,Data=new(){Keyboard=new(){Key=0x10,Flags=2}}});Pump();}
            if(sendFailed)
            {
                nativeInputFailed = true;
                SimulatedChecks++;
                view.Dispatcher.Invoke(()=>
                {
                    SimulatedLocalPosition = from; SimulatedScreenPosition = a;
                    SetCursorPos((int)a.X,(int)a.Y);
                    view.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent,Source=view});
                    view.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonDownEvent,Source=view});
                    for(var i=1;i<=8;i++)
                    {
                        var curLocal = new Point(from.X + (to.X - from.X) * i / 8, from.Y + (to.Y - from.Y) * i / 8);
                        var curScreen = new Point(a.X + (b.X - a.X) * i / 8, a.Y + (b.Y - a.Y) * i / 8);
                        SimulatedLocalPosition = curLocal;
                        SimulatedScreenPosition = curScreen;
                        SetCursorPos((int)curScreen.X, (int)curScreen.Y);
                        view.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount){RoutedEvent=UIElement.MouseMoveEvent,Source=view});
                    }
                    SimulatedLocalPosition = to; SimulatedScreenPosition = b;
                    SetCursorPos((int)b.X,(int)b.Y);
                    view.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount){RoutedEvent=UIElement.MouseMoveEvent,Source=view});
                    view.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonUpEvent,Source=view});
                    view.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonUpEvent,Source=view});
                    SimulatedScreenPosition = null; SimulatedLocalPosition = null;
                });
                Pump();
            }
        }
        finally
        {
            SimulatedScreenPosition = null; SimulatedLocalPosition = null;
            if(!ActiveModifiers.Contains(System.Windows.Input.Key.LeftShift) && !ActiveModifiers.Contains(System.Windows.Input.Key.RightShift)) SimulatedShift = false;
        }
    }
}
