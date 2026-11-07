using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Nashi.Core;
namespace Nashi.Windows;

internal sealed class ScreenshotWindow:Window
{
    private readonly Image preview=new(){Stretch=Stretch.Uniform};private readonly TextBox text=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly TextBlock status=MainWindow.Text("Capture an area or paste a screenshot to read its text.",13,"Muted");private readonly Button copyImage,saveImage,copyText,capture;
    private BitmapSource? image;private CancellationTokenSource? recognition;private bool edited,settingText,selecting;private int generation;private readonly Action<IReadOnlyList<string>> saved;
    internal TextBox TextField=>text;internal TextBlock StatusField=>status;internal Button CopyTextButton=>copyText;
    internal Func<Task<BitmapSource?>>? CaptureArea{get;set;}
    internal ScreenshotWindow(Window owner,Action<IReadOnlyList<string>> saved)
    {
        this.saved=saved;Owner=owner;Title="Screenshot & text · Nashi";Width=960;Height=650;MinWidth=740;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterOwner;Theme.Attach(this);
        var root=new DockPanel{Margin=new(24)};Content=root;var heading=new StackPanel();DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);heading.Children.Add(MainWindow.Text("Screenshot & text",26));heading.Children.Add(MainWindow.Text("Capture once. Copy the image or its text.",14,"Muted"));
        var tools=new WrapPanel{Margin=new(0,16,0,16)};heading.Children.Add(tools);capture=MainWindow.Button("Select screen area",async()=>await SelectAreaAsync());tools.Children.Add(capture);tools.Children.Add(MainWindow.Button("Paste image",Paste));
        var footer=new DockPanel{Margin=new(0,18,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(actions,Dock.Right);footer.Children.Add(actions);
        var close=MainWindow.Button("Close",Close);close.IsCancel=true;actions.Children.Add(close);copyImage=MainWindow.Button("Copy image",CopyImage);actions.Children.Add(copyImage);saveImage=MainWindow.Button("Save image…",Save);actions.Children.Add(saveImage);copyText=MainWindow.Primary("Copy text",()=>CopyText());actions.Children.Add(copyText);
        DockPanel.SetDock(status,Dock.Bottom);status.Margin=new(4,12,4,0);status.MinHeight=36;root.Children.Add(status);
        var body=new Grid{Margin=new(0,8,0,0)};body.ColumnDefinitions.Add(new());body.ColumnDefinitions.Add(new(){Width=new(20)});body.ColumnDefinitions.Add(new());root.Children.Add(body);
        var left=new DockPanel();body.Children.Add(left);var imageLabel=MainWindow.Text("Screenshot",14);imageLabel.Margin=new(4,0,4,10);DockPanel.SetDock(imageLabel,Dock.Top);left.Children.Add(imageLabel);var frame=new Border{CornerRadius=new(10),Padding=new(8),Child=preview};frame.SetResourceReference(Border.BackgroundProperty,"Inset");left.Children.Add(frame);
        var right=new DockPanel();Grid.SetColumn(right,2);body.Children.Add(right);var label=new Label{Target=text,Padding=new(4,0,4,10)};label.SetResourceReference(Control.ForegroundProperty,"Ink");L10n.Bind(label,ContentControl.ContentProperty,"Recognized text");DockPanel.SetDock(label,Dock.Top);right.Children.Add(label);right.Children.Add(text);
        AutomationProperties.SetName(text,L10n.Text("Recognized text"));AutomationProperties.SetAutomationId(text,"ScreenshotText");AutomationProperties.SetAutomationId(copyText,"CopyScreenshotText");AutomationProperties.SetName(preview,L10n.Text("Screenshot preview"));AutomationProperties.SetAutomationId(status,"ScreenshotStatus");AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);
        var descriptor=DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty,typeof(TextBlock));EventHandler announce=(_,_)=>{if(IsVisible&&!string.IsNullOrWhiteSpace(status.Text))(UIElementAutomationPeer.FromElement(status)??UIElementAutomationPeer.CreatePeerForElement(status))?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);};descriptor.AddValueChanged(status,announce);
        text.TextChanged+=(_,_)=>{if(!settingText)edited=true;UpdateButtons();};Closed+=(_,_)=>{generation++;recognition?.Cancel();descriptor.RemoveValueChanged(status,announce);};UpdateButtons();
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape&&!selecting){Close();e.Handled=true;}};
    }
    internal async Task SelectAreaAsync()
    {
        if(selecting||CaptureArea is null)return;selecting=true;capture.IsEnabled=false;
        try{var selected=await CaptureArea();if(selected is not null)await SetImageAsync(selected);}
        catch(Exception error){L10n.SetText(status,error.Message);}
        finally{selecting=false;capture.IsEnabled=true;}
    }
    internal async Task SetImageAsync(BitmapSource source,Func<BitmapSource,CancellationToken,Task<string>>? recognize=null)
    {
        if(source.PixelWidth<1||source.PixelHeight<1||(long)source.PixelWidth*source.PixelHeight>100_000_000){L10n.SetText(status,"This image is too large to read.");return;}
        recognition?.Cancel();var pending=new CancellationTokenSource();recognition=pending;var current=++generation;image=source;preview.Source=source;settingText=true;text.Text="";settingText=false;edited=false;UpdateButtons();L10n.SetText(status,"Reading text from the image…");
        try
        {
            var result=await (recognize??((bitmap,token)=>ImageTextRecognition.Recognize(bitmap,token)))(source,pending.Token);if(pending.IsCancellationRequested||current!=generation)return;
            if(!edited){settingText=true;text.Text=result;settingText=false;}L10n.SetText(status,string.IsNullOrWhiteSpace(result)?"No text found. Try a clearer or larger capture.":"Text ready. Review it, then Copy text.");UpdateButtons();
        }
        catch(OperationCanceledException){}catch(Exception error){if(current==generation&&!pending.IsCancellationRequested)L10n.SetText(status,error.Message);}
        finally{if(recognition==pending)recognition=null;pending.Dispose();}
    }
    private void UpdateButtons(){copyImage.IsEnabled=saveImage.IsEnabled=image is not null;copyText.IsEnabled=!string.IsNullOrWhiteSpace(text.Text);}
    private void Paste(){try{var pasted=Clipboard.GetImage();if(pasted is null){L10n.SetText(status,"Copy a screenshot or image first.");return;}_=SetImageAsync(pasted);}catch(Exception error)when(ClipboardImport.IsClipboardError(error)){L10n.SetText(status,"Clipboard is busy. Try again.");}}
    internal void CopyText(Action<string>? write=null){try{if(string.IsNullOrWhiteSpace(text.Text))return;(write??Clipboard.SetText)(text.Text);L10n.SetText(status,"Text copied. Paste it anywhere.");}catch(Exception error)when(ClipboardImport.IsClipboardError(error)){L10n.SetText(status,"Clipboard is busy. Try again.");}}
    private void CopyImage(){try{if(image is null)return;Clipboard.SetImage(image);L10n.SetText(status,"Image copied.");}catch(Exception error)when(ClipboardImport.IsClipboardError(error)){L10n.SetText(status,"Clipboard is busy. Try again.");}}
    internal IReadOnlyList<string> SaveTo(string fileName)
    {
        if(image is null)throw new InvalidOperationException("Capture an area or paste a screenshot to read its text.");
        using var transaction=new ExportTransaction(Path.GetDirectoryName(fileName)!);var path=Path.Combine(transaction.StagingDirectory,Path.GetFileName(fileName));ImageEngine.Save(image,path,"png");var outputs=transaction.Publish([path],CancellationToken.None);saved(outputs);return outputs;
    }
    private void Save()
    {
        if(image is null)return;var picker=new SaveFileDialog{Title=L10n.Text("Save screenshot"),FileName=$"Screenshot {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png",Filter="PNG image (*.png)|*.png",DefaultExt=".png",AddExtension=true,OverwritePrompt=false,InitialDirectory=Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)};
        if(picker.ShowDialog(this)!=true)return;
        try{SaveTo(Path.ChangeExtension(picker.FileName,".png"));L10n.SetText(status,"Screenshot saved. Existing files kept.");}catch(Exception error){L10n.SetText(status,error.Message);}
    }
}
