using System.Text.RegularExpressions;
namespace Nashi.Core;

/// A drag shortcut such as "Ctrl+Shift", "Alt+Mouse 4" or "F": modifiers, then at most one key or mouse button.
public static class DragShortcut
{
    private static readonly string[] modifiers=["Ctrl","Shift","Alt"];
    private static readonly string[] mouseButtons=["Right Click","Middle Click","Mouse 4","Mouse 5"];
    /// WPF key names Windows can poll during a drag, aliases included because Key.ToString may return either one.
    public static readonly IReadOnlySet<string> Keys=Enumerable.Range('A',26).Select(letter=>((char)letter).ToString())
        .Concat(Enumerable.Range(0,10).SelectMany(digit=>new[]{"D"+digit,"NumPad"+digit})).Concat(Enumerable.Range(1,24).Select(number=>"F"+number))
        .Concat(new[]{"Multiply","Add","Separator","Subtract","Decimal","Divide","Space","Tab","Enter","Return","Back","Insert","Delete","Home","End","PageUp","Prior","PageDown","Next","Left","Up","Right","Down",
            "CapsLock","Capital","Scroll","NumLock","Pause","PrintScreen","Snapshot","Apps",
            "OemSemicolon","Oem1","OemPlus","OemComma","OemMinus","OemPeriod","OemQuestion","Oem2","OemTilde","Oem3","OemOpenBrackets","Oem4","OemPipe","Oem5","OemCloseBrackets","Oem6","OemQuotes","Oem7","Oem8","OemBackslash","Oem102"}).ToHashSet();
    public static string Chord(string shortcut){var parts=shortcut.Split('+');return string.Join('+',modifiers.Where(modifier=>parts.Contains(modifier)));}
    public static string? Trigger(string shortcut)=>shortcut.Split('+').FirstOrDefault(part=>!modifiers.Contains(part));
    public static string Compose(string chord,string? trigger)=>string.Join('+',new[]{chord,trigger}.Where(part=>!string.IsNullOrEmpty(part)));
    public static bool IsValid(string? shortcut)
    {
        if(string.IsNullOrEmpty(shortcut)||Compose(Chord(shortcut),Trigger(shortcut))!=shortcut)return false;
        return Trigger(shortcut) is not {} trigger||mouseButtons.Contains(trigger)||Keys.Contains(trigger);
    }
    /// A held trigger is the more specific match when its modifiers also form a plain shortcut.
    public static string? Match(string chord,Func<string,bool> pressed,params (string Kind,string Shortcut)[] shortcuts)
    {
        foreach(var (kind,shortcut) in shortcuts)if(Trigger(shortcut) is {} trigger&&Chord(shortcut)==chord&&pressed(trigger))return kind;
        foreach(var (kind,shortcut) in shortcuts)if(chord.Length>0&&shortcut==chord)return kind;
        return null;
    }
    public static string Title(string shortcut,string language)=>string.Join('+',shortcut.Split('+').Select(part=>mouseButtons.Contains(part)?AppLocalization.Text(part,language):Regex.IsMatch(part,"^D[0-9]$")?part[1..]:part));
}
