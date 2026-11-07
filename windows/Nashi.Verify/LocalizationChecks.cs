using System.Text.RegularExpressions;
using Nashi.Core;
internal static class LocalizationChecks
{
    internal static void Run(Action<bool,string> check,string root)
    {
        foreach(var source in FeatureCatalog.ToolNames.Values.Concat(Enum.GetValues<WindowPlacement>().Select(p=>p.Title())))check(AppLocalization.Text(source,"tr")!=source,"Missing Turkish tool/layout copy: "+source);
        check(AppLocalization.Text("Library","tr")=="Kitaplık"&&AppLocalization.Text("Language","tr")=="Dil","Turkish UI catalog not loaded");
        check(AppLocalization.Text("RESIZE IMAGE","tr")=="GÖRSELİ BOYUTLANDIR","Turkish dotted I was lost in uppercase wheel labels");
        check(AppLocalization.Text("SHOW NASHI IN FINDER","tr")=="NASHI'Yİ FINDER'DA GÖSTER"&&AppLocalization.Uppercase("Shift'e bas","tr")=="SHIFT'E BAS"&&AppLocalization.Uppercase("Shift","en")=="SHIFT","Turkish upper case dotted a Latin product or key name");
        check(AppLocalization.Matches("Crop Image","kırp","tr")&&AppLocalization.Matches("Crop Image","görsel","tr"),"Turkish search did not match localized tool names");
        var raw="Türkçe {0} $1 \\ image.png";
        check(AppLocalization.Text("Original size: "+raw,"tr")=="Orijinal boyut: "+raw,"Interpolation changed a file/user value");
        check(AppLocalization.Text("Nicely done. 12 files are ready.","tr")=="Hazır! 12 dosyan hazır.","Count interpolation failed");
        check(AppLocalization.Text("7 ways to change it","tr")=="7 farklı biçime çevir"&&AppLocalization.Text("Ctrl+Alt  Convert","tr")=="Ctrl+Alt  Dönüştür"&&AppLocalization.Text("✦  READY WHEN YOU ARE","tr")=="✦  SIRA SENDE","Windows Home copy stayed in English");
        check(AppLocalization.Text("Couldn’t finish","tr")=="İşlem tamamlanamadı"&&AppLocalization.Text("Choose at least two PDFs.","tr")=="En az iki PDF seç.","Windows dialog titles or error messages stayed in English");
        check(AppLocalization.Text("Download, verify and install Nashi 0.8.0? The app will restart. No account or token is needed.","tr")=="Nashi 0.8.0 indirilip doğrulansın ve kurulsun mu? Uygulama yeniden başlayacak. Hesap veya token gerekmez."&&AppLocalization.Text("Office could not convert this document to PDF. ","tr")=="Office bu belgeyi PDF biçimine dönüştüremedi. ","Windows dialog templates lost their inserted values");
        // Word-bound templates ("{0} files", "Convert to {0}") must not swallow prose or window titles.
        check(AppLocalization.Text("Add or change files","tr")!="Add or change dosya","Template turned prose into half-translated copy");
        check(AppLocalization.Text("3 files","tr")=="3 dosya"&&AppLocalization.Text("Convert to JPG","tr")=="JPG biçimine dönüştür","Word-bound templates stopped translating single values");
        check(AppLocalization.Text("Convert to JPG · Nashi","tr")=="JPG biçimine dönüştür · Nashi"&&AppLocalization.Text("Make it yours · Nashi","tr")==AppLocalization.Text("Make it yours","tr")+" · Nashi","Window titles were not translated before the app suffix");
        foreach(var value in new[]{"Home.png","MP4","Ctrl+Shift","⌘S","00:01.250"})check(AppLocalization.Text(value,"tr")==value,"Translation changed a format, path or shortcut");
        foreach(var (source,target) in AppLocalization.Turkish)check(Regex.Matches(source,@"\{[0-9]\}").Select(m=>m.Value).Order().SequenceEqual(Regex.Matches(target,@"\{[0-9]\}").Select(m=>m.Value).Order()),"Translation placeholders differ: "+source);
        var directory=Path.Combine(root,"Localization preferences");var store=new PreferencesStore(directory);check(store.Value.Language=="en","Existing users changed language without choosing");
        store.Save(store.Value with{Language="tr",Feedback=false,ToolsModifier="Ctrl+Alt"});var loaded=new PreferencesStore(directory).Value;
        check(loaded.Language=="tr"&&!loaded.Feedback&&loaded.ToolsModifier=="Ctrl+Alt","Language persistence changed other preferences");
        var old=Path.Combine(root,"Legacy language preferences");Directory.CreateDirectory(old);File.WriteAllText(Path.Combine(old,"preferences.json"),"{\"Feedback\":false}");var legacy=new PreferencesStore(old);
        check(legacy.Value.Language=="en"&&!legacy.Value.Feedback,"Legacy preference migration changed other values");
        check(AppLocalization.Normalize("de")=="en"&&AppLocalization.Text("Home","en")=="Home"&&AppLocalization.Text("Library","de")=="Library","Unsupported language or English fallback failed");
        check(AppLocalization.Normalize("fr")=="fr"&&AppLocalization.Text("Library","fr")=="Bibliothèque"&&AppLocalization.Text("Language","fr")=="Langue","French UI catalog not loaded");
        check(AppLocalization.Text("3 files","fr")=="3 fichiers"&&AppLocalization.Text("Convert to JPG","fr")=="Convertir en JPG"&&AppLocalization.Text("Convert to JPG · Nashi","fr")=="Convertir en JPG · Nashi","French templates failed");
        check(AppLocalization.Uppercase("Bibliothèque","fr")=="BIBLIOTHÈQUE"&&AppLocalization.Matches("Crop Image","recad","fr")&&AppLocalization.Matches("Crop Image","crop","fr"),"French upper case or search failed");
        foreach(var value in new[]{"Home.png","MP4","Ctrl+Shift","⌘S","00:01.250"})check(AppLocalization.Text(value,"fr")==value,"French translation changed a format, path or shortcut");
        foreach(var (source,target) in AppLocalization.French)check(Regex.Matches(source,@"\{[0-9]\}").Select(m=>m.Value).Order().SequenceEqual(Regex.Matches(target,@"\{[0-9]\}").Select(m=>m.Value).Order()),"French translation placeholders differ: "+source);
        check(AppLocalization.French.Keys.All(AppLocalization.Turkish.ContainsKey),"French.json has keys that Turkish.json lacks");
        check(AppLocalization.Turkish.Keys.All(AppLocalization.French.ContainsKey),"Turkish.json has keys that French.json lacks");
        var frenchDirectory=Path.Combine(root,"French preferences");new PreferencesStore(frenchDirectory).Save(new Preferences{Language="fr",Feedback=false});check(new PreferencesStore(frenchDirectory).Value.Language=="fr","French setting did not persist");
    }
}
