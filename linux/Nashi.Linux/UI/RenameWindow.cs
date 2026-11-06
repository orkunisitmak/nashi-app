using Avalonia.Controls;
using Avalonia.Media;
namespace Nashi.Linux.UI;

/// Batch rename with a live preview: {name} {ext} {n} {date} {today}. The extension is kept unless
/// the pattern uses {ext}. Nothing changes until Rename, and clashes are shown before they happen.
public sealed class RenameWindow:Window
{
    private readonly IReadOnlyList<string> paths;private readonly TextBox pattern=new(){Text="{name}"};private readonly NumericUpDown start=Ui.Number(1,0,1_000_000);
    private readonly ComboBox casing=Ui.Choice([("keep","Keep"),("lower","lowercase"),("upper","UPPERCASE"),("title","Title Case")],"keep");
    private readonly StackPanel preview=new(){Spacing=2};private readonly TextBlock problem=new(){Foreground=Brushes.IndianRed,TextWrapping=TextWrapping.Wrap};
    public IReadOnlyList<(string From,string To)> Plan{get;private set;}=[];
    /// For tests and scripted use: set the pattern and get whether the plan is valid.
    internal bool Try(string text){pattern.Text=text;return Build();}
    public RenameWindow(IReadOnlyList<string> paths)
    {
        this.paths=paths;Title=L10n.T("Rename")+" · Nashi";Width=640;Height=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var ok=Ui.Primary("Rename",()=>{if(Build())Close(true);});ok.IsDefault=true;var cancel=Ui.Button("Cancel",()=>Close(false));cancel.IsCancel=true;
        pattern.TextChanged+=(_,_)=>Build();start.ValueChanged+=(_,_)=>Build();casing.SelectionChanged+=(_,_)=>Build();
        var tokens=Ui.Wrap(new[]{"{name}","{n}","{date}","{today}","{ext}"}.Select(t=>(Control)Ui.Button(t,()=>{pattern.Text+=t;pattern.CaretIndex=pattern.Text?.Length??0;},"chip")).ToArray());
        Content=new DockPanel{Margin=new(24),Children=
        {
            Ui.Stack(6,Ui.Title("Rename",24),Ui.Text("Files are renamed where they are. Nothing changes until you choose Rename.",12,"muted"),
                Ui.Field("Pattern",pattern),tokens,Ui.Wrap(Ui.Field("First number",start),new Control{Width=16},Ui.Field("Letters",casing)),problem).Also(s=>DockPanel.SetDock(s,Avalonia.Controls.Dock.Top)),
            Ui.Row(0,ok,cancel).Also(r=>{DockPanel.SetDock(r,Avalonia.Controls.Dock.Bottom);r.Margin=new(0,10,0,0);}),
            new Border{Classes={"inset"},Margin=new(0,8,0,0),Child=new ScrollViewer{Content=preview}}
        }};
        Build();
    }
    private bool Build()
    {
        preview.Children.Clear();problem.Text="";var plan=new List<(string,string)>();var digits=Math.Max(2,(paths.Count+(int)Ui.Read(start)).ToString().Length);
        for(var i=0;i<paths.Count;i++)
        {
            var path=paths[i];var stem=Path.GetFileNameWithoutExtension(path);var ext=Path.GetExtension(path).TrimStart('.');
            var text=(pattern.Text??"").Replace("{name}",stem).Replace("{ext}",ext).Replace("{n}",((int)Ui.Read(start)+i).ToString().PadLeft(digits,'0'))
                .Replace("{date}",File.GetLastWriteTime(path).ToString("yyyy-MM-dd")).Replace("{today}",DateTime.Now.ToString("yyyy-MM-dd")).Trim();
            text=Ui.Value(casing) switch{"lower"=>text.ToLowerInvariant(),"upper"=>text.ToUpperInvariant(),"title"=>System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLower()),_=>text};
            var name=(pattern.Text??"").Contains("{ext}")||ext.Length==0?text:text+"."+ext;
            plan.Add((path,Path.Combine(Path.GetDirectoryName(path)!,name)));
            preview.Children.Add(Ui.Text(Path.GetFileName(path)+"   →   "+name,12,null,false));
        }
        string? error=null;
        if(plan.Any(p=>Path.GetFileName(p.Item2).Length==0||Path.GetFileName(p.Item2).IndexOfAny(['/','\0'])>=0||Path.GetFileName(p.Item2) is "." or ".."))error="Every new name needs some text.";
        else if(plan.GroupBy(p=>p.Item2).Any(g=>g.Count()>1))error="Two files would get the same name. Add {n} to the pattern.";
        else if(plan.Any(p=>p.Item1!=p.Item2&&(File.Exists(p.Item2)||Directory.Exists(p.Item2))&&!plan.Any(q=>q.Item1==p.Item2)))error="A file with one of these names already exists.";
        problem.Text=error is null?"":L10n.T(error);Plan=plan.Where(p=>p.Item1!=p.Item2).ToList();return error is null;
    }
    /// Renames in two steps (through temporary names) so swapping names inside the batch works.
    public static IReadOnlyList<string> Apply(IReadOnlyList<(string From,string To)> plan)
    {
        var staged=new List<(string Temp,string To)>();
        foreach(var (from,to) in plan){var temp=Path.Combine(Path.GetDirectoryName(from)!,".nashi-rename-"+Guid.NewGuid().ToString("N"));Move(from,temp);staged.Add((temp,to));}
        foreach(var (temp,to) in staged)Move(temp,to);
        return staged.Select(s=>s.To).ToList();
    }
    private static void Move(string from,string to){if(Directory.Exists(from))Directory.Move(from,to);else File.Move(from,to,false);}
}
