using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nashi.Core;
namespace Nashi.Windows;

/// Records a drag shortcut from Settings; it only reads input sent to that window.
internal sealed class DragShortcutRecorder:Button
{
    private readonly Func<string> current;private readonly Action<string> save;private Window? window;private ModifierKeys held;
    internal bool Recording=>window is not null;
    internal DragShortcutRecorder(Func<string> current,Action<string> save)
    {
        this.current=current;this.save=save;Refresh();Click+=(_,_)=>Start();PreviewKeyDown+=KeyPressed;PreviewKeyUp+=KeyReleased;LostKeyboardFocus+=(_,_)=>Stop();Unloaded+=(_,_)=>Stop();
    }
    private void Refresh()=>Content=DragShortcut.Title(current(),L10n.Language);
    private void Start()
    {
        if(Recording||Window.GetWindow(this) is not {} owner)return;
        window=owner;held=ModifierKeys.None;Content=L10n.Text("Press keys or a mouse button…");Focus();owner.PreviewMouseDown+=MousePressed;
    }
    private void Stop(){if(window is null)return;window.PreviewMouseDown-=MousePressed;window=null;Refresh();}
    private void Record(string shortcut){Stop();save(shortcut);Refresh();}
    private static ModifierKeys Modifier(Key key)=>key switch{Key.LeftCtrl or Key.RightCtrl=>ModifierKeys.Control,Key.LeftShift or Key.RightShift=>ModifierKeys.Shift,Key.LeftAlt or Key.RightAlt=>ModifierKeys.Alt,_=>ModifierKeys.None};
    private static string Chord(ModifierKeys keys)=>string.Join('+',new[]{(ModifierKeys.Control,"Ctrl"),(ModifierKeys.Shift,"Shift"),(ModifierKeys.Alt,"Alt")}.Where(modifier=>keys.HasFlag(modifier.Item1)).Select(modifier=>modifier.Item2));
    private void KeyPressed(object sender,KeyEventArgs e)
    {
        if(!Recording)return;e.Handled=true;var key=e.Key==Key.System?e.SystemKey:e.Key;
        if(key==Key.Escape)Stop();
        else if(Modifier(key)!=ModifierKeys.None)held|=Keyboard.Modifiers|Modifier(key);
        else if(!e.IsRepeat&&key is not(Key.None or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed))Record(DragShortcut.Compose(Chord(Keyboard.Modifiers),key.ToString()));
    }
    // A modifier-only shortcut is complete once every key of the chord has been released.
    private void KeyReleased(object sender,KeyEventArgs e)
    {
        if(!Recording)return;e.Handled=true;var key=e.Key==Key.System?e.SystemKey:e.Key;
        if(Modifier(key)!=ModifierKeys.None&&(Keyboard.Modifiers&~Modifier(key))==ModifierKeys.None&&held!=ModifierKeys.None)Record(Chord(held));
    }
    private void MousePressed(object sender,MouseButtonEventArgs e)
    {
        var button=e.ChangedButton switch{MouseButton.Right=>"Right Click",MouseButton.Middle=>"Middle Click",MouseButton.XButton1=>"Mouse 4",MouseButton.XButton2=>"Mouse 5",_=>null};
        // The primary button cancels; over this button it must not start recording again.
        if(button is null){e.Handled=IsMouseOver;Stop();return;}
        e.Handled=true;Record(DragShortcut.Compose(Chord(Keyboard.Modifiers),button));
    }
}
