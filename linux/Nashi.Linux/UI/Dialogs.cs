using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
namespace Nashi.Linux.UI;

public static class Dialogs
{
    public static Window? Main=>(Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    private static Window Shell(string title,double width=460)=>new(){Title=L10n.T(title)+" · Nashi",Width=width,SizeToContent=SizeToContent.Height,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false};
    private static async Task<T?> Show<T>(Window? owner,Window dialog)
    {
        owner??=Main;
        if(owner is {IsVisible:true})return await dialog.ShowDialog<T>(owner);
        var done=new TaskCompletionSource<T?>();dialog.Closed+=(_,_)=>done.TrySetResult(default);dialog.Show();return await done.Task;
    }
    public static Task Message(Window? owner,string title,string text)
    {
        var dialog=Shell(title);var ok=Ui.Primary("OK",()=>dialog.Close());ok.IsDefault=true;
        dialog.Content=new StackPanel{Margin=new(24),Spacing=10,Children={Ui.Title(title,20),Ui.Text(text,13),Ui.Row(0,ok)}};
        return Show<object>(owner,dialog);
    }
    public static async Task<bool> Confirm(Window? owner,string title,string text,string accept="Continue")
    {
        var dialog=Shell(title);var yes=Ui.Primary(accept,()=>dialog.Close(true));yes.IsDefault=true;var no=Ui.Button("Cancel",()=>dialog.Close(false));no.IsCancel=true;
        dialog.Content=new StackPanel{Margin=new(24),Spacing=10,Children={Ui.Title(title,20),Ui.Text(text,13),Ui.Row(0,yes,no)}};
        return await Show<bool>(owner,dialog);
    }
    /// A message with a command the user can copy, e.g. how to install LibreOffice.
    public static Task Command(Window? owner,string title,string text,string command)
    {
        var dialog=Shell(title,540);var box=new TextBox{Text=command,IsReadOnly=true,FontFamily=new FontFamily("monospace")};
        var copy=Ui.Button("Copy",async()=>{if(dialog.Clipboard is {} clipboard)await clipboard.SetTextAsync(command);});
        var ok=Ui.Primary("OK",()=>dialog.Close());ok.IsDefault=true;
        dialog.Content=new StackPanel{Margin=new(24),Spacing=10,Children={Ui.Title(title,20),Ui.Text(text,13),box,Ui.Row(0,copy,ok)}};
        return Show<object>(owner,dialog);
    }
    public static Task QrResults(Window? owner,IReadOnlyList<(string File,string Value)> results)
    {
        var dialog=Shell("Read QR Codes",640);dialog.SizeToContent=SizeToContent.Manual;dialog.Height=520;dialog.CanResize=true;
        var stack=new StackPanel{Margin=new(24),Spacing=12};stack.Children.Add(Ui.Title("Read QR Codes",26));
        stack.Children.Add(Ui.Text(results.Count==0?"No QR codes found. Try a larger or sharper image.":$"{results.Count} QR {(results.Count==1?"code":"codes")} found.",14,"muted"));
        foreach(var (file,value) in results)
        {
            var buttons=new WrapPanel();buttons.Children.Add(Ui.Button("Copy",async()=>{if(dialog.Clipboard is {} clipboard)await clipboard.SetTextAsync(value);}));
            if(Uri.TryCreate(value,UriKind.Absolute,out var url)&&url.Scheme is "http" or "https")buttons.Children.Add(Ui.Button("Open link",()=>LinuxShell.Open(url.AbsoluteUri)));
            stack.Children.Add(Ui.Card(new StackPanel{Spacing=6,Children={Ui.Text(file,12,"muted",false),new TextBox{Text=value,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=150},buttons}}));
        }
        var close=Ui.Primary("Done",()=>dialog.Close());close.IsDefault=true;close.IsCancel=true;stack.Children.Add(close);
        dialog.Content=new ScrollViewer{Content=stack};return Show<object>(owner,dialog);
    }
    /// Long text (recognized text) with Copy and Save.
    public static Task TextResult(Window? owner,string title,string text,bool copied)
    {
        var dialog=Shell(title,640);dialog.SizeToContent=SizeToContent.Manual;dialog.Height=520;dialog.CanResize=true;
        var box=new TextBox{Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true};
        var copy=Ui.Button("Copy",async()=>{if(dialog.Clipboard is {} clipboard)await clipboard.SetTextAsync(text);},"chip");
        var save=Ui.Button("Save as text…",async()=>{var file=await dialog.StorageProvider.SaveFilePickerAsync(new(){SuggestedFileName="Text.txt",DefaultExtension="txt"});if(file?.TryGetLocalPath() is {} path)await File.WriteAllTextAsync(path,text);},"chip");
        var done=Ui.Primary("Done",()=>dialog.Close());done.IsDefault=true;done.IsCancel=true;
        var top=Ui.Stack(6,Ui.Title(title,22),Ui.Text(copied?"Copied to the clipboard.":"",12,"muted"));top.Margin=new(0,0,0,10);DockPanel.SetDock(top,Dock.Top);
        var bottom=Ui.Row(0,copy,save,done);bottom.Margin=new(0,10,0,0);DockPanel.SetDock(bottom,Dock.Bottom);
        dialog.Content=new DockPanel{Margin=new(24),Children={top,bottom,box}};return Show<object>(owner,dialog);
    }
    /// One choice from a short list; null when cancelled.
    public static async Task<int?> Pick(Window? owner,string title,string text,string[] options)
    {
        var dialog=Shell(title);var list=new ListBox{ItemsSource=options,SelectedIndex=0,MaxHeight=240};
        var ok=Ui.Primary("Continue",()=>dialog.Close(true));ok.IsDefault=true;var no=Ui.Button("Cancel",()=>dialog.Close(false));no.IsCancel=true;
        dialog.Content=new StackPanel{Margin=new(24),Spacing=10,Children={Ui.Title(title,20),Ui.Text(text,13),list,Ui.Row(0,ok,no)}};
        return await Show<bool>(owner,dialog)?list.SelectedIndex:null;
    }
    public static async Task<string?> Folder(Window owner,string title)
    {
        var folders=await owner.StorageProvider.OpenFolderPickerAsync(new(){Title=L10n.T(title),AllowMultiple=false});
        return folders.Count>0?folders[0].TryGetLocalPath():null;
    }
}
