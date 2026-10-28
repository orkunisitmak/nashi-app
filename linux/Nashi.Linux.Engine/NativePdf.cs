using Nashi.Core;
namespace Nashi.Windows;

public static class NativePdf
{
    public static async Task<Dictionary<int,byte[]>> RenderPagesAsync(string input,IEnumerable<int> pages,int dpi,CancellationToken token,string password="")
    {
        token.ThrowIfCancellationRequested();if(dpi is <36 or >600)throw new ArgumentOutOfRangeException(nameof(dpi));
        using var pdf=UglyToad.PdfPig.PdfDocument.Open(input,new(){Password=password});
        var selected=pages.Distinct().Where(p=>p>=1&&p<=pdf.NumberOfPages).ToArray();
        foreach(var number in selected){var page=pdf.GetPage(number);if(page.Width*dpi/72d*page.Height*dpi/72d>100_000_000)throw new InvalidDataException("A rendered page exceeds 100 MP. Reduce DPI.");}
        var work=Path.Combine(Path.GetTempPath(),"NashiPdfPages-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        if(OperatingSystem.IsLinux())File.SetUnixFileMode(work,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        try{
            var result=new Dictionary<int,byte[]>();long total=0;
            foreach(var number in selected){
                token.ThrowIfCancellationRequested();var prefix=Path.Combine(work,"page");var page=number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var args=new List<string>{"-png","-singlefile","-f",page,"-l",page,"-r",dpi.ToString(System.Globalization.CultureInfo.InvariantCulture)};
                if(password.Length>0)args.AddRange(["-upw",password]);args.AddRange([Path.GetFullPath(input),prefix]);
                await ProcessRunner.RunAsync(SystemTools.Find("pdftoppm")??throw new FileNotFoundException("Install Poppler for PDF rendering."),args,token);
                var path=prefix+".png";total=checked(total+new FileInfo(path).Length);if(total>512_000_000)throw new InvalidDataException("Rendered pages exceed 512 MB. Choose a lower DPI or split the PDF first.");
                result[number]=await File.ReadAllBytesAsync(path,token);File.Delete(path);
            }
            return result;
        }finally{Directory.Delete(work,true);}
    }
    public static async Task<byte[][]> RenderAsync(string input,int dpi,CancellationToken token,string password="")
    {
        token.ThrowIfCancellationRequested();int count;using(var pdf=UglyToad.PdfPig.PdfDocument.Open(input,new(){Password=password}))count=pdf.NumberOfPages;
        var pages=await RenderPagesAsync(input,Enumerable.Range(1,count),dpi,token,password);return pages.OrderBy(p=>p.Key).Select(p=>p.Value).ToArray();
    }
}
