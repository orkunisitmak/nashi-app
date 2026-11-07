using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;

// Split PDF. The pages lie on the canvas, the decision sits beside them, and what will come out is said in plain words:
// which files, with which pages. A line runs under the pages of one file and breaks where the next begins; a click between
// two pages cuts there, a click on a page leaves its file out. The generic fields stay (hidden) as the values saved.
public sealed partial class ToolEditor
{
    private const double SplitCellWidth=118,SplitCellHeight=172;
    private sealed record SplitCell(FrameworkElement Sheet,Image Picture,Border Tag,TextBlock TagText,FrameworkElement Mark,FrameworkElement Gap,Border Bar);
    private readonly WrapPanel splitCanvas=new();private readonly ScrollViewer splitScroll=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    private readonly List<SplitCell> splitCells=[];private bool splitLoading;
    // The pages fill the left and the panel sits on the right, as in the media editors; nothing lies over the pages.
    private void ArrangePdfSplit(DockPanel previewArea)
    {
        Grid.SetRow(stage,1);Grid.SetRowSpan(stage,1);previewArea.Margin=new(18,4,18,6);
        if(optionsPanel is {} panel){panel.VerticalAlignment=VerticalAlignment.Top;panel.Margin=new(0,4,18,6);panel.SizeChanged+=(_,_)=>FitBeside(previewArea);panel.IsVisibleChanged+=(_,_)=>FitBeside(previewArea);}
        // Several PDFs are split alike, so there is no one set of pages to show: the panel says what happens to each.
        if(collageInputs.Length==1){previewArea.Children.Clear();splitScroll.Content=splitCanvas;previewArea.Children.Add(splitScroll);splitScroll.ScrollChanged+=(_,_)=>_ = LoadSplitPages();}
        Loaded+=(_,_)=>ShapePdfSplit();
    }
    private void ShapePdfSplit()
    {
        const string each="Pages per file",ranges="Page ranges";
        exact=new StackPanel{Visibility=Visibility.Collapsed};foreach(var child in options.Children.Cast<UIElement>().Where(c=>c!=panelTitle).ToArray()){options.Children.Remove(child);exact.Children.Add(child);}
        Heading("How should it be split?");var total=0;var chosen=false;var cuts=new SortedSet<int>();var left=new HashSet<int>();
        var about=Note("");
        // A password is asked for only when the PDF has one.
        var locked=new StackPanel{Visibility=Visibility.Collapsed};locked.Children.Add(L10n.Label(new TextBlock{Text="PDF password (if needed)"}));Take(archivePassword);locked.Children.Add(archivePassword);options.Children.Add(locked);
        var mode=new Segmented(small:true);var regular=new StackPanel();var manual=new StackPanel();var list=new StackPanel{Margin=new(0,2,0,0)};var typed=new TextBox();
        var wrong=L10n.Label(new TextBlock{Text="Write the parts in order, such as 1-3; 5; 8-10.",TextWrapping=TextWrapping.Wrap,FontSize=11.5,Margin=new(2,6,0,0),Visibility=Visibility.Collapsed});wrong.SetResourceReference(TextBlock.ForegroundProperty,"Danger");
        var files=L10n.Label(new TextBlock{Text="These files will be made"});var leave=L10n.Label(new TextBlock{Text="Click a file or a page to leave it out or take it back.",TextWrapping=TextWrapping.Wrap,FontSize=11.5,Margin=new(2,6,0,4)});leave.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        int Each(){return int.TryParse(fields[each].Text.Trim(),NumberStyles.Integer,CultureInfo.CurrentCulture,out var n)?Math.Clamp(n,1,1000):1;}
        // Every part of the PDF, in order, including the ones left out.
        (int First,int Last)[] Parts()
        {
            var starts=chosen?new[]{1}.Concat(cuts).ToArray():Enumerable.Range(0,(total+Each()-1)/Math.Max(1,Each())).Select(n=>n*Each()+1).ToArray();
            return starts.Select((first,n)=>(first,n+1<starts.Length?starts[n+1]-1:total)).ToArray();
        }
        static string Written((int First,int Last) part)=>part.First==part.Last?part.First.ToString(CultureInfo.InvariantCulture):$"{part.First}-{part.Last}";
        string Kept()=>string.Join("; ",Parts().Where((_,n)=>!left.Contains(n)).Select(Written));
        // What the engine gets: nothing for an even split; semicolons name the files themselves.
        void Keep(){fields[ranges].Text=!chosen&&left.Count==0?"":Kept()+";";}
        mode.Add("Every few pages",()=>{chosen=false;left.Clear();Keep();Show();});
        mode.Add("Where I choose",()=>{if(!chosen){cuts=new SortedSet<int>(Each()==1?Enumerable.Empty<int>():Parts().Skip(1).Select(p=>p.First));chosen=true;}left.Clear();Keep();Show();});
        mode.Margin=new(0,8,0,0);mode.HorizontalAlignment=HorizontalAlignment.Left;mode.Select(0);options.Children.Add(mode);
        // Every few pages: how many in each file.
        var pagesChips=Chips("Pages in each file",new[]{(L10n.Text("Every page"),"1"),("2","2"),("3","3"),("5","5"),("10","10")},()=>Number(Each()),value=>fields[each].Text=value,light=>fields[each].TextChanged+=(_,_)=>light());options.Children.Remove(pagesChips);regular.Children.Add(pagesChips);
        var own=fields[each];Take(own);own.Style=Ui.Style("Unit");own.Tag=L10n.Text("pages");own.Margin=new(0);own.Width=110;own.HorizontalAlignment=HorizontalAlignment.Left;regular.Children.Add(L10n.Label(new TextBlock{Text="Or a number of your own"}));regular.Children.Add(own);
        own.TextChanged+=(_,_)=>{left.Clear();if(!chosen)Keep();Show();};options.Children.Add(regular);
        // Where I choose: cuts made on the pages, or typed.
        var how=L10n.Label(new TextBlock{Text="Click between two pages on the left to cut there. Click again to take the cut away.",TextWrapping=TextWrapping.Wrap,FontSize=11.5,Margin=new(2,10,0,4)});how.SetResourceReference(TextBlock.ForegroundProperty,"Muted");manual.Children.Add(how);
        L10n.Bind(typed,ToolTipProperty,"1-3; 5; 8-10 makes three files.");System.Windows.Automation.AutomationProperties.SetName(typed,L10n.Text("Or type the parts"));manual.Children.Add(L10n.Label(new TextBlock{Text="Or type the parts"}));manual.Children.Add(typed);
        manual.Children.Add(wrong);options.Children.Add(manual);
        typed.TextChanged+=(_,_)=>
        {
            if(!typed.IsKeyboardFocusWithin)return;var named=TypedParts(typed.Text,total);wrong.Visibility=named is not null||typed.Text.Trim().Length==0?Visibility.Collapsed:Visibility.Visible;if(named is null)return;
            // A page not named falls in a part of its own, left out.
            cuts=new SortedSet<int>(named.SelectMany(p=>new[]{p.First,p.Last+1}).Where(p=>p>1&&p<=total));var all=Parts();left=new HashSet<int>(Enumerable.Range(0,all.Length).Where(n=>!named.Any(p=>p.First<=all[n].First&&all[n].Last<=p.Last)));Keep();Show();
        };
        options.Children.Add(ResultCard());options.Children.Add(files);options.Children.Add(list);options.Children.Add(leave);options.Children.Add(exact);
        void ToggleFile(int index){if(!left.Remove(index)&&left.Count+1<Parts().Length)left.Add(index);Keep();Show();}
        void ToggleCut(int page){if(!chosen){cuts=new SortedSet<int>(Parts().Skip(1).Select(p=>p.First));chosen=true;mode.Select(1);}if(!cuts.Remove(page))cuts.Add(page);left.Clear();Keep();Show();}
        void Show()
        {
            var known=total>0;var many=total>1;mode.Visibility=many?Visibility.Visible:Visibility.Collapsed;regular.Visibility=(many&&!chosen)||!known?Visibility.Visible:Visibility.Collapsed;manual.Visibility=many&&chosen?Visibility.Visible:Visibility.Collapsed;files.Visibility=list.Visibility=leave.Visibility=many?Visibility.Visible:Visibility.Collapsed;
            if(!known)
            {
                var n=Each();Outcome(n==1?L10n.Text("Every page becomes a file"):string.Format(L10n.Text("A new file every {0} pages"),n));return;
            }
            var parts=Parts();var number=0;var names=parts.Select((_,n)=>left.Contains(n)?"":string.Format(L10n.Text("File {0}"),++number)).ToArray();
            if(!typed.IsKeyboardFocusWithin){typed.Text=Kept();wrong.Visibility=Visibility.Collapsed;}
            string Pages(int count)=>count==1?L10n.Text("1 page"):string.Format(L10n.Text("{0} pages"),count);
            // The pages: a tag on the first of each file, a line under each file's pages, broken where a cut is.
            for(var n=0;n<parts.Length;n++)for(var page=parts[n].First;page<=parts[n].Last&&page<=splitCells.Count;page++)
            {
                var cell=splitCells[page-1];var first=page==parts[n].First;var outside=left.Contains(n);cell.Sheet.Opacity=outside?.35:1;
                cell.Tag.Visibility=first?Visibility.Visible:Visibility.Collapsed;if(first)cell.TagText.Text=outside?L10n.Text("Left out"):names[n];
                cell.Mark.Opacity=first?1:0;cell.Gap.ToolTip=L10n.Text(first?"Take this cut away":"Cut here");cell.Sheet.ToolTip=L10n.Text(outside?"Keep this file":"Leave this file out");
                Grid.SetColumn(cell.Bar,first?1:0);Grid.SetColumnSpan(cell.Bar,first?1:2);cell.Bar.SetResourceReference(Border.BackgroundProperty,outside?"Faint":n%2==0?"Coral":"Ink");
            }
            list.Children.Clear();
            foreach(var n in Enumerable.Range(0,Math.Min(parts.Length,40)))
            {
                var index=n;var part=parts[n];var keep=!left.Contains(n);var line=new DockPanel();var mark=Icons.Make(keep?"check":"x",14);mark.Margin=new(0,0,8,0);DockPanel.SetDock(mark,Dock.Left);line.Children.Add(mark);
                var count=Plain(Pages(part.Last-part.First+1),11.5);count.Opacity=.7;count.Margin=new(8,0,0,0);DockPanel.SetDock(count,Dock.Right);line.Children.Add(count);
                var where=part.First==part.Last?string.Format(L10n.Text("Page {0}"),part.First):string.Format(L10n.Text("Pages {0}–{1}"),part.First,part.Last);var text=Plain((keep?names[n]:L10n.Text("Left out"))+"   "+where,12.5,FontWeights.SemiBold);if(!keep)text.TextDecorations=TextDecorations.Strikethrough;line.Children.Add(text);
                var row=MainWindow.Button("",()=>ToggleFile(index));row.Style=Ui.Style("Tile");Ui.SetRadius(row,12);row.Padding=new(10,7,10,7);row.Margin=new(0,0,0,5);row.HorizontalAlignment=HorizontalAlignment.Stretch;row.HorizontalContentAlignment=HorizontalAlignment.Stretch;row.Content=line;row.Opacity=keep?1:.55;L10n.Bind(row,ToolTipProperty,keep?"Leave this file out":"Keep this file");System.Windows.Automation.AutomationProperties.SetName(row,where);list.Children.Add(row);
            }
            if(parts.Length>40)list.Children.Add(new TextBlock{Text=string.Format(L10n.Text("and {0} more files"),parts.Length-40),Margin=new(2,2,0,0)});
            var made=parts.Length-left.Count;var kept=parts.Where((_,n)=>!left.Contains(n)).Sum(p=>p.Last-p.First+1);
            Outcome(made==1?L10n.Text("1 file"):string.Format(L10n.Text("{0} files"),made),string.Format(L10n.Text("{0} pages in all"),kept)+(kept<total?" · "+string.Format(L10n.Text("{0} pages left out"),total-kept):""));
        }
        afterReset+=()=>{chosen=false;cuts.Clear();left.Clear();mode.Select(0);wrong.Visibility=Visibility.Collapsed;Show();};
        // A new password starts a new read; only the latest one may change what is shown.
        var reading=0;
        async void Read()
        {
            var mine=++reading;
            try
            {
                var count=collageInputs.Length==1?await NativePdf.PageCountAsync(input,archivePassword.Password):0;if(!IsLoaded||mine!=reading)return;total=count;
                ShowPreviewNotice(null);locked.Visibility=archivePassword.Password.Length>0?Visibility.Visible:Visibility.Collapsed;
                about.Text=collageInputs.Length>1?string.Format(L10n.Text("{0} PDFs selected. Each is split the same way."),collageInputs.Length):total==1?L10n.Text("This PDF has one page, so there is nothing to split."):string.Format(L10n.Text("{0} pages."),total)+" "+L10n.Text("Each part becomes its own PDF; your original stays as it is.");
                if(total>0)BuildSplitCells(total,ToggleCut,page=>{var parts=Parts();var n=Array.FindIndex(parts,p=>p.First<=page&&page<=p.Last);if(n>=0)ToggleFile(n);});
            }
            catch(OperationCanceledException){return;}
            catch(InvalidDataException error)
            {
                // Nothing can be shown: the even split still works, and a password can still be typed.
                if(mine!=reading)return;total=0;splitCanvas.Children.Clear();splitCells.Clear();if(!IsLoaded)return;var needsPassword=error.Message=="The PDF password is missing or incorrect.";locked.Visibility=needsPassword?Visibility.Visible:locked.Visibility;
                ShowPreviewNotice(L10n.Text(needsPassword?"This PDF has a password. Type it to see the pages.":"The pages cannot be shown, but the split still works."));about.Text=L10n.Text("Each part becomes its own PDF; your original stays as it is.");
            }
            catch(Exception error){if(mine!=reading)return;if(IsLoaded)ShowPreviewNotice(L10n.Text("Preview unavailable")+" · "+error.Message);}
            chosen=false;cuts.Clear();left.Clear();mode.Select(0);Keep();Show();if(total>0)_ = LoadSplitPages();
        }
        var again=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(400)};again.Tick+=(_,_)=>{again.Stop();Read();};archivePassword.PasswordChanged+=(_,_)=>{again.Stop();again.Start();};Closed+=(_,_)=>again.Stop();
        Show();Read();
    }
    // Typed parts in order and without overlap ("1-3; 5"), or null. Each is a run of pages.
    private static (int First,int Last)[]? TypedParts(string text,int total)
    {
        var parts=new List<(int First,int Last)>();foreach(var piece in text.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
        {
            var ends=piece.Split('-',StringSplitOptions.TrimEntries);if(ends.Length>2||!int.TryParse(ends[0],out var first)||!int.TryParse(ends[^1],out var last)||first<1||last<first||last>total||parts.Count>0&&first<=parts[^1].Last)return null;parts.Add((first,last));
        }
        return parts.Count>0?parts.ToArray():null;
    }
    private void BuildSplitCells(int total,Action<int> cut,Action<int> toggle)
    {
        splitCanvas.Children.Clear();splitCells.Clear();
        for(var page=1;page<=total;page++)
        {
            var number=page;var cell=new Grid{Width=SplitCellWidth,Height=SplitCellHeight};cell.ColumnDefinitions.Add(new(){Width=new(22)});cell.ColumnDefinitions.Add(new());cell.RowDefinitions.Add(new(){Height=new(132)});cell.RowDefinitions.Add(new(){Height=new(9)});cell.RowDefinitions.Add(new());
            var picture=new Image{Stretch=Stretch.Uniform};RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.HighQuality);var paper=new Border{Background=Brushes.White,BorderThickness=new(1),CornerRadius=new(4),Child=picture,ClipToBounds=true};paper.SetResourceReference(Border.BorderBrushProperty,"Line");
            var tagText=new TextBlock{FontSize=10.5,FontWeight=FontWeights.SemiBold,Style=null,Margin=new(0)};tagText.SetResourceReference(TextBlock.ForegroundProperty,"OnSolid");var tag=new RoundBorder{Radius=99,Padding=new(7,2,7,2),Child=tagText,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new(4),IsHitTestVisible=false,Visibility=Visibility.Collapsed};tag.SetResourceReference(Border.BackgroundProperty,"Solid");
            var sheet=new Grid{Cursor=Cursors.Hand,Background=Brushes.Transparent,Children={paper,tag}};sheet.MouseLeftButtonUp+=(_,_)=>toggle(number);Grid.SetColumn(sheet,1);cell.Children.Add(sheet);
            var bar=new Border{Height=3,CornerRadius=new(1.5),VerticalAlignment=VerticalAlignment.Bottom,IsHitTestVisible=false};Grid.SetRow(bar,1);cell.Children.Add(bar);
            var label=new TextBlock{Text=number.ToString(CultureInfo.CurrentCulture),FontSize=11,TextAlignment=TextAlignment.Center,Style=null,Margin=new(0,3,0,0)};label.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Grid.SetRow(label,2);Grid.SetColumn(label,1);cell.Children.Add(label);
            // Between this page and the one before: a place to cut, showing the cut when there is one.
            var mark=new Grid{IsHitTestVisible=false,Opacity=0};var line=new Border{Width=2,HorizontalAlignment=HorizontalAlignment.Center,CornerRadius=new(1)};line.SetResourceReference(Border.BackgroundProperty,"Coral");mark.Children.Add(line);var scissors=new Border{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,CornerRadius=new(9),Width=18,Height=18,Child=new TextBlock{Text="✂",FontSize=11,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Style=null,Margin=new(0),Foreground=Brushes.White}};scissors.SetResourceReference(Border.BackgroundProperty,"Coral");mark.Children.Add(scissors);
            var gap=new Border{Background=Brushes.Transparent,Child=mark,Cursor=Cursors.Hand};if(page==1)gap.Visibility=Visibility.Hidden;gap.MouseLeftButtonUp+=(_,_)=>cut(number);
            var hint=new Border{Width=2,HorizontalAlignment=HorizontalAlignment.Center,IsHitTestVisible=false,Opacity=0,CornerRadius=new(1)};hint.SetResourceReference(Border.BackgroundProperty,"Coral");cell.Children.Add(hint);gap.MouseEnter+=(_,_)=>hint.Opacity=.5;gap.MouseLeave+=(_,_)=>hint.Opacity=0;cell.Children.Add(gap);
            splitCells.Add(new(sheet,picture,tag,tagText,mark,gap,bar));splitCanvas.Children.Add(cell);
        }
    }
    // Only the pages in view (and the row after) are drawn, a few at a time, so a long PDF opens at once and scrolls freely.
    private async Task LoadSplitPages()
    {
        if(splitLoading||splitCells.Count==0)return;splitLoading=true;
        try
        {
            while(IsLoaded)
            {
                var columns=Math.Max(1,(int)(splitScroll.ViewportWidth/SplitCellWidth));var from=(int)(splitScroll.VerticalOffset/SplitCellHeight)*columns;var to=Math.Min(splitCells.Count,((int)((splitScroll.VerticalOffset+splitScroll.ViewportHeight)/SplitCellHeight)+2)*columns);
                var wanted=Enumerable.Range(from,Math.Max(0,to-from)).Where(n=>splitCells[n].Picture.Source is null).Take(24).Select(n=>n+1).ToArray();if(wanted.Length==0)return;
                var rendered=await NativePdf.RenderPagesAsync(input,wanted,36,previewLifetime.Token,archivePassword.Password);
                var pictures=await Task.Run(()=>rendered.Select(page=>(page.Key,Picture:SplitThumbnail(page.Value))).ToArray());
                foreach(var (page,picture) in pictures)if(page<=splitCells.Count)splitCells[page-1].Picture.Source=picture;
                if(rendered.Count==0)return;
            }
        }
        catch(OperationCanceledException){}catch(InvalidDataException){}catch(IOException){}
        finally{splitLoading=false;}
    }
    private static BitmapSource SplitThumbnail(byte[] png){var image=new BitmapImage();image.BeginInit();image.StreamSource=new MemoryStream(png);image.DecodePixelWidth=192;image.CacheOption=BitmapCacheOption.OnLoad;image.EndInit();image.Freeze();return image;}
}
