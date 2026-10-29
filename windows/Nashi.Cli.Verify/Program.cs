using Nashi.Cli;
using Nashi.Core;
using System.Security.Cryptography;
using System.Text.Json;

var checks=0;
void Check(bool good,string message) { if(!good) throw new Exception(message);checks++; }
(string output,string error,int status) Run(params string[] arguments)
{
    using var output=new StringWriter();
    using var error=new StringWriter();
    var status=CliRunner.Run(arguments,output,error);
    return (output.ToString(),error.ToString(),status);
}

var (catalog,diagnostic,catalogStatus)=Run("catalog");
Check(catalogStatus==0&&diagnostic=="","Catalog should exit successfully without stderr.");
using(var doc=JsonDocument.Parse(catalog))
{
    var obj=doc.RootElement;
    Check(obj.GetProperty("conversionMatrix").EnumerateObject().Count()==FeatureCatalog.Conversions.Count,
        "Catalog must use the shared conversion matrix, not a copied list.");
    var tools=obj.GetProperty("tools").EnumerateArray().Select(x=>x.GetString()).ToHashSet();
    Check(tools.SetEquals(FeatureCatalog.ToolNames.Keys),"CLI tools must come from the shared tool list.");
}

var folder=Path.Combine(Path.GetTempPath(),"Nashi-Cli-Verify-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var image=Path.Combine(folder,"Turkce spaced ��  image.png");
    PhotoCodec.EncodePixels([255,0,0,255,255,0,0,255,255,0,0,255,255,0,0,255,255,0,0,255,255,0,0,255],2,3,image,"png");
    var sourceDigest=SHA256.HashData(File.ReadAllBytes(image));
    var (imageJson,imageError,imageStatus)=Run("inspect",image);
    Check(imageStatus==0&&imageError=="","Inspect should read a valid image.");
    using(var doc=JsonDocument.Parse(imageJson))
    {
        Check(doc.RootElement.GetProperty("category").GetString()=="image","Image category must match macOS.");
        Check(doc.RootElement.GetProperty("width").GetInt32()==2&&doc.RootElement.GetProperty("height").GetInt32()==3,
            "Image size must be read from the actual file.");
    }
    Check(SHA256.HashData(File.ReadAllBytes(image)).SequenceEqual(sourceDigest),
        "Inspection must leave the source image byte-for-byte unchanged.");

    using(var pdf=new PdfSharp.Pdf.PdfDocument())
    {
        pdf.AddPage();
        pdf.AddPage();
        pdf.Save(Path.Combine(folder,"Synthetic.pdf"));
    }
    var (pdfJson,pdfError,pdfStatus)=Run("inspect",Path.Combine(folder,"Synthetic.pdf"));
    Check(pdfStatus==0&&pdfError=="","Inspect should read a valid PDF.");
    using(var doc=JsonDocument.Parse(pdfJson))
    {
        Check(doc.RootElement.GetProperty("category").GetString()=="pdf","PDF category must match macOS.");
        Check(doc.RootElement.GetProperty("pages").GetInt32()==2,"PDF inspect should count actual pages.");
    }

    var (badOut,badError,badStatus)=Run("inspect",Path.Combine(folder,"nonexistent.txt"),"--json");
    Check(badStatus!=0&&badOut=="","Missing input should not output a success result.");
    using(var doc=JsonDocument.Parse(badError))
        Check(doc.RootElement.GetProperty("error").GetString() is {Length:>0},"JSON errors must be machine-readable.");

    var (invalidOut,invalidError,invalidStatus)=Run("convert","png",image,"--json");
    Check(invalidStatus!=0&&invalidOut=="","Unsupported conversion must not misleadingly succeed.");
    using(var doc=JsonDocument.Parse(invalidError))
        Check(doc.RootElement.GetProperty("error").GetString() is {Length:>0},"Unsupported commands need machine-readable errors.");
    Check(Directory.GetFiles(folder).Length==2,"Read-only CLI commands must not create exports.");

    // New tool command must reuse the core transaction: atomic exports,
    // source preservation, safe names, and machine-readable output.
    var firstPdf=Path.Combine(folder,"Synthetic.pdf");
    var secondPdf=Path.Combine(folder,"Second file.pdf");
    using(var pdf=new PdfSharp.Pdf.PdfDocument())
    {
        pdf.AddPage();
        pdf.Save(secondPdf);
    }
    var firstHash=SHA256.HashData(File.ReadAllBytes(firstPdf));
    var secondHash=SHA256.HashData(File.ReadAllBytes(secondPdf));

    var (mergeJson,mergeError,mergeStatus)=Run("tool","mergePDF",firstPdf,secondPdf,"--json");
    Check(mergeStatus==0&&mergeError=="","A supported headless PDF merge must succeed.");
    string firstMerged;
    using(var doc=JsonDocument.Parse(mergeJson))
    {
        var paths=doc.RootElement.EnumerateArray().Select(p=>p.GetString()!).ToArray();
        Check(paths.Length==1 && Path.GetFileName(paths[0])=="Merged.pdf" && File.Exists(paths[0]),
            "JSON merge results should contain the output path.");
        firstMerged=paths[0];
    }
    using(var document=UglyToad.PdfPig.PdfDocument.Open(firstMerged))
        Check(document.NumberOfPages==3,"Merged PDF must preserve all three pages.");
    Check(SHA256.HashData(File.ReadAllBytes(firstPdf)).SequenceEqual(firstHash)
        && SHA256.HashData(File.ReadAllBytes(secondPdf)).SequenceEqual(secondHash),
        "PDF merge must not modify source files.");

    var (againOut,againErr,againStatus)=Run("tool","mergePDF",firstPdf,secondPdf);
    var secondMerged=againOut.Trim();
    Check(againStatus==0 && againErr=="" && File.Exists(secondMerged)
        && !string.Equals(firstMerged,secondMerged,StringComparison.OrdinalIgnoreCase),
        "Running again must create a unique output without overwriting existing merges.");

    var (notEnoughOut,notEnoughErr,notEnoughStatus)=Run("tool","mergePDF",firstPdf,"--json");
    Check(notEnoughStatus!=0 && notEnoughOut=="" && notEnoughErr.Contains("error"),
        "Missing second PDF must fail and return structured error.");

    var invalid=Path.Combine(folder,"Bad.pdf");
    File.WriteAllText(invalid,"not a PDF");
    var (brokenOut,brokenErr,brokenStatus)=Run("tool","mergePDF",firstPdf,invalid,"--json");
    Check(brokenStatus!=0 && brokenOut=="" && brokenErr.Contains("error"),
        "A corrupt input PDF must fail atomically.");
    Check(Directory.GetFiles(folder,"Merged*.pdf").Length==2,
        "A failed merge must not publish a partial PDF.");
    Check(!Directory.EnumerateDirectories(folder,".nashi-*").Any(),
        "Staging folders must be cleaned after both success and failure.");

    var (wrongOut,wrongErr,wrongStatus)=Run("tool","mergePDF",firstPdf,image,"--json");
    Check(wrongStatus!=0 && wrongOut=="" && wrongErr.Contains("error"),
        "A non-PDF input must not be silently treated as a PDF.");

}
finally{Directory.Delete(folder,true);}
Console.WriteLine(JsonSerializer.Serialize(new {passed=checks,scope="Isolated Windows CLI preview: catalog/inspect/transactional mergePDF; real UI not claimed"}));
