using System.Windows.Input;
using Nashi.Core;
namespace Nashi.Windows;

/// Nashi's keyboard commands and the keys they answer to. Each command has a default; a key or
/// combination chosen in Settings › Shortcuts replaces it (stored as text such as "Ctrl+Shift+D").
internal static class Shortcuts
{
    internal sealed record Command(string Id,string Title,string Hint,string Default);
    internal static readonly Command[] All=
    [
        new("tools","Open tools","The wheel of tools for the selected files.","Ctrl+Space"),
        new("open","Choose files","Adds files to the desk.","Ctrl+O"),
        new("paste","Paste","Files, text, an image or a link from the clipboard.","Ctrl+V"),
        new("home","Home","Go to the desk.","Ctrl+D1"),
        new("library","Library","Go to Library and its spaces.","Ctrl+D2"),
        new("find","Find in Library","Search kept files and spaces.","Ctrl+F"),
        new("download","Use a copied download link","Opens the download panel with the link.","Ctrl+Shift+D"),
        new("clear","Clear desk","Starts a new space; the current one waits in Library.",""),
        new("settings","Settings","Opens this window.","Ctrl+OemComma"),
        new("updates","Check for updates","Looks for a newer Nashi.","Ctrl+U"),
    ];
    internal static string Gesture(Preferences preferences,string id)=>preferences.Shortcuts.TryGetValue(id,out var chosen)?chosen:All.FirstOrDefault(c=>c.Id==id)?.Default??"";
    internal static bool Matches(Preferences preferences,string id,Key key,ModifierKeys modifiers)=>Parse(Gesture(preferences,id)) is {} gesture&&gesture.Key==key&&gesture.Modifiers==modifiers;
    /// "Ctrl+Shift+D" → (D, Control|Shift); empty or unknown text gives null.
    internal static (Key Key,ModifierKeys Modifiers)? Parse(string text)
    {
        if(string.IsNullOrWhiteSpace(text))return null;var parts=text.Split('+');var modifiers=ModifierKeys.None;
        foreach(var part in parts[..^1])modifiers|=part switch{"Ctrl"=>ModifierKeys.Control,"Shift"=>ModifierKeys.Shift,"Alt"=>ModifierKeys.Alt,"Win"=>ModifierKeys.Windows,_=>ModifierKeys.None};
        return Enum.TryParse<Key>(parts[^1],out var key)&&key!=Key.None?(key,modifiers):null;
    }
    internal static string Format(Key key,ModifierKeys modifiers)
    {
        var parts=new List<string>();if(modifiers.HasFlag(ModifierKeys.Control))parts.Add("Ctrl");if(modifiers.HasFlag(ModifierKeys.Shift))parts.Add("Shift");if(modifiers.HasFlag(ModifierKeys.Alt))parts.Add("Alt");if(modifiers.HasFlag(ModifierKeys.Windows))parts.Add("Win");
        parts.Add(key.ToString());return string.Join("+",parts);
    }
    /// The same text as people read it: "Ctrl+1", "Ctrl+,", "Backspace".
    internal static string Display(string text)
    {
        if(Parse(text) is not {} gesture)return L10n.Text("Not set");var (key,modifiers)=gesture;
        var name=key switch
        {
            >=Key.D0 and <=Key.D9=>((int)(key-Key.D0)).ToString(),>=Key.NumPad0 and <=Key.NumPad9=>"Num "+(int)(key-Key.NumPad0),
            Key.OemComma=>",",Key.OemPeriod=>".",Key.OemMinus=>"-",Key.OemPlus=>"+",Key.OemQuestion=>"/",Key.OemSemicolon=>";",Key.OemQuotes=>"'",Key.OemOpenBrackets=>"[",Key.OemCloseBrackets=>"]",Key.OemPipe=>"\\",Key.OemTilde=>"`",
            Key.Back=>"Backspace",Key.Return=>"Enter",Key.Escape=>"Esc",Key.Prior=>"Page Up",Key.Next=>"Page Down",Key.Left=>"←",Key.Right=>"→",Key.Up=>"↑",Key.Down=>"↓",
            _=>key.ToString()
        };
        var shown=Format(key,modifiers);return string.Join("+",shown.Split('+')[..^1].Append(name));
    }
    internal static bool IsModifier(Key key)=>key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;
    /// A shortcut needs a modifier (or a function key), so plain typing never triggers one.
    internal static bool Allowed(Key key,ModifierKeys modifiers)=>!IsModifier(key)&&key!=Key.None&&(key is >=Key.F1 and <=Key.F24||(modifiers&~ModifierKeys.Shift)!=ModifierKeys.None);
}
