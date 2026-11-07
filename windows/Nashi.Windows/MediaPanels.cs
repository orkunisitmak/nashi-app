using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;

// Each media editor's one decision, said plainly. The generic fields stay (hidden) as the values saved and the ones
// the tests read; the panel shows controls that say what they do, and the top bar says what will be saved.
public sealed partial class ToolEditor
{
    private bool mediaShaped,formatRequested;private StackPanel? exact;private Action<bool>? markHere;private Action? afterRanges;private int activePart=-1;
    private void ShapeMedia()
    {
        if(mediaShaped||media is null)return;mediaShaped=true;outcome.SetResourceReference(TextBlock.ForegroundProperty,"Ink");
        // The generic controls move out of sight; the ones a panel shows are taken back from there.
        exact=new StackPanel{Visibility=Visibility.Collapsed};foreach(var child in options.Children.Cast<UIElement>().Where(c=>c!=panelTitle).ToArray()){options.Children.Remove(child);exact.Children.Add(child);}
        switch(tool)
        {
            case "trimVideo" or "trimAudio":
                Heading("Start and end");Note("Drag the coral edges on the timeline, or press I and O while it plays.");Times2("Start","End");HereButtons();
                if(tool=="trimAudio"){Take(trimSilence);trimSilence.Margin=new(2,14,0,0);options.Children.Add(trimSilence);}
                Follow(()=>{try{var a=Value("Start");var b=RangeEnd();return b>a?string.Format(L10n.Text("New length {0}"),Stamp(b-a)):"";}catch(ArgumentException){return "";}},"Start","End");break;
            case "cropVideo":ShapeCrop();break;
            case "muteVideo":
                Heading("Remove sound");player.IsMuted=true;
                Note(media.HasAudio?"The picture is copied as it is, with no quality loss. Only the sound is removed.":"This video has no sound already.");Note("The preview plays without sound.");
                Outcome(L10n.Text("Silent copy · same picture quality"));break;
            case "changeVideoSpeed":
            {
                Heading("Speed");Stash("Speed");Chips("",new[]{.25,.5,.75,1,1.25,1.5,2,4}.Select(s=>(Times(s),Number(s))),()=>{try{return Number(Value("Speed",1));}catch(ArgumentException){return null;}},value=>fields["Speed"].Text=value,light=>fields["Speed"].TextChanged+=(_,_)=>light());
                var custom=fields["Speed"];Take(custom);custom.Style=Ui.Style("Unit");custom.Tag="×";custom.Margin=new(0);custom.Width=110;custom.HorizontalAlignment=HorizontalAlignment.Left;options.Children.Add(L10n.Label(new TextBlock{Text="Or a speed of your own"}));options.Children.Add(custom);
                Note("The preview plays at this speed. Voices keep their pitch.");
                Follow(()=>{try{var speed=Math.Clamp(Value("Speed",1),.05,32);return(string.Format(L10n.Text("New length {0}"),Stamp(duration/speed)),Math.Abs(speed-1)<1e-6?L10n.Text("Speed unchanged"):string.Format(L10n.Text("Now {0}"),Stamp(duration)));}catch(ArgumentException){return("","");}},"Speed");break;
            }
            case "compress":ShapeCompress();break;
            case "convert":ShapeConvert();break;
            case "splitVideo":ShapeSplit();break;
            case "videoSnapshots":ShapeSnapshots();break;
            case "redactVideo":ShapeRedactVideo();break;
            case "redactAudio":ShapeRedactAudio();break;
            case "audioToVideo":ShapeVisualization();break;
            case "normalizeAudio":ShapeLoudness();break;
            case "audioChannels":ShapeChannels();break;
            case "joinVideos":
                Heading("Order");Note("The videos play one after another in this order. Other sizes are fitted to the first video's frame.");
                Take(inputOrder);options.Children.Add(inputOrder);foreach(var item in inputOrder.Items.Cast<ListBoxItem>())if(item.Tag is string path){var row=new DockPanel();var thumb=Ui.Thumb(path,30);thumb.Margin=new(0,0,10,0);row.Children.Add(thumb);row.Children.Add(new TextBlock{Text=Path.GetFileName(path),VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0),FontSize=12.5});item.Content=row;}
                if(exact.Children.OfType<WrapPanel>().FirstOrDefault(w=>w.Children.OfType<Button>().Any(b=>b.Content as string==L10n.Text("Move up"))) is {} moves){Take(moves);options.Children.Add(moves);}
                Outcome(string.Format(L10n.Text("{0} videos joined"),inputOrder.Items.Count));break;
        }
        options.Children.Add(ResultCard());
        // Hearing or seeing the made result, where the live preview cannot show it.
        if(processedButton?.Parent is WrapPanel comparison){Take(comparison);comparison.Margin=new(-4,10,0,0);options.Children.Add(comparison);}
        options.Children.Add(exact);
    }
    private void Heading(string text){L10n.SetText(panelTitle,text);panelTitle.FontSize=13;panelTitle.Margin=new(2,0,0,4);panelTitle.SetResourceReference(TextBlock.ForegroundProperty,"Ink");}
    // The result card, recomputed whenever one of the named fields changes.
    private void Follow(Func<string> say,params string[] names)=>Follow(()=>(say(),""),names);
    private void Follow(Func<(string Text,string Detail)> say,params string[] names){void Show(){var (text,detail)=say();Outcome(text,detail);}foreach(var name in names)if(fields.TryGetValue(name,out var box))box.TextChanged+=(_,_)=>Show();afterReset+=Show;Show();}
    private static void Take(FrameworkElement element){switch(element.Parent){case Panel panel:panel.Children.Remove(element);break;case Decorator decorator:decorator.Child=null;break;case ContentControl control:control.Content=null;break;}}
    // Two fields side by side with their labels; times accept seconds or minutes:seconds.
    private void Times2(string left,string right,string unit="s",Panel? into=null)
    {
        var grid=new Grid{Margin=new(0,2,0,0)};grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=new(8)});grid.ColumnDefinitions.Add(new());
        foreach(var (name,column) in new[]{(left,0),(right,2)})
        {
            var box=fields[name];Take(box);var cell=new StackPanel();cell.Children.Add(L10n.Label(new TextBlock{Text=name,Margin=new(2,8,0,6)}));box.Style=Ui.Style("Unit");box.Tag=L10n.Text(unit);box.Margin=new(0);if(unit=="s")L10n.Bind(box,ToolTipProperty,"Seconds, or minutes:seconds (1:23.5)");cell.Children.Add(box);Grid.SetColumn(cell,column);grid.Children.Add(cell);
        }
        (into??options).Children.Add(grid);
    }
    // "Start here" and "End here" take the playhead's time (an editor can take them over, as the I and O keys do).
    private void HereButtons(Panel? into=null)
    {
        var row=new WrapPanel{Margin=new(-3,10,0,0)};
        foreach(var (title,start) in new[]{("Start here",true),("End here",false)})
        {
            var button=MainWindow.Button(title,()=>MarkHere(start));button.Style=Ui.Style("Chip");button.Margin=new(3,0,3,6);L10n.Bind(button,ToolTipProperty,start?"Start at the playhead (I)":"End at the playhead (O)");row.Children.Add(button);
        }
        (into??options).Children.Add(row);
    }
    private void MarkHere(bool start)
    {
        if(markHere is not null){markHere(start);return;}var now=Math.Round(Math.Clamp(player.Position.TotalSeconds,0,duration),2);
        try{if(start&&now<Value("End",duration))fields["Start"].Text=Number(now);else if(!start&&now>Value("Start"))fields["End"].Text=Number(now);SyncTimeline();}catch(ArgumentException error){time.Text=L10n.Text(error.Message);}
    }
    // Crop Video: the shape, the whole frame, the turned shape, and the exact size.
    private void ShapeCrop()
    {
        Heading("Crop area");Note("Drag the corners on the video, or pick a shape.");
        Take(aspectRatio);aspectRatio.Margin=new(0,8,0,0);aspectRatio.HorizontalAlignment=HorizontalAlignment.Left;options.Children.Add(aspectRatio);
        var row=new WrapPanel{Margin=new(-3,10,0,0)};foreach(var (title,icon,action) in new (string,string,Action)[]{("Whole frame","frame",()=>{if(drawing.Width>0)SetCrop(new(0,0,drawing.Width,drawing.Height));}),("Turn shape","rotL",()=>{if(aspectRatio.SelectedItem is string value&&value.Split(':') is [var a,var b])aspectRatio.SelectedItem=b+":"+a;})}){var button=MainWindow.Button(title,action);button.Style=Ui.Style("Chip");button.Margin=new(3,0,3,6);Ui.SetIcon(button,icon,13);row.Children.Add(button);}
        options.Children.Add(row);Times2("Width","Height","px");Times2("X","Y","px");
        if(drawing.Width>0&&selection is null)SetCrop(new(0,0,drawing.Width,drawing.Height));
        Follow(()=>{try{return string.Format(L10n.Text("New size {0} × {1}"),(int)Value("Width"),(int)Value("Height"));}catch(ArgumentException){return "";}},"Width","Height");
    }
    // Compress: three strengths, each with the size it should come out at, or a size of one's own.
    private void ShapeCompress()
    {
        Heading("File size");const string share="Reduction (%; 0 = auto)",target="Target size (KB; 0 = auto)";fields[share].Text=Number(50);afterReset+=()=>fields[share].Text=Number(50);
        string Size(double reduction)=>"≈ "+Weight(media!.Bytes*(1-reduction/100));
        Rows(new (string,string,Func<string>,string)[]{("Gentle","Quality stays almost the same",()=>Size(25),"25"),("Balanced","Good for sharing",()=>Size(50),"50"),("Strong","The smallest file",()=>Size(75),"75")},
            ()=>{try{return Value(target)>0?null:Number(Value(share));}catch(ArgumentException){return null;}},value=>{fields[target].Text="0";fields[share].Text=value;},light=>{fields[share].TextChanged+=(_,_)=>light();fields[target].TextChanged+=(_,_)=>light();});
        // A size of one's own, in megabytes (the saved field is in kilobytes).
        var own=new TextBox{Style=Ui.Style("Unit"),Tag="MB",Margin=new(0,6,0,0)};L10n.Bind(own,ToolTipProperty,"Exact size");System.Windows.Automation.AutomationProperties.SetName(own,L10n.Text("Exact size"));
        own.TextChanged+=(_,_)=>{if(!own.IsKeyboardFocusWithin)return;if(double.TryParse(own.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out var mb)&&mb>0)fields[target].Text=Number(Math.Round(mb*1024));else if(own.Text.Trim().Length==0)fields[target].Text="0";};
        fields[target].TextChanged+=(_,_)=>{if(own.IsKeyboardFocusWithin)return;try{var kb=Value(target);own.Text=kb>0?Number(Math.Round(kb/1024,2)):"";}catch(ArgumentException){}};
        options.Children.Add(L10n.Label(new TextBlock{Text="Or an exact size"}));options.Children.Add(own);
        Follow(()=>{try{var kb=Value(target);return(string.Format(L10n.Text("About {0}"),kb>0?Weight(kb*1024):Weight(media!.Bytes*(1-Value(share)/100))),string.Format(L10n.Text("Now {0}"),Weight(media!.Bytes)));}catch(ArgumentException){return("","");}},share,target);
    }
    // Choices as full-width rows (a name, what it means, and a value on the right); the one the source holds is lit.
    private void Rows((string Name,string Detail,Func<string> Value,string Choice)[] items,Func<string?> current,Action<string> choose,Action<Action> follow)
    {
        var list=new StackPanel{Margin=new(0,8,0,0)};var rows=new List<(Button Row,TextBlock Value,string Choice,Func<string> Read)>();
        foreach(var (name,detail,value,choice) in items)
        {
            var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var text=new StackPanel{HorizontalAlignment=HorizontalAlignment.Left};text.Children.Add(L10n.Label(Plain(name,13,FontWeights.SemiBold)));var sub=L10n.Label(Plain(detail,11.5));sub.Opacity=.7;sub.Margin=new(0,2,0,0);sub.TextWrapping=TextWrapping.Wrap;text.Children.Add(sub);grid.Children.Add(text);
            var number=Plain(value(),12.5,FontWeights.SemiBold);number.Margin=new(10,0,0,0);Grid.SetColumn(number,1);grid.Children.Add(number);
            var row=MainWindow.Button("",()=>choose(choice));row.Style=Ui.Style("Tile");Ui.SetRadius(row,14);row.Padding=new(14,9,14,9);row.Margin=new(0,0,0,6);row.Content=grid;row.HorizontalAlignment=HorizontalAlignment.Stretch;System.Windows.Automation.AutomationProperties.SetName(row,L10n.Text(name));rows.Add((row,number,choice,value));list.Children.Add(row);
        }
        void Light(){var now=current();foreach(var (row,number,choice,read) in rows){number.Text=read();if(now==choice){row.SetResourceReference(BackgroundProperty,"Solid");row.SetResourceReference(ForegroundProperty,"OnSolid");}else{row.ClearValue(BackgroundProperty);row.ClearValue(ForegroundProperty);}}}
        options.Children.Add(list);follow(Light);Light();
    }
    // Split: equal parts or chosen points; a part's number on the timeline leaves it out; separate files or one video.
    private void ShapeSplit()
    {
        const string points="Split points (seconds; separated by semicolons)",drop="Remove clips (numbers; separated by semicolons)",count="Number of clips";Heading("Split");
        double[] Points(){try{return fields[points].Text.Split(';',StringSplitOptions.RemoveEmptyEntries).Select(s=>NumericInput.ParseFinite(s.Trim())).Where(t=>t>0&&t<duration).Distinct().Order().ToArray();}catch(ArgumentException){return [];}}
        int Count(){try{return Math.Clamp((int)Value(count,2),1,200);}catch(ArgumentException){return 2;}}
        HashSet<int> Dropped(){var set=new HashSet<int>();foreach(var s in fields[drop].Text.Split(';',StringSplitOptions.RemoveEmptyEntries))if(int.TryParse(s.Trim(),NumberStyles.Integer,CultureInfo.InvariantCulture,out var n)&&n>=1)set.Add(n-1);return set;}
        double[] Bounds(){var chosen=Points();return chosen.Length>0?chosen:Enumerable.Range(1,Count()-1).Select(n=>duration*n/Count()).ToArray();}
        void Write(IEnumerable<double> times)=>fields[points].Text=string.Join("; ",times.Select(t=>Number(Math.Round(t,2))));
        var mode=new Segmented(small:true);var equal=new StackPanel();var chosenPane=new StackPanel();var number=new TextBlock{FontSize=13,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,MinWidth=74,TextAlignment=TextAlignment.Center,Margin=new(0)};var chips=new WrapPanel{Margin=new(-3,0,0,0)};var list=new StackPanel{Margin=new(0,2,0,0)};
        mode.Add("Equal parts",()=>{fields[drop].Text="";Write([]);Show();});mode.Add("At chosen points",()=>{if(Points().Length==0)Write(Bounds());Show();});mode.Margin=new(0,8,0,0);mode.HorizontalAlignment=HorizontalAlignment.Left;options.Children.Add(mode);
        // Equal parts: how many, with − and +.
        var stepper=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,10,0,0)};number.SetResourceReference(TextBlock.ForegroundProperty,"Ink");
        Button Step(string label,int delta){var button=MainWindow.Button(label,()=>{fields[drop].Text="";fields[count].Text=Number(Math.Clamp(Count()+delta,2,200));});button.Style=Ui.Style("Chip");button.MinWidth=34;button.Margin=new(0);return button;}
        stepper.Children.Add(Step("−",-1));stepper.Children.Add(number);stepper.Children.Add(Step("+",1));equal.Children.Add(stepper);
        // Chosen points: added at the playhead, dragged on the timeline, removed with ×.
        var add=MainWindow.Button("Split here",()=>{var now=Math.Round(player.Position.TotalSeconds,2);if(now<=.05||now>=duration-.05)return;fields[drop].Text="";if(Points().Length==0)Write([now]);else Write(Points().Append(now));mode.Select(1);Show();});Ui.SetIcon(add,"plus",13);add.Margin=new(0,10,0,6);add.HorizontalAlignment=HorizontalAlignment.Left;
        chosenPane.Children.Add(add);chosenPane.Children.Add(L10n.Label(new TextBlock{Text="Split points",Margin=new(2,2,0,6)}));chosenPane.Children.Add(chips);
        options.Children.Add(equal);options.Children.Add(chosenPane);
        options.Children.Add(L10n.Label(new TextBlock{Text="The video is cut into these parts"}));options.Children.Add(list);Note("Click a part to leave it out or take it back. ▶ plays it.");
        // Saved as separate files, or what is kept as one video.
        options.Children.Add(L10n.Label(new TextBlock{Text="Save the parts as"}));var output=new Segmented(small:true);output.Add("Separate files",()=>joinClips.IsChecked=false);output.Add("One video",()=>joinClips.IsChecked=true);output.HorizontalAlignment=HorizontalAlignment.Left;options.Children.Add(output);
        joinClips.Checked+=(_,_)=>output.Select(1);joinClips.Unchecked+=(_,_)=>output.Select(0);output.Select(joinClips.IsChecked==true?1:0);
        var bar=MainWindow.Button("Split here",()=>add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));Ui.SetIcon(bar,"plus",13);bar.MinHeight=36;bar.Margin=new(6,0,0,0);barActions.Children.Add(bar);barHint.Text=L10n.Text("Click the film to move the playhead.");
        timeline.ShowsSegments=true;
        void Toggle(int index){var set=Dropped();if(!set.Remove(index))set.Add(index);if(set.Count>=Bounds().Length+1)return;fields[drop].Text=string.Join("; ",set.Order().Select(n=>(n+1).ToString(CultureInfo.InvariantCulture)));}
        timeline.SegmentToggled+=Toggle;
        timeline.MarkMoved+=(index,to)=>{var list=Points().ToList();if(index<list.Count){list[index]=Math.Round(to,2);Write(list);}};
        timeline.MarkRemoved+=index=>{var list=Points().ToList();if(index<list.Count){fields[drop].Text="";list.RemoveAt(index);if(list.Count==0){fields[count].Text="2";mode.Select(0);}Write(list);}};
        void Show()
        {
            var chosen=Points().Length>0;if(mode.Selected!=(chosen?1:0))mode.Select(chosen?1:0);equal.Visibility=chosen?Visibility.Collapsed:Visibility.Visible;chosenPane.Visibility=chosen||mode.Selected==1?Visibility.Visible:Visibility.Collapsed;
            number.Text=string.Format(L10n.Text("{0} parts"),Count());chips.Children.Clear();
            foreach(var t in Points()){var chip=MainWindow.Button(Stamp(t)+"  ×",()=>{fields[drop].Text="";var list=Points().Where(x=>Math.Abs(x-t)>1e-6).ToList();if(list.Count==0){fields[count].Text="2";mode.Select(0);}Write(list);});chip.Style=Ui.Style("Chip");chip.Margin=new(3,0,3,6);chips.Children.Add(chip);}
            timeline.Marks=Bounds();timeline.MarksMovable=chosen;var dropped=Dropped();timeline.Dropped=dropped;
            var cuts=new[]{0d}.Concat(Bounds()).Append(duration).ToArray();var parts=cuts.Length-1;var kept=Enumerable.Range(0,parts).Count(n=>!dropped.Contains(n));list.Children.Clear();
            for(var n=0;n<parts;n++)
            {
                var index=n;var a=cuts[n];var b=cuts[n+1];var keep=!dropped.Contains(n);var row=new Grid{Margin=new(0,0,0,5)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                // The part's name and length, then where it runs from and to.
                var line=new DockPanel();var mark=Icons.Make(keep?"check":"x",14);mark.Margin=new(0,0,8,0);DockPanel.SetDock(mark,Dock.Left);line.Children.Add(mark);
                var length=Plain(keep?Stamp(b-a):L10n.Text("Left out"),11.5);length.Opacity=.7;length.Margin=new(8,0,0,0);DockPanel.SetDock(length,Dock.Right);line.Children.Add(length);
                var text=new StackPanel();var name=Plain(string.Format(L10n.Text("Part {0}"),n+1),12.5,FontWeights.SemiBold);var span=Plain(Stamp(a)+"  →  "+Stamp(b),12);span.Margin=new(0,1,0,0);if(!keep)name.TextDecorations=span.TextDecorations=TextDecorations.Strikethrough;text.Children.Add(name);text.Children.Add(span);line.Children.Add(text);
                var toggle=MainWindow.Button("",()=>Toggle(index));toggle.Style=Ui.Style("Tile");Ui.SetRadius(toggle,12);toggle.Padding=new(10,7,10,7);toggle.Margin=new(0);toggle.HorizontalContentAlignment=HorizontalAlignment.Stretch;toggle.Content=line;toggle.Opacity=keep?1:.55;L10n.Bind(toggle,ToolTipProperty,keep?"Leave this part out":"Keep this part");System.Windows.Automation.AutomationProperties.SetName(toggle,string.Format(L10n.Text("Part {0}"),n+1));
                var play=MainWindow.Button("",()=>PlayPart(a,b));play.Style=Ui.Style("Chip");play.Content=Icons.Make("play",12);play.MinWidth=34;play.MinHeight=34;play.Margin=new(6,0,0,0);L10n.Bind(play,ToolTipProperty,"Play this part");System.Windows.Automation.AutomationProperties.SetName(play,L10n.Text("Play this part"));Grid.SetColumn(play,1);
                row.Children.Add(toggle);row.Children.Add(play);list.Children.Add(row);
            }
            var keptLength=Enumerable.Range(0,parts).Where(n=>!dropped.Contains(n)).Sum(n=>cuts[n+1]-cuts[n]);
            Outcome(joinClips.IsChecked==true?string.Format(L10n.Text("One video from {0} of {1} parts"),kept,parts):string.Format(L10n.Text("{0} files"),kept),joinClips.IsChecked==true?string.Format(L10n.Text("New length {0}"),Stamp(keptLength)):string.Join(", ",Enumerable.Range(0,parts).Where(n=>!dropped.Contains(n)).Select(n=>string.Format(L10n.Text("Part {0}"),n+1))));
        }
        foreach(var name in new[]{points,drop,count})fields[name].TextChanged+=(_,_)=>Show();joinClips.Checked+=(_,_)=>Show();joinClips.Unchecked+=(_,_)=>Show();afterReset+=()=>{mode.Select(0);output.Select(0);Show();};Show();
    }
    // Video Snapshots: frames added at the playhead, shown as pictures with their times.
    private void ShapeSnapshots()
    {
        const string field="Times (seconds)";Heading("Frames to save");
        double[] Times(){return fields[field].Text.Split(';',StringSplitOptions.RemoveEmptyEntries).Select(s=>{try{return NumericInput.ParseFinite(s.Trim());}catch(ArgumentException){return double.NaN;}}).Where(t=>double.IsFinite(t)&&t>=0&&t<duration).Distinct().Order().ToArray();}
        void Write(IEnumerable<double> times)=>fields[field].Text=string.Join("; ",times.Select(t=>Number(Math.Round(t,2))));
        void Add(){var now=Math.Round(player.Position.TotalSeconds,2);if(now<0||now>=duration)return;Write(Times().Append(now));}
        var add=MainWindow.Button("Add this frame",Add);Ui.SetIcon(add,"plus",13);add.Margin=new(0,8,0,8);add.HorizontalAlignment=HorizontalAlignment.Left;options.Children.Add(add);
        var grid=new WrapPanel{Margin=new(-4,0,0,0)};options.Children.Add(grid);var none=Note("Play or scrub to a frame, then add it. If you add none, the frame on screen is saved.");
        var bar=MainWindow.Button("Add this frame",Add);Ui.SetIcon(bar,"plus",13);bar.MinHeight=36;bar.Margin=new(6,0,0,0);barActions.Children.Add(bar);barHint.Text=L10n.Text("Click the film to move the playhead.");
        var pictures=new Dictionary<double,BitmapSource?>();
        void Show()
        {
            grid.Children.Clear();var times=Times();none.Visibility=times.Length==0?Visibility.Visible:Visibility.Collapsed;timeline.Marks=times;
            foreach(var t in times)
            {
                var tile=new Grid{Width=120,Margin=new(4,0,4,8)};var frame=new RoundBorder{Radius=10,Height=70,ClipToBounds=true};frame.SetResourceReference(Border.BackgroundProperty,"Sel");var image=new Image{Stretch=Stretch.UniformToFill};frame.Child=image;
                if(pictures.TryGetValue(t,out var known))image.Source=known;else{pictures[t]=null;_ = Picture(t,image);}
                var stack=new StackPanel();stack.Children.Add(frame);var label=new TextBlock{Text=Stamp(t),FontSize=11.5,FontWeight=FontWeights.SemiBold,Margin=new(2,4,0,0)};label.SetResourceReference(TextBlock.ForegroundProperty,"Ink");stack.Children.Add(label);tile.Children.Add(stack);
                var remove=MainWindow.Button("×",()=>Write(Times().Where(x=>Math.Abs(x-t)>1e-6)));remove.Style=Ui.Style("Chip");remove.MinHeight=22;remove.Padding=new(7,0,7,0);remove.Margin=new(0,4,4,0);remove.HorizontalAlignment=HorizontalAlignment.Right;remove.VerticalAlignment=VerticalAlignment.Top;L10n.Bind(remove,ToolTipProperty,"Remove");tile.Children.Add(remove);
                grid.Children.Add(tile);
            }
            if(times.Length>0)Outcome(string.Format(L10n.Text("{0} frames · PNG"),times.Length),string.Join(", ",times.Select(Stamp)));
        }
        async Task Picture(double t,Image image){var made=await FrameAt(t,120);pictures[t]=made;if(made is not null&&IsLoaded)Show();}
        fields[field].TextChanged+=(_,_)=>Show();Show();
    }
    // Redact Video: areas drawn on the video, each covering a time span on the timeline; one style for all.
    private void ShapeRedactVideo()
    {
        Heading("Areas to cover");Stash("X","Y","Width","Height");var hint=Note("Drag over the video to draw an area to cover.");
        Chips("Cover with",[("Black box","solid"),("Blur","blur"),("Pixelate","pixelate")],()=>redactMode.SelectedItem as string,value=>redactMode.SelectedItem=value,light=>redactMode.SelectionChanged+=(_,_)=>light());
        var list=new StackPanel{Margin=new(0,6,0,0)};options.Children.Add(list);var timing=new StackPanel();options.Children.Add(timing);
        timing.Children.Add(L10n.Label(new TextBlock{Text="When the chosen area is covered"}));var times=new StackPanel();timing.Children.Add(times);timing.Children.Add(L10n.Label(new TextBlock{Text="Or drag its coral edges on the timeline.",FontSize=11.5,Margin=new(2,2,0,0)}));
        void Choose(int index)
        {
            chosenArea=index;lastStyle="";timing.Visibility=index>=0?Visibility.Visible:Visibility.Collapsed;timeline.ShowsRange=index>=0;
            if(index>=0&&index<videoRegions.Count){var r=videoRegions[index];fields["Start"].Text=Number(r.Start);fields["End"].Text=Number(r.End??duration);timeline.Set(duration,r.Start,r.End??duration);}
            ShowAreas();
        }
        void ShowAreas()
        {
            list.Children.Clear();hint.Visibility=videoRegions.Count==0?Visibility.Visible:Visibility.Collapsed;
            for(var n=0;n<videoRegions.Count;n++)
            {
                var index=n;var r=videoRegions[n];var row=MainWindow.Button("",()=>{Choose(index);Seek(r.Start);});row.Style=Ui.Style("Tile");Ui.SetRadius(row,12);row.Padding=new(12,7,8,7);row.Margin=new(0,0,0,5);row.HorizontalAlignment=HorizontalAlignment.Stretch;
                var line=new DockPanel();var remove=MainWindow.Button("×",()=>{videoRegions.RemoveAt(index);var shape=drawing.Children.OfType<System.Windows.Shapes.Rectangle>().ElementAtOrDefault(index);if(shape is not null)drawing.Children.Remove(shape);Choose(-1);ReloadRanges();});remove.Style=Ui.Style("Chip");remove.MinHeight=22;remove.Padding=new(7,0,7,0);remove.Margin=new(6,0,0,0);L10n.Bind(remove,ToolTipProperty,"Remove");DockPanel.SetDock(remove,Dock.Right);line.Children.Add(remove);
                line.Children.Add(Plain(string.Format(L10n.Text("Area {0}"),n+1)+"  ·  "+Stamp(r.Start)+" – "+Stamp(r.End??duration),12.5,FontWeights.SemiBold));row.Content=line;row.HorizontalContentAlignment=HorizontalAlignment.Stretch;
                if(n==chosenArea){row.SetResourceReference(BackgroundProperty,"Solid");row.SetResourceReference(ForegroundProperty,"OnSolid");}list.Children.Add(row);
            }
            Outcome(videoRegions.Count==0?"":string.Format(L10n.Text("{0} areas covered"),videoRegions.Count));if(processedButton?.Parent is FrameworkElement compare)compare.Visibility=videoRegions.Count==0?Visibility.Collapsed:Visibility.Visible;
        }
        // The chosen area's time follows the fields (typed, the timeline's handles, or the playhead buttons).
        void Sync(){if(chosenArea<0||chosenArea>=videoRegions.Count)return;try{var a=Value("Start");var b=RangeEnd();if(a<0||b-a<MinRange)return;var r=videoRegions[chosenArea];if(Math.Abs(r.Start-a)<1e-6&&Math.Abs((r.End??duration)-b)<1e-6)return;videoRegions[chosenArea]=r with{Start=a,End=b};ShowAreas();}catch(ArgumentException){}}
        Times2("Start","End",into:times);HereButtons(timing);
        fields["Start"].TextChanged+=(_,_)=>Sync();fields["End"].TextChanged+=(_,_)=>Sync();
        afterRanges+=()=>{if(videoRegions.Count>0&&chosenArea>=videoRegions.Count)chosenArea=-1;ShowAreas();};
        newArea=index=>Choose(index);afterReset+=()=>Choose(-1);Choose(-1);
    }
    private Action<int>? newArea;
    // Bleep Audio: parts dragged on the waveform, each with ×; the chosen part's edges and times can be adjusted.
    private void ShapeRedactAudio()
    {
        Heading("Parts to cover");var hint=Note("Drag across the waveform to choose a part to cover.");
        Chips("Cover with",[("Bleep","bleep"),("Silence","mute")],()=>bleepMode.SelectedItem as string,value=>bleepMode.SelectedItem=value,light=>bleepMode.SelectionChanged+=(_,_)=>light());
        var list=new StackPanel{Margin=new(0,6,0,0)};options.Children.Add(list);
        options.Children.Add(L10n.Label(new TextBlock{Text="Exact times"}));Times2("Start","End");HereButtons();
        var add=exact!.Children.OfType<Button>().FirstOrDefault(b=>b.Content as string==L10n.Text("Add selected range"));if(add is not null){Take(add);add.Margin=new(0,4,0,0);Ui.SetIcon(add,"plus",13);options.Children.Add(add);}
        timeline.DrawsSpans=true;barHint.Text=L10n.Text("Drag across the waveform to cover a part.");Closed+=(_,_)=>{if(covering)Tone.Stop();};
        void Choose(int index){activePart=index;if(index>=0&&index<bleepRanges.Count){var r=bleepRanges[index];fields["Start"].Text=Number(r.Start);fields["End"].Text=Number(r.End);}ShowParts();}
        void ShowParts()
        {
            if(activePart>=bleepRanges.Count)activePart=-1;timeline.Spans=bleepRanges.Select(r=>(r.Start,r.End)).ToArray();timeline.ActiveSpan=activePart;timeline.ShowsRange=activePart>=0;if(activePart>=0)timeline.Set(duration,bleepRanges[activePart].Start,bleepRanges[activePart].End);
            list.Children.Clear();hint.Visibility=bleepRanges.Count==0?Visibility.Visible:Visibility.Collapsed;
            for(var n=0;n<bleepRanges.Count;n++)
            {
                var index=n;var r=bleepRanges[n];var row=MainWindow.Button("",()=>{Choose(index);Seek(r.Start);});row.Style=Ui.Style("Tile");Ui.SetRadius(row,12);row.Padding=new(12,7,8,7);row.Margin=new(0,0,0,5);row.HorizontalAlignment=HorizontalAlignment.Stretch;row.HorizontalContentAlignment=HorizontalAlignment.Stretch;
                var line=new DockPanel();var remove=MainWindow.Button("×",()=>{bleepRanges.RemoveAt(index);activePart=-1;ReloadRanges();});remove.Style=Ui.Style("Chip");remove.MinHeight=22;remove.Padding=new(7,0,7,0);remove.Margin=new(6,0,0,0);L10n.Bind(remove,ToolTipProperty,"Remove");DockPanel.SetDock(remove,Dock.Right);line.Children.Add(remove);
                line.Children.Add(Plain(string.Format(L10n.Text("Section {0}"),n+1)+"  ·  "+Stamp(r.Start)+" – "+Stamp(r.End),12.5,FontWeights.SemiBold));row.Content=line;
                if(n==activePart){row.SetResourceReference(BackgroundProperty,"Solid");row.SetResourceReference(ForegroundProperty,"OnSolid");}list.Children.Add(row);
            }
            Outcome(bleepRanges.Count==0?"":string.Format(L10n.Text("{0} parts · {1} covered"),bleepRanges.Count,Stamp(bleepRanges.Sum(r=>r.End-r.Start))));
        }
        void Sync(){if(activePart<0||activePart>=bleepRanges.Count)return;try{var a=Value("Start");var b=RangeEnd();if(a<0||b-a<MinRange)return;var r=bleepRanges[activePart];if(Math.Abs(r.Start-a)<1e-6&&Math.Abs(r.End-b)<1e-6)return;bleepRanges[activePart]=new(a,b);ShowParts();}catch(ArgumentException){}}
        fields["Start"].TextChanged+=(_,_)=>Sync();fields["End"].TextChanged+=(_,_)=>Sync();
        timeline.SpanDrawn+=(a,b)=>{bleepRanges.Add(new(Math.Round(a,2),Math.Round(b,2)));activePart=bleepRanges.Count-1;Choose(activePart);};
        timeline.SpanChosen+=Choose;timeline.SpanRemoved+=index=>{if(index<bleepRanges.Count){bleepRanges.RemoveAt(index);activePart=-1;ReloadRanges();}};
        // The playhead buttons and the I and O keys start a part here, or move the chosen part's edge here.
        markHere=start=>{var now=Math.Round(Math.Clamp(player.Position.TotalSeconds,0,duration),2);if(activePart<0){var a=start?now:Math.Max(0,now-1);var b=start?Math.Min(duration,now+1):now;if(b-a<MinRange)return;bleepRanges.Add(new(a,b));Choose(bleepRanges.Count-1);return;}
            var r=bleepRanges[activePart];if(start&&now<r.End-MinRange)fields["Start"].Text=Number(now);else if(!start&&now>r.Start+MinRange)fields["End"].Text=Number(now);};
        afterRanges+=()=>{if(add is not null&&activePart<0&&bleepRanges.Count>0)activePart=bleepRanges.Count-1;ShowParts();};afterReset+=()=>{activePart=-1;ShowParts();};ShowParts();
    }
    // Audio to Video: the frame's shape and what moves in it, shown as a still of the video it will make.
    private readonly Image visualPreview=new(){Stretch=Stretch.Uniform};
    private void ShapeVisualization()
    {
        Heading("Video");Stash("Width","Height");string Shape(){try{return Value("Width",1280)+"x"+Value("Height",720);}catch(ArgumentException){return "";}}
        Chips("Size",[("Wide 1080p","1920x1080"),("Wide 720p","1280x720"),("Square","1080x1080"),("Vertical","1080x1920")],Shape,value=>{var sides=value.Split('x');fields["Width"].Text=sides[0];fields["Height"].Text=sides[1];},light=>{fields["Width"].TextChanged+=(_,_)=>light();fields["Height"].TextChanged+=(_,_)=>light();});
        Chips("Visualization",[("Waveform","waveform"),("Spectrum","spectrum"),("Bars","bars"),("Picture","image")],()=>visualization.SelectedItem as string,value=>{if(value=="image"&&visualImage is null){ChooseVisualImage();return;}visualization.SelectedItem=value;},light=>visualization.SelectionChanged+=(_,_)=>light());
        var picker=MainWindow.Button("Choose picture…",ChooseVisualImage);picker.Style=Ui.Style("Chip");picker.Margin=new(0,2,0,0);options.Children.Add(picker);
        void Show(){picker.Visibility=visualization.SelectedItem as string=="image"?Visibility.Visible:Visibility.Collapsed;try{Outcome(string.Format(L10n.Text("MP4 · {0} × {1} · {2}"),(int)Value("Width",1280),(int)Value("Height",720),Stamp(duration)));}catch(ArgumentException){}_ = DrawVisualization();_ = RenderVisualization();}
        visualization.SelectionChanged+=(_,_)=>Show();fields["Width"].TextChanged+=(_,_)=>Show();fields["Height"].TextChanged+=(_,_)=>Show();afterReset+=Show;Show();
    }
    private void ChooseVisualImage(){var picker=new Microsoft.Win32.OpenFileDialog{Filter="Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp"};if(picker.ShowDialog(this)==true){visualImage=picker.FileName;visualization.SelectedItem="image";}}
    // Normalize: three targets, each saying how much louder or quieter the sound becomes once measured.
    private double? loudness;
    private void ShapeLoudness()
    {
        Heading("Loudness");Stash("Target LUFS","True peak dB","Loudness range");
        string Change(double target)=>loudness is {} now?Gain(target-now):"";
        var measured=Note("Measuring how loud it is now…");
        Rows(new (string,string,Func<string>,string)[]{("Speech and podcasts","−16 LUFS",()=>Change(-16),"-16"),("Music services","−14 LUFS",()=>Change(-14),"-14"),("TV and radio","−23 LUFS",()=>Change(-23),"-23")},
            ()=>{try{return Number(Value("Target LUFS",-16));}catch(ArgumentException){return null;}},value=>fields["Target LUFS"].Text=value,light=>{fields["Target LUFS"].TextChanged+=(_,_)=>light();loudnessKnown+=light;});
        Note("Peaks stay below −1.5 dB, so nothing clips.");
        Follow(()=>{try{return loudness is {} now?Gain(Value("Target LUFS",-16)-now,true):"";}catch(ArgumentException){return "";}},"Target LUFS");
        loudnessKnown+=()=>{measured.Text=loudness is {} now?string.Format(L10n.Text("Now {0} LUFS."),now.ToString("0.0",CultureInfo.CurrentCulture)):L10n.Text("The current loudness could not be measured.");try{Outcome(loudness is {} n?Gain(Value("Target LUFS",-16)-n,true):"");}catch(ArgumentException){}};
        _ = MeasureLoudness();
    }
    private Action? loudnessKnown;
    private static string Gain(double db,bool sentence=false){var value=Math.Abs(db).ToString("0.0",CultureInfo.CurrentCulture)+" dB";if(Math.Abs(db)<.3)return L10n.Text(sentence?"Already at this loudness":"No change");return sentence?string.Format(L10n.Text(db>0?"{0} louder":"{0} quieter"),value):(db>0?"+":"−")+value;}
    // Audio Channels: stereo or mono, the balance and the volume, heard live in the preview.
    private void ShapeChannels()
    {
        Heading("Channels");Stash("Channels","Left volume","Right volume");var output=new Segmented(small:true);output.Add("Stereo",()=>fields["Channels"].Text="2");output.Add("Mono",()=>fields["Channels"].Text="1");output.Margin=new(0,8,0,0);output.HorizontalAlignment=HorizontalAlignment.Left;options.Children.Add(output);
        double Level(){try{return Math.Max(Value("Left volume",1),Value("Right volume",1));}catch(ArgumentException){return 1;}}
        double Balance(){try{double left=Value("Left volume",1),right=Value("Right volume",1),top=Math.Max(left,right);return top<=0?0:left<right?1-left/top:right<left?-(1-right/top):0;}catch(ArgumentException){return 0;}}
        void Mix(double level,double balance){if(Math.Abs(balance)<.02)balance=0;fields["Left volume"].Text=Number(Math.Round(level*(balance>0?1-balance:1),2));fields["Right volume"].Text=Number(Math.Round(level*(balance<0?1+balance:1),2));}
        string Side(double v)=>Math.Abs(v)<.02?L10n.Text("Center"):string.Format(L10n.Text(v<0?"Left {0}":"Right {0}"),Math.Round(Math.Abs(v)*100)+"%");
        Slide("Balance",-1,1,Balance,Side,v=>Mix(Level(),v));Slide("Volume",0,2,Level,v=>Math.Round(v*100)+"%",v=>Mix(v,Balance()));Note("The preview plays with this balance and volume.");
        void Show()
        {
            var mono=fields["Channels"].Text.Trim()=="1";output.Select(mono?1:0);player.Balance=mono?0:Math.Clamp(Balance(),-1,1);player.Volume=Math.Clamp(.5*Level(),0,1);
            Outcome(string.Format(L10n.Text("{0} · {1} · {2}"),L10n.Text(mono?"Mono":"Stereo"),Side(Balance()),Math.Round(Level()*100)+"%"));
        }
        foreach(var name in new[]{"Channels","Left volume","Right volume"})fields[name].TextChanged+=(_,_)=>Show();afterReset+=Show;Show();
    }
    // Convert: formats as tiles that say what each is for (video first, then sound only), then the chosen format's options.
    private void ShapeConvert()
    {
        Heading("Format");var all=format.Items.Cast<string>().ToArray();
        if(!formatRequested&&(media!.Video is not null?new[]{"mp4","mov","mkv","webm"}:new[]{"mp3","m4a","wav"}).FirstOrDefault(all.Contains) is {} preferred)format.SelectedItem=preferred;
        var pictures=all.Where(MediaEngine.VideoFormats.Contains).ToArray();var sounds=all.Where(MediaEngine.AudioFormats.Contains).ToArray();
        void Tiles(string title,string[] list)
        {
            if(list.Length==0)return;options.Children.Add(L10n.Label(new TextBlock{Text=title}));var grid=new UniformGrid{Columns=2,Margin=new(-3,0,-3,0)};var tiles=new List<(Button,string)>();
            foreach(var f in list)
            {
                var text=new StackPanel{HorizontalAlignment=HorizontalAlignment.Left};text.Children.Add(Plain(f.ToUpperInvariant(),13,FontWeights.Bold));var sub=L10n.Label(Plain(FormatUse(f),11));sub.Margin=new(0,1,0,0);sub.Opacity=.7;sub.TextWrapping=TextWrapping.Wrap;text.Children.Add(sub);
                var tile=MainWindow.Button("",()=>format.SelectedItem=f);tile.Style=Ui.Style("Tile");Ui.SetRadius(tile,12);tile.Padding=new(11,7,8,7);tile.Margin=new(3,0,3,6);tile.Content=text;tile.HorizontalContentAlignment=HorizontalAlignment.Stretch;System.Windows.Automation.AutomationProperties.SetName(tile,f.ToUpperInvariant());tiles.Add((tile,f));grid.Children.Add(tile);
            }
            void Light(){foreach(var (tile,f) in tiles)if(format.SelectedItem as string==f){tile.SetResourceReference(BackgroundProperty,"Solid");tile.SetResourceReference(ForegroundProperty,"OnSolid");}else{tile.ClearValue(BackgroundProperty);tile.ClearValue(ForegroundProperty);}}
            options.Children.Add(grid);format.SelectionChanged+=(_,_)=>Light();Light();
        }
        Tiles(pictures.Length>0&&sounds.Length>0?"Video":"Format",pictures);Tiles(pictures.Length>0?"Sound only":"Format",sounds);
        Stash("Quality (%)","Audio bitrate (kbps)","Maximum video height (0 = original)","GIF frames per second","GIF width");
        var quality=NumberChips("Quality","Quality (%)",[("High",90),("Balanced",80),("Small file",65)]);var height=NumberChips("Resolution","Maximum video height (0 = original)",[("Original",0),("1080p",1080),("720p",720),("480p",480)]);
        var bitrate=NumberChips("Sound quality","Audio bitrate (kbps)",[("128 kbps",128),("192 kbps",192),("256 kbps",256),("320 kbps",320)]);
        var smooth=NumberChips("GIF smoothness","GIF frames per second",[("10 fps",10),("15 fps",15),("24 fps",24)]);var wide=NumberChips("GIF width","GIF width",[("480 px",480),("720 px",720),("960 px",960)]);
        // The bit depth and the metadata choice stay where they were made, below the format's options.
        foreach(var late in new FrameworkElement[]{sampleDepth,preserveMetadata}){if(late.Parent is not Panel){continue;}var label=late==sampleDepth&&late.Parent is Panel holder&&holder.Children.IndexOf(late) is var at&&at>0&&holder.Children[at-1] is TextBlock caption?caption:null;if(label is not null){Take(label);options.Children.Add(label);}Take(late);options.Children.Add(late);}
        // What each format can use.
        void Show()
        {
            var value=format.SelectedItem as string??"";var video=MediaEngine.VideoFormats.Contains(value);var audio=MediaEngine.AudioFormats.Contains(value);
            quality.Visibility=height.Visibility=video&&value!="gif"?Visibility.Visible:Visibility.Collapsed;bitrate.Visibility=(audio||video)&&value is not ("wav" or "aiff" or "flac" or "gif")?Visibility.Visible:Visibility.Collapsed;smooth.Visibility=wide.Visibility=value=="gif"?Visibility.Visible:Visibility.Collapsed;
            Outcome(value.Length==0?"":value.ToUpperInvariant()+" · "+L10n.Text(FormatUse(value)));
        }
        format.SelectionChanged+=(_,_)=>Show();Show();
    }
    private static string FormatUse(string format)=>format switch{"mp4"=>"Plays everywhere","mov"=>"Apple devices and editing","mkv"=>"Keeps every track","webm"=>"For websites","gif"=>"Silent looping animation","avi"=>"Older Windows software","wmv"=>"Windows Media","m4v"=>"iTunes and Apple TV","mp3"=>"Plays everywhere","m4a"=>"Small, Apple-friendly","wav"=>"Lossless, large","flac"=>"Lossless, smaller","ogg"=>"Open format","opus"=>"Smallest for voice","aiff"=>"Lossless, for Mac","wma"=>"Windows Media Audio","aac"=>"Small, widely supported",_=>""};
    // A field moves (with its label) out of sight; it keeps working as the value saved.
    private void Stash(params string[] names)
    {
        exact??=new StackPanel{Visibility=Visibility.Collapsed};
        foreach(var name in names)
        {
            if(!fields.TryGetValue(name,out var box)||box.Parent is not Panel holder||holder==exact)continue;var at=holder.Children.IndexOf(box);
            if(at>0&&holder.Children[at-1] is TextBlock label){holder.Children.Remove(label);exact.Children.Add(label);}holder.Children.Remove(box);exact.Children.Add(box);
        }
    }
    // Choices as chips: the one the source holds is lit, a click writes it back. Title and chips go and come together.
    private StackPanel Chips(string title,IEnumerable<(string Label,string Value)> items,Func<string?> current,Action<string> choose,Action<Action> follow)
    {
        var group=new StackPanel();if(title.Length>0)group.Children.Add(L10n.Label(new TextBlock{Text=title}));else group.Margin=new(0,8,0,0);var row=new WrapPanel{Margin=new(-3,0,0,0)};var chips=new List<(Button Chip,string Value)>();
        foreach(var (label,value) in items){var chip=MainWindow.Button(label,()=>choose(value));chip.Style=Ui.Style("Chip");chip.Margin=new(3,0,3,6);chips.Add((chip,value));row.Children.Add(chip);}
        void Light(){var now=current();foreach(var (chip,value) in chips){if(now==value){chip.SetResourceReference(BackgroundProperty,"Solid");chip.SetResourceReference(ForegroundProperty,"OnSolid");}else{chip.ClearValue(BackgroundProperty);chip.ClearValue(ForegroundProperty);}}}
        group.Children.Add(row);options.Children.Add(group);follow(Light);Light();return group;
    }
    private StackPanel NumberChips(string title,string field,(string Label,double Value)[] items)=>!fields.ContainsKey(field)?new StackPanel():Chips(title,items.Select(i=>(i.Label,Number(i.Value))),()=>{try{var value=Value(field);return items.Where(i=>Math.Abs(i.Value-value)<1e-6).Select(i=>Number(i.Value)).FirstOrDefault();}catch(ArgumentException){return null;}},value=>fields[field].Text=value,light=>fields[field].TextChanged+=(_,_)=>light());
    // Text inside a choice: no panel styling, so it takes the choice's own color when lit.
    private static TextBlock Plain(string text,double size,FontWeight? weight=null)=>new(){Text=text,FontSize=size,FontWeight=weight??FontWeights.Normal,Style=null,Margin=new(0),TextAlignment=TextAlignment.Left,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};
    private TextBlock Note(string text){var note=L10n.Label(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,FontSize=11.5,Margin=new(2,6,0,4)});note.SetResourceReference(TextBlock.ForegroundProperty,"Muted");options.Children.Add(note);return note;}
    private static string Times(double speed)=>Number(speed)+"×";
}
