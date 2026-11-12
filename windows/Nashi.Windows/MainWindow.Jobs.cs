using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Nashi.Core;
namespace Nashi.Windows;
// Work in progress and failures show in the window, under the files they concern, as in the design.
public sealed partial class MainWindow
{
    private Border? jobCard,errorCard;private TextBlock? jobTitle,jobDetail,jobPercent,errorTitle,errorBody;private ProgressBar? jobBar;private System.Windows.Shapes.Path? jobRing;private Action? jobCancel,retry;
    private void BuildJobCards()
    {
        // Working: a ring and a bar for progress, what is happening, and Cancel.
        var row=new DockPanel();jobRing=new System.Windows.Shapes.Path{Width=22,Height=22,StrokeThickness=2.5,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,12,0)};jobRing.SetResourceReference(Shape.StrokeProperty,"Solid");
        jobRing.Margin=new(0);var track=new Ellipse{Width=19.5,Height=19.5,StrokeThickness=2.5,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};track.SetResourceReference(Shape.StrokeProperty,"Sel");var ring=new Grid{Width=22,Height=22,Margin=new(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};ring.Children.Add(track);ring.Children.Add(jobRing);DockPanel.SetDock(ring,Dock.Left);row.Children.Add(ring);
        var cancel=Button("Cancel",()=>jobCancel?.Invoke());cancel.MinHeight=26;cancel.FontSize=12;cancel.Margin=new(10,0,0,0);cancel.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(cancel,Dock.Right);row.Children.Add(cancel);
        jobPercent=Text("",12,"Muted",localize:false);jobPercent.Margin=new(10,0,0,0);DockPanel.SetDock(jobPercent,Dock.Right);row.Children.Add(jobPercent);
        var meta=new StackPanel{VerticalAlignment=VerticalAlignment.Center};jobTitle=Text("",13,localize:false,weight:FontWeights.SemiBold);meta.Children.Add(jobTitle);jobDetail=Text("",11.5,"Muted",localize:false);jobDetail.Margin=new(0,3,0,0);jobDetail.TextTrimming=TextTrimming.CharacterEllipsis;jobDetail.TextWrapping=TextWrapping.NoWrap;meta.Children.Add(jobDetail);row.Children.Add(meta);
        jobBar=new ProgressBar{Minimum=0,Maximum=1,Margin=new(0,12,0,0)};var stack=new StackPanel();stack.Children.Add(row);stack.Children.Add(jobBar);
        jobCard=Card(stack,18,new(16,14,16,14));jobCard.Margin=new(0,12,0,0);jobCard.Visibility=Visibility.Collapsed;
        // Failed: what went wrong in plain words, Try Again and Dismiss.
        var error=new DockPanel();var warn=Icons.Make("warn",22,"Danger");warn.VerticalAlignment=VerticalAlignment.Top;warn.Margin=new(0,0,12,0);DockPanel.SetDock(warn,Dock.Left);error.Children.Add(warn);
        var dismiss=Button("Dismiss",()=>{if(errorCard is not null)errorCard.Visibility=Visibility.Collapsed;});dismiss.Style=Ui.Style("Plain");dismiss.MinHeight=26;dismiss.FontSize=12;dismiss.VerticalAlignment=VerticalAlignment.Top;DockPanel.SetDock(dismiss,Dock.Right);error.Children.Add(dismiss);
        var again=Button("Try Again",()=>{if(errorCard is not null)errorCard.Visibility=Visibility.Collapsed;retry?.Invoke();});again.MinHeight=26;again.FontSize=12;again.Margin=new(10,0,4,0);again.VerticalAlignment=VerticalAlignment.Top;DockPanel.SetDock(again,Dock.Right);error.Children.Add(again);
        var words=new StackPanel();errorTitle=Text("",13,localize:false,weight:FontWeights.SemiBold);words.Children.Add(errorTitle);errorBody=Text("",11.5,"Muted",localize:false);errorBody.Margin=new(0,3,0,0);words.Children.Add(errorBody);error.Children.Add(words);
        errorCard=Card(error,18,new(16,14,16,14));errorCard.Margin=new(0,12,0,0);errorCard.Visibility=Visibility.Collapsed;
        var at=workArea.Children.IndexOf(dropCard)+1;workArea.Children.Insert(at,errorCard);workArea.Children.Insert(at,jobCard);
    }
    private void ShowJob(string label,Action cancel)
    {
        if(jobCard is null)return;jobCancel=cancel;if(errorCard is not null)errorCard.Visibility=Visibility.Collapsed;
        jobTitle!.Text=L10n.Text(label);jobDetail!.Text=files.Count==0?"":files.Count==1?Path.GetFileName(files[0]):$"{Path.GetFileName(files[0])} · {L10n.Text($"{files.Count} files")}";jobPercent!.Text="";jobBar!.IsIndeterminate=true;Ring(0);jobCard.Visibility=Visibility.Visible;
    }
    private void UpdateJob(OperationProgress progress)
    {
        if(jobCard is null)return;jobTitle!.Text=L10n.Text(progress.Label);jobBar!.IsIndeterminate=progress.Fraction is null;
        if(progress.Fraction is {} fraction){jobBar.Value=Math.Clamp(fraction,0,1);jobPercent!.Text=$"{fraction:P0}";Ring(fraction);}
    }
    private void HideJob(){if(jobCard is not null)jobCard.Visibility=Visibility.Collapsed;jobCancel=null;}
    private void ShowError(string title,string message,Action again)
    {
        if(errorCard is null)return;retry=again;errorTitle!.Text=L10n.Text(title);errorBody!.Text=L10n.Text(message);errorCard.Visibility=Visibility.Visible;
    }
    // The progress ring: an arc from the top, clockwise.
    private void Ring(double fraction)
    {
        if(jobRing is null)return;var f=Math.Clamp(fraction,0,.9999);if(f<=0){jobRing.Data=null;return;}var angle=f*2*Math.PI;const double r=8.5,c=11;
        var end=new Point(c+r*Math.Sin(angle),c-r*Math.Cos(angle));var figure=new PathFigure{StartPoint=new(c,c-r)};figure.Segments.Add(new ArcSegment(end,new Size(r,r),0,angle>Math.PI,SweepDirection.Clockwise,true));jobRing.Data=new PathGeometry([figure]);
    }
}
