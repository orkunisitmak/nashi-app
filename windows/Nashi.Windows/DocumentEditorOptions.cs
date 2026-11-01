using System.Windows;
using System.Windows.Controls;
using Nashi.Core;
namespace Nashi.Windows;
public sealed partial class ToolEditor
{
    internal string PageRanges=>fields.GetValueOrDefault("Page ranges")?.Text??"";
    internal int PagesPerFile=>(int)Value("Pages per file",1);
    public DocumentOptions DocumentOptions{get;private set;}=new();
    private readonly CheckBox combinePDFs=new(){Content="Combine into one PDF",IsChecked=false,Margin=new(4,12,4,12),Visibility=Visibility.Collapsed};
    private readonly ComboBox documentLayout=new(){ItemsSource=new[]{"editable","preserve"},SelectedIndex=0},epubLayout=new(){ItemsSource=new[]{"reflowable","fixed"},SelectedIndex=0};private readonly PasswordBox archivePassword=new();
    private void BuildDocumentOptions()
    {
        if(tool=="convert"&&OrderedInputs.Length>1&&media is null)
        {
            options.Children.Add(L10n.Content(combinePDFs));
            void UpdatePDFOutput()=>combinePDFs.Visibility=format.SelectedItem as string=="pdf"?Visibility.Visible:Visibility.Collapsed;
            format.SelectionChanged+=(_,_)=>UpdatePDFOutput();UpdatePDFOutput();
        }
        if(tool=="splitPDF"){Field("Pages per file",1);TextField("Page ranges","");}
        if(FeatureCatalog.Extension(input)=="pdf"){options.Children.Add(L10n.Label(new TextBlock{Text="PDF password (if needed)",Margin=new(4,12,4,0)}));options.Children.Add(archivePassword);}
        if(tool is "convert"&&media is null&&bitmap is null)
        {Field("DPI",144);options.Children.Add(L10n.Label(new TextBlock{Text="PDF → Office layout"}));options.Children.Add(documentLayout);options.Children.Add(L10n.Label(new TextBlock{Text="EPUB layout"}));options.Children.Add(epubLayout);}
        if(tool=="extractArchive"||FeatureCatalog.IsArchive(input)){options.Children.Add(L10n.Label(new TextBlock{Text="Archive password (if needed)",Margin=new(4,12,4,0)}));options.Children.Add(archivePassword);}
    }
}
