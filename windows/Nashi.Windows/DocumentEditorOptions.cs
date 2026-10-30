using System.Windows;
using System.Windows.Controls;
using Nashi.Core;
namespace Nashi.Windows;
public sealed partial class ToolEditor
{
    internal string PageRanges=>fields.GetValueOrDefault("Page ranges")?.Text??"";
    internal int PagesPerFile=>(int)Value("Pages per file",1);
    public DocumentOptions DocumentOptions{get;private set;}=new();
    private readonly ComboBox documentLayout=new(){ItemsSource=new[]{"editable","preserve"},SelectedIndex=0},epubLayout=new(){ItemsSource=new[]{"reflowable","fixed"},SelectedIndex=0};private readonly PasswordBox archivePassword=new();private TextBlock? splitSummary;private IReadOnlyList<string> splitInputs=[];
    private void BuildDocumentOptions()
    {
        if(tool=="splitPDF")
        {
            splitInputs=collageInputs.Length>0?collageInputs:[input];
            options.Children.Add(L10n.Label(new TextBlock{Text="One PDF per page by default. Enter a larger number to group pages into each new PDF. Leave page ranges blank to split every page; use ranges such as 1-3, 5 to include selected pages only.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,2,4,12),Foreground=Theme.Brush("Muted")}));
            Field("Pages per file",1);TextField("Page ranges","");
            splitSummary=L10n.Label(new TextBlock{Margin=new Thickness(4,10,4,0),FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});options.Children.Add(splitSummary);
            fields["Pages per file"].TextChanged+=(_,_)=>UpdateSplitSummary();fields["Page ranges"].TextChanged+=(_,_)=>UpdateSplitSummary();archivePassword.PasswordChanged+=(_,_)=>UpdateSplitSummary();UpdateSplitSummary();
        }
        if(FeatureCatalog.Extension(input)=="pdf"){options.Children.Add(L10n.Label(new TextBlock{Text="PDF password (if needed)",Margin=new(4,12,4,0)}));options.Children.Add(archivePassword);}
        if(tool is "convert"&&media is null&&bitmap is null)
        {Field("DPI",144);options.Children.Add(L10n.Label(new TextBlock{Text="PDF → Office layout"}));options.Children.Add(documentLayout);options.Children.Add(L10n.Label(new TextBlock{Text="EPUB layout"}));options.Children.Add(epubLayout);}
        if(tool=="extractArchive"||FeatureCatalog.IsArchive(input)){options.Children.Add(L10n.Label(new TextBlock{Text="Archive password (if needed)",Margin=new(4,12,4,0)}));options.Children.Add(archivePassword);}
    }
    private void UpdateSplitSummary()
    {
        if(splitSummary is null)return;
        try
        {
            var perFile=int.Parse(fields["Pages per file"].Text,System.Globalization.CultureInfo.CurrentCulture);
            if(perFile is <1 or >1000)throw new ArgumentOutOfRangeException();
            var count=0;var pagesTotal=0;
            foreach(var path in splitInputs)
            {
                var pageCount=DocumentEngine.PdfPageCount(path,archivePassword.Password);
                var selected=string.IsNullOrWhiteSpace(fields["Page ranges"].Text)?pageCount:DocumentEngine.PageRange(fields["Page ranges"].Text,pageCount).Length;
                if(selected<1)throw new ArgumentException("Select at least one page.");
                pagesTotal+=selected;count+=(selected+perFile-1)/perFile;
            }
            splitSummary.Text=L10n.Language=="tr"?$"{pagesTotal} sayfa · {count} PDF dosyası oluşturulacak.":$"{pagesTotal} pages · {count} PDF file(s) will be created.";
            splitSummary.Foreground=Theme.Brush("Ink");
        }
        catch(Exception)
        {
            splitSummary.Text=L10n.Language=="tr"?"Sayfa aralığını, sayfa sayısını veya PDF parolasını kontrol et.":"Check the page range, pages per file, or PDF password.";
            splitSummary.Foreground=Theme.Brush("Muted");
        }
    }
}
