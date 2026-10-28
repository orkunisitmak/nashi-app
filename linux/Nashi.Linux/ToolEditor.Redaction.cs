using Avalonia.Controls;
using System.Globalization;
using Nashi.Windows;
namespace Nashi.Linux;

public sealed partial class ToolEditor
{
    internal sealed record OcrLanguage(string Code,string Label){public override string ToString()=>Label;}
    private static string OcrLanguageLabel(string code)=>code switch{
        "chi_sim"=>"Chinese (Simplified)","chi_tra"=>"Chinese (Traditional)",
        _=>CultureInfo.GetCultures(CultureTypes.NeutralCultures).FirstOrDefault(c=>c.ThreeLetterISOLanguageName==code)?.NativeName??code
    };
    private Task? detectionTask;
    private CancellationTokenSource? detectionRun;
    private void BuildRedactionControls(string input)
    {
        Text("RedactionStyle","solid");
        var panel=new StackPanel{Spacing=8};fields.Children.Add(panel);panel.Children.Add(NashiTheme.Text("Areas",16));
        var editing=new WrapPanel();editing.Children.Add(NashiTheme.Button("Undo",surface!.Undo));editing.Children.Add(NashiTheme.Button("Redo",surface.Redo));editing.Children.Add(NashiTheme.Button("Delete selected",surface.DeleteSelected));editing.Children.Add(NashiTheme.Button("Clear",()=>{detectionRun?.Cancel();surface.ClearMarks();}));panel.Children.Add(editing);
        var areas=new ListBox{Name="RedactionAreas",MaxHeight=100};panel.Children.Add(areas);var updating=false;
        void RefreshAreas(){updating=true;try{areas.ItemsSource=surface.RegionBounds.Select((r,i)=>$"{i+1}. {r.X:0}, {r.Y:0} · {r.Width:0} × {r.Height:0}").ToArray();areas.SelectedIndex=surface.SelectedRegion;}finally{updating=false;}}
        surface.SceneChanged+=RefreshAreas;areas.SelectionChanged+=(_,_)=>{if(!updating&&areas.SelectedIndex>=0)surface.SelectRegion(areas.SelectedIndex);};RefreshAreas();
        panel.Children.Add(NashiTheme.Text("OCR language",13));var languages=new ComboBox{Name="OcrLanguage",IsEnabled=false};panel.Children.Add(languages);
        var actions=new WrapPanel();var detect=NashiTheme.Button("Detect text",()=>{});detect.IsEnabled=false;actions.Children.Add(detect);var cancel=NashiTheme.Button("Cancel detection",()=>detectionRun?.Cancel());cancel.IsEnabled=false;actions.Children.Add(cancel);panel.Children.Add(actions);
        var status=NashiTheme.Text("Draw areas, or detect text. Review the areas before saving.",12,true);panel.Children.Add(status);
        async Task LoadLanguages()
        {
            try{var available=await TextDetection.LanguagesAsync(lifetime.Token);languages.ItemsSource=available.Select(code=>new OcrLanguage(code,OcrLanguageLabel(code))).ToArray();languages.SelectedIndex=0;languages.IsEnabled=true;detect.IsEnabled=true;}
            catch(OperationCanceledException){}catch(Exception error){status.Text=error.Message;}
        }
        async Task Detect()
        {
            using var run=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);detectionRun=run;detect.IsEnabled=false;languages.IsEnabled=false;cancel.IsEnabled=true;status.Text="Detecting text…";
            try{
                var words=await TextDetection.DetectAsync(input,(languages.SelectedItem as OcrLanguage)?.Code,run.Token);run.Token.ThrowIfCancellationRequested();
                var added=surface.AddRegions(words.Select(w=>w.Region));status.Text=words.Count==0?"No text areas found. You can still draw areas by hand.":$"{added} new text area{(added==1?"":"s")}. Review the areas before saving.";
            }catch(OperationCanceledException){if(!lifetime.IsCancellationRequested)status.Text="Text detection cancelled.";}
            catch(Exception error){if(!lifetime.IsCancellationRequested)status.Text="Detection: "+error.Message;}
            finally{if(ReferenceEquals(detectionRun,run))detectionRun=null;cancel.IsEnabled=false;languages.IsEnabled=true;detect.IsEnabled=true;}
        }
        detect.Click+=(_,_)=>{if(detectionTask?.IsCompleted!=false)detectionTask=Detect();};Opened+=(_,_)=>detectionTask=LoadLanguages();
    }
}
