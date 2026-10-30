using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;
public sealed partial class MainWindow
{
    private readonly OfficeRuntime officeRuntime=new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi","Tools"));
    private readonly MetadataService metadataService=new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi","Tools"));
    private async Task EditMetadataAsync(string[] selected)
    {
        if(selected.All(p=>FeatureCatalog.IsAudio(p)||FeatureCatalog.IsVideo(p)&&FeatureCatalog.Extension(p)!="gif")){await EditMediaMetadataAsync(selected);return;}
        if(metadataService.Executable is null)
        {
            if(MessageBox.Show(this,L10n.Text("Download ExifTool (~11 MB) from its official SourceForge distribution? The SHA-256 is checked and it runs locally without an administrator password. Upstream licenses are retained."),L10n.Text("Set up metadata tools"),MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            SetBusy(true);using var token=new CancellationTokenSource();var toast=new ToastWindow(this,token.Cancel);toast.Show();try{await metadataService.InstallAsync(new Progress<OperationProgress>(toast.Update),token.Token);}finally{toast.Close();SetBusy(false);}
        }
        var removeAll=selected.Length>1;Dictionary<string,string>? changes=null;
        if(selected.Length==1){var values=await metadataService.ReadAsync(selected[0]);var editor=new MetadataEditor(this,values);if(editor.ShowDialog()!=true)return;removeAll=editor.RemoveAll;changes=removeAll?null:editor.Updates;}
        await RunAsync((destination,p,t)=>metadataService.WriteAsync(selected,destination,removeAll,changes,progress:p,token:t),"Metadata");
    }
    internal static string ToolSymbol(string id)=>id switch{"compress"=>"↘","removeMetadata"=>"◇","editImage"=>"☷","annotateImage"=>"✎","frameImage"=>"▣","cropImage" or "cropVideo"=>"⌗","trimAudio" or "trimVideo"=>"✂","muteVideo"=>"♫","normalizeAudio"=>"≋","audioChannels"=>"⇆","audioToVideo"=>"▷","createCollage"=>"▦","redactImage" or "redactVideo" or "redactAudio"=>"▨","readQRCode"=>"▧","mergePDF"=>"▣","splitPDF" or "splitVideo"=>"◫","organizePDF"=>"▤","extractArchive"=>"↗","createPDF"=>"▱","joinVideos"=>"⇥","videoSnapshots"=>"▣","changeVideoSpeed"=>"◴",_=>"↗"};
    private async Task SetupOfficeAsync()
    {
        if(busy)return;if(officeRuntime.Executable is not null){L10n.SetText(status,"Office tools are ready.");return;}
        if(MessageBox.Show(this,L10n.Text("Download LibreOffice 26.8.1 (~360 MB) from The Document Foundation? Its SHA-256 is checked, then the package is extracted into Nashi’s user tools folder. No administrator password or Office subscription is needed. LibreOffice retains its upstream licenses."),L10n.Text("Set up office tools"),MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        SetBusy(true);using var cancellation=new CancellationTokenSource();var toast=new ToastWindow(this,cancellation.Cancel);toast.Show();try{await officeRuntime.InstallAsync(new Progress<OperationProgress>(toast.Update),cancellation.Token);toast.Complete([]);L10n.SetText(status,"Office tools ready.");}catch(OperationCanceledException){toast.Close();L10n.SetText(status,"Setup cancelled.");}catch(Exception error){toast.Close();MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Office setup could not finish"));}finally{SetBusy(false);}
    }
    private async Task EditDocumentAsync(ToolChoice choice,string[] selected,string? requestedFormat=null)
    {
        if(choice.Id=="convert")
        {
            var editor=new ToolEditor(this,"convert","Convert",selected[0],inputs:selected,outputFormats:FeatureCatalog.Formats(selected));editor.ConfigureProfiles(preferences,requestedFormat);if(editor.ShowDialog()!=true)return;
            await RunAsync((destination,p,t)=>new DocumentConversion(officeRuntime,tools).ConvertAsync(selected,editor.Format,destination,editor.DocumentOptions,p,t),"Convert to "+editor.Format.ToUpperInvariant());return;
        }
        if(choice.Id=="organizePDF")
        {
            var organizer=await PdfOrganizer.CreateAsync(this,selected[0]);if(organizer.ShowDialog()!=true)return;
            // Read the organizer's WPF state on the UI thread; the export runs on a worker thread.
            var pages=organizer.Pages;var rotations=organizer.Rotations;var pageRotations=organizer.PageRotations;var uniformWidth=organizer.UniformWidth;
            await RunAsync((destination,p,t)=>Task.Run(()=>DocumentEngine.Pdf("organizePDF",selected,destination,pages,t,rotations,pageRotations:pageRotations,uniformWidth:uniformWidth),t),"Organize PDF");return;
        }
        var dialog=new ToolEditor(this,choice.Id,choice.Title,selected[0],inputs:selected);if(dialog.ShowDialog()!=true)return;if(choice.Id=="compress"){await RunAsync((destination,p,t)=>PdfCompression.RunAsync(selected,destination,dialog.ImageOptions,dialog.DocumentOptions.Password,p,t),"Compress PDF");return;}
        // PagesPerFile and PageRanges read editor text boxes, so capture them before leaving the UI thread.
        var inputs=dialog.OrderedInputs;var pagesPerFile=dialog.PagesPerFile;var password=dialog.DocumentOptions.Password;var pageRanges=dialog.PageRanges;
        await RunAsync((destination,p,t)=>Task.Run(()=>DocumentEngine.Pdf(choice.Id,inputs,destination,null,t,pagesPerFile:pagesPerFile,password:password,pageRanges:pageRanges),t),choice.Title);
    }
    private async Task ReadQrAsync(IReadOnlyList<string> inputs)
    {
        SetBusy(true);var results=new List<(string File,string Value)>();try
        {
            foreach(var file in inputs)
            {
                if(FeatureCatalog.Extension(file)=="pdf"){var pages=await NativePdf.RenderAsync(file,180);for(var n=0;n<pages.Length;n++)foreach(var value in NativePdf.DecodeQr(ImageEngine.FromPng(pages[n])))results.Add((Path.GetFileName(file)+$" · Page {n+1}",value));}
                else foreach(var value in NativePdf.DecodeQr(ImageEngine.Load(file)))results.Add((Path.GetFileName(file),value));
            }
            var window=new Window{Owner=this,Title="Read QR Codes · Nashi",Width=720,Height=520,WindowStartupLocation=WindowStartupLocation.CenterOwner};Theme.Attach(window);var stack=new StackPanel{Margin=new(26)};stack.Children.Add(Text("Read QR Codes",28));stack.Children.Add(Text(results.Count==0?"No QR codes found. Try a larger or sharper image.":$"{results.Count} QR {(results.Count==1?"code":"codes")} found.",14,"Muted"));
            foreach(var result in results){var content=new StackPanel();content.Children.Add(Text(result.File,12,"Muted"));content.Children.Add(new TextBox{Text=result.Value,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=150});var buttons=new WrapPanel();buttons.Children.Add(Button("Copy",()=>{try{Clipboard.SetText(result.Value);}catch(Exception error)when(ClipboardImport.IsClipboardError(error)){MessageBox.Show(window,L10n.Text("Clipboard is busy. Try again."),"Nashi");}}));if(Uri.TryCreate(result.Value,UriKind.Absolute,out var url)&&url.Scheme is "http" or "https")buttons.Children.Add(Button("Open link",()=>OpenUrl(url.AbsoluteUri)));content.Children.Add(buttons);stack.Children.Add(Card(content));}window.Content=new ScrollViewer{Content=stack};window.ShowDialog();
        }finally{SetBusy(false);}
    }
}
