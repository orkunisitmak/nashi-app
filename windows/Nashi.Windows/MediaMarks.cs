using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Nashi.Windows;

/// Frame grabs and video splits are marked at the playhead, as on the Mac: an Add Frame / Add Split button,
/// the M key and removable time chips. The semicolon field stays as the editable source of truth, so typed
/// times, Reset and the existing settings code keep working unchanged.
public sealed partial class ToolEditor
{
    private const string SnapshotField="Times (seconds)",SplitField="Split points (seconds; separated by semicolons)";
    private readonly WrapPanel markChips=new(){Margin=new(4,8,4,4)};
    private string? MarkField=>tool switch{"videoSnapshots"=>SnapshotField,"splitVideo"=>SplitField,_=>null};
    private void BuildMarkControls()
    {
        if(media is null||MarkField is not {} key||!fields.TryGetValue(key,out var box))return;
        var snapshots=tool=="videoSnapshots";
        var add=MainWindow.Button(snapshots?"Add Frame":"Add Split",AddMarkAtPlayhead);add.ToolTip=snapshots?"Add frame at playhead (M)":"Add split at playhead (M)";add.HorizontalAlignment=HorizontalAlignment.Left;add.Margin=new(4,10,4,0);
        var index=options.Children.IndexOf(box);options.Children.Insert(index+1,add);options.Children.Insert(index+2,markChips);
        box.TextChanged+=(_,_)=>RenderMarks();RenderMarks();
    }
    private static double? MarkTime(string text)
    {
        if(!double.TryParse(text,NumberStyles.Float,CultureInfo.CurrentCulture,out var value)&&!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value))return null;
        return double.IsFinite(value)?value:null;
    }
    private static string[] MarkTokens(string text)=>text.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    private void AddMarkAtPlayhead()
    {
        if(media is null||MarkField is not {} key||!fields.TryGetValue(key,out var box))return;
        var at=Math.Round(Math.Clamp(player.Position.TotalSeconds,0,duration),3);
        var times=new List<double>();
        // The snapshot field starts at "0" so the tool can save without marking; the first marked frame replaces it.
        if(!(tool=="videoSnapshots"&&box.Text==defaults[key]))foreach(var token in MarkTokens(box.Text))if(MarkTime(token) is {} value)times.Add(value);
        if(tool=="splitVideo"&&(at<MinRange||duration-at<MinRange||times.Any(t=>Math.Abs(t-at)<MinRange))){time.Text=L10n.Text("Split points must be at least 0.05 seconds apart and from either end.");return;}
        if(times.Any(t=>Math.Abs(t-at)<.001))return;
        times.Add(at);times.Sort();box.Text=string.Join(";",times.Select(Number));
    }
    private void RenderMarks()
    {
        markChips.Children.Clear();
        if(MarkField is not {} key||!fields.TryGetValue(key,out var box))return;
        var tokens=MarkTokens(box.Text);
        for(var index=0;index<tokens.Length;index++)
        {
            if(MarkTime(tokens[index]) is not {} seconds)continue;var position=index;
            var chip=new StackPanel{Orientation=Orientation.Horizontal};
            var jump=new Button{Content=TimeSpan.FromSeconds(Math.Max(0,seconds)).ToString(@"mm\:ss\.fff",CultureInfo.InvariantCulture),Padding=new(8,3,8,3),ToolTip=L10n.Text("Preview position")};jump.Click+=(_,_)=>Seek(seconds);
            var remove=new Button{Content="×",Padding=new(7,3,7,3),Margin=new(2,0,0,0),ToolTip=L10n.Text("Remove")};remove.Click+=(_,_)=>box.Text=string.Join(";",tokens.Where((_,n)=>n!=position));
            chip.Children.Add(jump);chip.Children.Add(remove);
            markChips.Children.Add(new Border{Child=chip,Margin=new(0,0,8,8),CornerRadius=new(8),BorderThickness=new(1),BorderBrush=Theme.Brush("Muted")});
        }
        if(markChips.Children.Count>0)markChips.Children.Insert(0,new TextBlock{Text=L10n.Text("{0} selected").Replace("{0}",markChips.Children.Count.ToString(CultureInfo.CurrentCulture),StringComparison.Ordinal),Foreground=Theme.Brush("Muted"),VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,10,8)});
    }
    private string MediaShortcutHint=>tool switch
    {
        "splitVideo"=>"Space play/pause · ←/→ 1s · ⇧←/→ 1 frame · M add split",
        "videoSnapshots"=>"Space play/pause · ←/→ 1s · ⇧←/→ 1 frame · M add frame",
        _=>"Space play/pause · ←/→ 1s · Shift+←/→ 1 frame (video), 0.1s (audio) · I/O set range · Enter save"
    };
}
