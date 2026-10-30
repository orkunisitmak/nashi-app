using Nashi.Core;
internal static class WheelCustomizationChecks
{
    internal static void Run(Action<bool,string> check,string root)
    {
        string[] Ids(params string[] ids)=>ids;
        check(WheelCustomization.Visible(Ids("a","b","c","d"),new HashSet<string>{"b","d"},x=>x).SequenceEqual(Ids("a","c")),"Hidden wheel items must be removed in order");
        check(WheelCustomization.Visible(Ids("a","b"),new HashSet<string>{"zzz"},x=>x).SequenceEqual(Ids("a","b")),"Unknown hidden ids must be ignored");
        check(WheelCustomization.Visible(Ids("a"),new HashSet<string>(),x=>x).SequenceEqual(Ids("a")),"An empty hidden list must show everything");
        check(WheelCustomization.Visible(Ids("a","b"),new HashSet<string>{"a","b"},x=>x).Count==0,"Convert and Tools wheels may be emptied");
        check(WheelCustomization.Visible(Ids("a","b"),new HashSet<string>{"a","b"},x=>x,keepOne:true).SequenceEqual(Ids("a","b")),"The Actions wheel must never come back empty");
        check(WheelCustomization.ConvertSamples.SelectMany(s=>FeatureCatalog.Formats(s.Inputs)).ToHashSet().SetEquals(FeatureCatalog.Conversions.Values.SelectMany(v=>v)),"Wheel preview samples must show every conversion target");
        check(WheelCustomization.ToolSamples.SelectMany(s=>FeatureCatalog.Tools(s.Inputs)).ToHashSet().SetEquals(FeatureCatalog.ToolNames.Keys),"Wheel preview samples must show every tool");
        var formats=WheelCustomization.FormatGroups.SelectMany(g=>g.Formats).ToArray();var tools=WheelCustomization.ToolGroups.SelectMany(g=>g.Tools).ToArray();
        check(formats.Length==formats.Distinct().Count()&&formats.ToHashSet().SetEquals(WheelCustomization.ConvertTargets),"Wheel format groups must list each target once");
        check(tools.Length==27&&tools.Distinct().Count()==27&&tools.ToHashSet().SetEquals(FeatureCatalog.ToolNames.Keys),"Wheel tool groups must list each tool once");
        check(WheelCustomization.FirstConvertSample("jpg")?.Id=="png"&&WheelCustomization.FirstConvertSample("html")?.Id=="docx"&&WheelCustomization.FirstConvertSample("nope") is null,"Preview must switch to a sample showing the format");
        check(WheelCustomization.FirstToolSample("mergePDF")?.Id=="pdf+pdf"&&WheelCustomization.FirstToolSample("extractArchive")?.Id=="zip","Preview must switch to a sample showing the tool");
        check(WheelCustomization.ToolSamples.First(s=>s.Id=="png+png").Title=="2 × PNG"&&WheelCustomization.ConvertSamples[0].Title=="PNG","Wheel sample titles changed");
        check(WheelCustomization.ToolId("crop")=="cropImage"&&WheelCustomization.ToolId("annotate")=="annotateImage"&&WheelCustomization.ToolId("mergePDFs")=="mergePDF"&&WheelCustomization.ToolId("compress")=="compress","In-app wheel tool ids must map back to the catalog ids Settings stores");
        check(FeatureCatalog.ToolNames.Keys.All(id=>WheelCustomization.ToolId(WheelCustomization.EditorToolId(id))==id),"Every renamed in-app tool id must map back to its catalog id");
        check(WheelCustomization.Actions.SequenceEqual(Ids("share","float","copy","move","copyPath","terminal")),"Windows file actions changed");
        foreach(var source in new[]{"Wheels","Only what you need.","Hide what you never use. The wheels share the space among the rest.","Click an item to hide or show it.","Preview with","At least one action stays visible.","Show all"})check(AppLocalization.Text(source,"tr")!=source,"Wheel settings copy is not translated: "+source);
        check(AppLocalization.Text("4 of 6 visible","tr")=="6 öğeden 4 tanesi görünüyor","Wheel settings counter is not translated");
        check(AppLocalization.Text("Opens with Ctrl+Shift while dragging files.","tr")=="Dosya sürüklerken Ctrl+Shift ile açılır.","Wheel shortcut hint is not translated");
        var directory=Path.Combine(root,"Wheel preferences");Directory.CreateDirectory(directory);
        try
        {
            var file=Path.Combine(directory,"preferences.json");
            File.WriteAllText(file,"{\"WheelScale\":1.2}");var old=new PreferencesStore(directory);
            check(old.RecoveryNotice is null&&old.Value.WheelScale==1.2&&old.Value.HiddenWheelFormats.Length==0&&old.Value.HiddenWheelTools.Length==0&&old.Value.HiddenWheelActions.Length==0,"Preferences from before wheel customization must load unchanged");
            File.WriteAllText(file,"{\"HiddenWheelFormats\":null,\"HiddenWheelTools\":[\"notATool\"]}");var odd=new PreferencesStore(directory);
            check(odd.RecoveryNotice is null&&odd.Value.HiddenWheelFormats.Length==0&&odd.Value.HiddenWheelTools.SequenceEqual(Ids("notATool")),"Null or unknown hidden wheel ids must not reset preferences");
            odd.Save(odd.Value with{HiddenWheelFormats=["gif","pdf"]});check(new PreferencesStore(directory).Value.HiddenWheelFormats.SequenceEqual(Ids("gif","pdf")),"Hidden wheel formats must survive a restart");
            var tooMany=Enumerable.Range(0,201).Select(i=>"id"+i).ToArray();
            try{odd.Save(odd.Value with{HiddenWheelActions=tooMany});throw new Exception("Oversized hidden wheel list saved");}catch(ArgumentException){check(true,"");}
            try{odd.Save(odd.Value with{HiddenWheelTools=[new string('x',65)]});throw new Exception("Oversized hidden wheel id saved");}catch(ArgumentException){check(true,"");}
            File.WriteAllText(file,"{\"HiddenWheelActions\":["+string.Join(",",tooMany.Select(i=>"\""+i+"\""))+"]}");var recovered=new PreferencesStore(directory);
            check(recovered.RecoveryNotice is not null&&recovered.Value.HiddenWheelActions.Length==0,"An oversized hidden wheel list must fall back to defaults, not crash");
        }
        finally{Directory.Delete(directory,true);}
    }
}
