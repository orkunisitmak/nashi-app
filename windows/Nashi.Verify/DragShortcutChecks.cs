using Nashi.Core;
internal static class DragShortcutChecks
{
    internal static void Run(Action<bool,string> check,string root)
    {
        foreach(var shortcut in new[]{"Ctrl","Shift+Alt","Ctrl+Shift+Alt","Mouse 4","Right Click","Ctrl+Mouse 5","Alt+F","Space","D1"})check(DragShortcut.IsValid(shortcut),"Valid drag shortcut was refused: "+shortcut);
        foreach(var shortcut in new[]{null,"","Alt+Ctrl","Ctrl+Ctrl","F+G","Mouse 4+Ctrl","Ctrl+","Escape","Ctrl+Escape","Left Click","f"})check(!DragShortcut.IsValid(shortcut),"Invalid drag shortcut was accepted: "+shortcut);
        check(DragShortcut.Chord("Ctrl+Alt+F")=="Ctrl+Alt"&&DragShortcut.Trigger("Ctrl+Alt+F")=="F"&&DragShortcut.Trigger("Ctrl+Alt") is null,"Drag shortcut parts were read incorrectly");
        check(DragShortcut.Compose("Ctrl","Mouse 4")=="Ctrl+Mouse 4"&&DragShortcut.Compose("","F")=="F"&&DragShortcut.Compose("Alt",null)=="Alt","Drag shortcut parts were joined incorrectly");
        (string,string)[] shortcuts=[("tools","Ctrl"),("convert","Mouse 4"),("actions","Ctrl+Mouse 5")];
        string? Match(string chord,string? held)=>DragShortcut.Match(chord,trigger=>trigger==held,shortcuts);
        check(Match("Ctrl",null)=="tools"&&Match("",null) is null&&Match("Shift",null) is null,"Plain modifier shortcut matched incorrectly");
        check(Match("","Mouse 4")=="convert"&&Match("Ctrl","Mouse 4")=="tools","Mouse shortcut did not need its exact modifiers");
        check(Match("Ctrl","Mouse 5")=="actions","A held trigger did not beat its plain modifier shortcut");
        check(DragShortcut.Title("Ctrl+D1","en")=="Ctrl+1"&&DragShortcut.Title("Alt+Home","tr")=="Alt+Home","Key names were shown incorrectly");
        check(DragShortcut.Title("Right Click","tr")=="Sağ tık"&&DragShortcut.Title("Ctrl+Mouse 4","tr")=="Ctrl+Fare 4","Mouse button names were not translated");
        var directory=Path.Combine(root,"Drag shortcut preferences");var store=new PreferencesStore(directory);
        store.Save(store.Value with{ToolsModifier="Ctrl+Mouse 4",ConvertModifier="F"});var saved=new PreferencesStore(directory);
        check(saved.RecoveryNotice is null&&saved.Value.ToolsModifier=="Ctrl+Mouse 4"&&saved.Value.ConvertModifier=="F","Drag shortcuts did not survive a restart");
        var refused=false;try{store.Save(store.Value with{ActionsModifier="F"});}catch(ArgumentException){refused=true;}
        check(refused&&store.Value.ActionsModifier=="Alt","A shortcut used by another wheel was accepted");
        var legacy=Path.Combine(root,"Legacy drag shortcuts");Directory.CreateDirectory(legacy);File.WriteAllText(Path.Combine(legacy,"preferences.json"),"{\"ToolsModifier\":\"Ctrl+Alt\",\"ConvertModifier\":\"Shift+Alt\",\"ActionsModifier\":\"Ctrl+Shift\"}");
        var upgraded=new PreferencesStore(legacy);check(upgraded.RecoveryNotice is null&&upgraded.Value.ToolsModifier=="Ctrl+Alt","Existing drag shortcuts stopped loading");
    }
}
