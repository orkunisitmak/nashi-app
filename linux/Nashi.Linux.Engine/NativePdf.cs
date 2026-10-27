using Nashi.Core;
namespace Nashi.Windows;

public static class NativePdf
{
    public static async Task<byte[][]> RenderAsync(string input, int dpi, CancellationToken token, string password="")
    {
        if(dpi is <36 or >600) throw new ArgumentOutOfRangeException(nameof(dpi));
        using var pdf=UglyToad.PdfPig.PdfDocument.Open(input,new(){Password=password});
        if(pdf.NumberOfPages>500) throw new InvalidDataException("Render up to 500 pages per operation.");
        foreach(var page in pdf.GetPages())
            if(page.Width*dpi/72d*page.Height*dpi/72d>100_000_000) throw new InvalidDataException("A rendered page exceeds 100 MP. Reduce DPI.");
        var work=Path.Combine(Path.GetTempPath(),"NashiPdf-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var args=new List<string>{"-png","-r",dpi.ToString(System.Globalization.CultureInfo.InvariantCulture)};
            if(password.Length>0) args.AddRange(["-upw",password]);
            args.AddRange([Path.GetFullPath(input),Path.Combine(work,"page")]);
            await ProcessRunner.RunAsync(SystemTools.Find("pdftoppm")??throw new FileNotFoundException("Install poppler-utils (Poppler on Fedora/Arch) for PDF previews."),args,token);
            return Directory.GetFiles(work,"page-*.png").OrderBy(p=>int.Parse(Path.GetFileNameWithoutExtension(p)[5..],System.Globalization.CultureInfo.InvariantCulture)).Select(File.ReadAllBytes).ToArray();
        }
        finally { Directory.Delete(work,true); }
    }
}
