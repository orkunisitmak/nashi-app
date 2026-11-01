using System.Text.RegularExpressions;
namespace Nashi.Core;

/// A drag shortcut such as "Ctrl+Shift", "Alt+Mouse 4" or "F": modifiers, then at most one key or mouse button.
public static class DragShortcut
{
    private static readonly string[] modifiers=["Ctrl","Shift","Alt"];
    private static readonly string[] mouseButtons=["Right Click","Middle Click","Mouse 4","Mouse 5"];
    public static string Chord(string shortcut){var parts=shortcut.Split('+');return string.Join('+',modifiers.Where(modifier=>parts.Contains(modifier)));}
    public static string? Trigger(string shortcut)=>shortcut.Split('+').FirstOrDefault(part=>!modifiers.Contains(part));
    public static string Compose(string chord,string? trigger)=>string.Join('+',new[]{chord,trigger}.Where(part=>!string.IsNullOrEmpty(part)));
    public static bool IsValid(string? shortcut)
    {
        if(string.IsNullOrEmpty(shortcut)||Compose(Chord(shortcut),Trigger(shortcut))!=shortcut)return false;
        return Trigger(shortcut) is not {} trigger||mouseButtons.Contains(trigger)||trigger!="Escape"&&Regex.IsMatch(trigger,"^[A-Z][A-Za-z0-9]{0,23}$");
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
