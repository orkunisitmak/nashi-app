using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Nashi.Core;

namespace Nashi.Windows;
internal sealed class QRCodeCreatorWindow:Window
{
    private readonly TextBox input=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=90};
    private readonly ComboBox sizes=new(){ItemsSource=new[]{256,512,1024},SelectedItem=512};
    private readonly ComboBox correction=new(){ItemsSource=new[]{"L","M","Q","H"},SelectedItem="M"};
    private readonly Image preview=new(){Width=260,Height=260,Stretch=Stretch.Uniform};
    private readonly TextBlock error=new(){TextWrapping=TextWrapping.Wrap};
    private readonly Button save;
    private readonly Func<bool> canSave;
    private readonly Action<string> saved;
    public QRCodeCreatorWindow(Window owner,Action<string> saved,Func<bool> canSave)
    {
        Owner=owner;this.saved=saved;this.canSave=canSave;Theme.Attach(this);Title=L10n.Text("Create QR Code");Width=620;Height=660;MinWidth=480;MinHeight=620;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var content=new StackPanel{Margin=new(24)};Content=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        content.Children.Add(L10n.Label(new TextBlock{Text="Create QR Code",FontSize=26,FontWeight=FontWeights.Bold}));
        content.Children.Add(Label("Text or URL"));content.Children.Add(input);content.Children.Add(Label("Image size"));content.Children.Add(sizes);content.Children.Add(Label("Error correction"));content.Children.Add(correction);
        var generate=ActionButton("Generate QR Code",Generate);generate.Margin=new(0,12,0,12);content.Children.Add(generate);
        RenderOptions.SetBitmapScalingMode(preview,BitmapScalingMode.NearestNeighbor);content.Children.Add(new Border{Background=System.Windows.Media.Brushes.White,Child=preview,HorizontalAlignment=HorizontalAlignment.Center});content.Children.Add(error);
        content.Children.Add(Label("Created locally. No upload."));save=ActionButton("Save PNG…",Save);save.IsEnabled=false;content.Children.Add(save);
        input.TextChanged+=(_,_)=>Invalidate();sizes.SelectionChanged+=(_,_)=>Invalidate();correction.SelectionChanged+=(_,_)=>Invalidate();
    }
    private static TextBlock Label(string text)=>L10n.Label(new TextBlock{Text=text,Margin=new(0,10,0,5)});
    private static Button ActionButton(string text,Action action){var button=new Button{Margin=new(0,6,0,0),Padding=new(12,7,12,7)};L10n.Bind(button,ContentControl.ContentProperty,text);button.Click+=(_,_)=>action();return button;}
    private void Invalidate(){preview.Source=null;save.IsEnabled=false;error.Text="";}
    private void Generate()
    {
        try{var bytes=QRCodeGenerator.Png(input.Text,(int)sizes.SelectedItem,(string)correction.SelectedItem);using var stream=new MemoryStream(bytes);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();preview.Source=bitmap;save.IsEnabled=true;error.Text="";}
        catch(Exception exception)when(exception is ArgumentException or IOException){Invalidate();L10n.SetText(error,exception.Message);}
    }
    private void Save()
    {
        if(!canSave()||preview.Source is null)return;
        var dialog=new SaveFileDialog{FileName="QR Code.png",Filter="PNG|*.png",DefaultExt=".png"};if(dialog.ShowDialog(this)!=true)return;
        try{var file=QRCodeGenerator.Export(input.Text,Path.GetDirectoryName(dialog.FileName)!,(int)sizes.SelectedItem,(string)correction.SelectedItem,Path.GetFileName(dialog.FileName));saved(file);error.Text="";}
        catch(Exception exception)when(exception is ArgumentException or IOException or UnauthorizedAccessException){L10n.SetText(error,exception.Message);}
    }
}
