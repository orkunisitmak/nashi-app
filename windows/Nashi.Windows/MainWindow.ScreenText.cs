using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
namespace Nashi.Windows;
public sealed partial class MainWindow
{
    internal const string ScreenTextTitle="Copy text from screen";
    internal const string TextCopied="Copied to the clipboard. You can edit the text here and copy it again.";
    /// <summary>The user drags over text on the display under the cursor; Windows OCR reads it and the text is copied.</summary>
    internal async Task CopyScreenTextAsync()
    {
        if(busy)return;
        if(ComponentDispatcher.IsThreadModal){L10n.SetText(status,CloseDialogFirst);return;}
        ScreenQr.Screen screen;Int32Rect monitor;
        try{monitor=ScreenText.MonitorUnderCursor();screen=await CaptureWithoutNashiAsync();}
        catch(InvalidOperationException error){MessageBox.Show(L10n.Text(error.Message),"Nashi");return;}
        var picture=ScreenText.Crop(screen,monitor);
        var selector=new ScreenSelectionWindow(picture,monitor);selector.ShowDialog();
        if(selector.Selection is not {} area)return;
        string text;SetBusy(true);
        try{var part=new CroppedBitmap(picture,area);part.Freeze();text=await ScreenText.RecognizeAsync(part);}
        catch(InvalidOperationException error){MessageBox.Show(L10n.Text(error.Message),"Nashi");return;}
        finally{SetBusy(false);}
        if(text.Length==0){MessageBox.Show(L10n.Text(ScreenText.NoText),L10n.Text(ScreenTextTitle));return;}
        var copied=true;try{Clipboard.SetText(text);}catch(Exception error)when(ClipboardImport.IsClipboardError(error)){copied=false;}
        ShowScreenText(text,copied);
    }
    private void ShowScreenText(string text,bool copied)
    {
        var window=new Window{Owner=IsVisible?this:null,Title=L10n.Text(ScreenTextTitle)+" · Nashi",Width=640,Height=460,WindowStartupLocation=IsVisible?WindowStartupLocation.CenterOwner:WindowStartupLocation.CenterScreen};Theme.Attach(window);
        var stack=new StackPanel{Margin=new(26)};stack.Children.Add(Text(ScreenTextTitle,28));
        stack.Children.Add(Text(copied?TextCopied:"Clipboard is busy. Try again.",14,"Muted"));
        var box=new TextBox{Text=text,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=250,Margin=new(0,14,0,10)};stack.Children.Add(box);
        var buttons=new WrapPanel();
        buttons.Children.Add(Button("Copy",()=>{try{Clipboard.SetText(box.Text);}catch(Exception error)when(ClipboardImport.IsClipboardError(error)){MessageBox.Show(window,L10n.Text("Clipboard is busy. Try again."),"Nashi");}}));
        buttons.Children.Add(Button("Close",window.Close));stack.Children.Add(buttons);
        window.Content=stack;window.Loaded+=(_,_)=>{box.Focus();box.SelectAll();};window.Show();window.Activate();
    }
}
