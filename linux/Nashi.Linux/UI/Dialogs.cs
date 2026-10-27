using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
namespace Nashi.Linux.UI;

internal static class Dialogs
{
    internal static Window Shell(string title,Control content,double width=560)
    {
        var window=new Window{Title=L10n.T(title)+" · Nashi",Width=width,SizeToContent=SizeToContent.Height,MaxHeight=760,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false,
            Content=new ScrollViewer{Content=new Border{Padding=new(26),Child=content}}};
        return window.Res(Window.BackgroundProperty,"Paper");
    }
    internal static async Task MessageAsync(Window owner,string title,string message)
    {
        var stack=new StackPanel{Spacing=12};stack.Children.Add(Brand.Text(title,19));
        stack.Children.Add(new SelectableTextBlock{Text=L10n.T(message),TextWrapping=Avalonia.Media.TextWrapping.Wrap}.Res(SelectableTextBlock.ForegroundProperty,"Ink"));
        var window=Shell(title,stack,520);var ok=Brand.Primary("OK",()=>window.Close());ok.HorizontalAlignment=HorizontalAlignment.Right;stack.Children.Add(ok);
        await window.ShowDialog(owner);
    }
    internal static async Task<string?> FolderAsync(Window owner,string title)
    {
        var picked=await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{Title=L10n.T(title),AllowMultiple=false});
        return picked.Count==0?null:picked[0].TryGetLocalPath();
    }
    internal static async Task QrAsync(Window owner,IReadOnlyList<string> results,Func<string,Task> copy)
    {
        var stack=new StackPanel{Spacing=10};stack.Children.Add(Brand.Text("Read QR Codes",26));
        stack.Children.Add(Brand.Text(results.Count==0?"No QR codes found. Try a larger or sharper image.":$"{results.Count} QR {(results.Count==1?"code":"codes")} found.",14,"Muted"));
        foreach(var result in results)
        {
            var content=new StackPanel{Spacing=6};content.Children.Add(new TextBox{Text=result,IsReadOnly=true,TextWrapping=Avalonia.Media.TextWrapping.Wrap,MaxHeight=150});
            var buttons=new WrapPanel();buttons.Children.Add(Brand.Button("Copy",()=>_=copy(result)));
            if(Uri.TryCreate(result,UriKind.Absolute,out var url)&&url.Scheme is "http" or "https")buttons.Children.Add(Brand.Button("Open link",()=>Desktop.Open(url.AbsoluteUri)));
            content.Children.Add(buttons);stack.Children.Add(Brand.Card(content,14));
        }
        var window=Shell("Read QR Codes",stack,640);var close=Brand.Primary("Close",()=>window.Close());close.HorizontalAlignment=HorizontalAlignment.Right;stack.Children.Add(close);
        await window.ShowDialog(owner);
    }
}
