using Nashi.Core;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using ImageMagick;
internal static class ParityChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        var characterDefaults=Path.Combine(root,"Character style defaults.docx");using(var archive=ZipFile.Open(characterDefaults,ZipArchiveMode.Create))
        {
            void Part(string name,string text){using var writer=new StreamWriter(archive.CreateEntry(name).Open(),new UTF8Encoding(false));writer.Write(text);}
            Part("word/styles.xml","<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:docDefaults><w:rPrDefault><w:rPr><w:highlight w:val=\"yellow\"/><w:sz w:val=\"20\"/></w:rPr></w:rPrDefault></w:docDefaults><w:style w:type=\"paragraph\" w:styleId=\"Body\"><w:rPr><w:highlight w:val=\"cyan\"/><w:sz w:val=\"44\"/></w:rPr></w:style><w:style w:type=\"character\" w:styleId=\"Parent\"><w:rPr><w:i/></w:rPr></w:style><w:style w:type=\"character\" w:styleId=\"Emphasis\"><w:basedOn w:val=\"Parent\"/></w:style></w:styles>");
            Part("word/document.xml","<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:pPr><w:pStyle w:val=\"Body\"/></w:pPr><w:r><w:rPr><w:rStyle w:val=\"Emphasis\"/></w:rPr><w:t>Inherited cyan</w:t></w:r><w:r><w:rPr><w:rStyle w:val=\"Missing\"/></w:rPr><w:t>Unknown style</w:t></w:r><w:r><w:rPr><w:rStyle w:val=\"Emphasis\"/><w:highlight w:val=\"green\"/></w:rPr><w:t>Direct green</w:t></w:r></w:p></w:body></w:document>");
        }
        using(var model=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(OfficeDocuments.ReadDocx(characterDefaults))))
        {
            var runs=model.RootElement.GetProperty("blocks")[0].GetProperty("runs");
            foreach(var index in new[]{0,1}){var character=runs[index].GetProperty("character");var color=character.GetProperty("backgroundColor");check(color.GetProperty("r").GetDouble()==0&&color.GetProperty("g").GetDouble()==1&&color.GetProperty("b").GetDouble()==1&&character.GetProperty("fontSize").GetDouble()==22,"A character style reapplied document defaults over paragraph formatting");}
            check(runs[0].GetProperty("character").GetProperty("italic").GetBoolean()&&runs[2].GetProperty("character").GetProperty("backgroundColor").GetProperty("b").GetDouble()==0,"Character style inheritance or direct highlighting was lost");
        }
        check(FeatureCatalog.Conversions.Count==46&&FeatureCatalog.ToolNames.Count==27,"Mac/Windows catalog count diverged");
        check(FeatureCatalog.Formats(["a.htm","b.html"]).SequenceEqual(new[]{"pdf"})&&FeatureCatalog.Tools(["a.html"]).Length==0,"HTML inputs lost their PDF route");
        check(FeatureCatalog.Formats(["a.jpeg","b.png"]).Contains("webp"),"Mixed image conversion intersection failed");
        check(!FeatureCatalog.Formats(["a.png","b.mp4"]).Any(),"Unrelated inputs offered an invalid format");
        check(FeatureCatalog.Tools(["a.pdf","b.pdf"]).SequenceEqual(new[]{"compress","removeMetadata","splitPDF","mergePDF","readQRCode"}),"Multiple PDF wheel diverged from Mac");
        check(FeatureCatalog.Tools(["a.jpg","b.png"]).Contains("createCollage"),"Mixed image collage missing");
        check(FeatureCatalog.Tools(["a.gif"]).SequenceEqual(new[]{"removeMetadata","readQRCode"})&&FeatureCatalog.Tools(["a.gif","b.png"]).Contains("readQRCode")&&!FeatureCatalog.Tools(["a.mp4"]).Contains("readQRCode"),"GIF wheel lost Read QR Codes, unlike the Mac");
        check(DocumentEngine.PageRange("3-1, 2",3).SequenceEqual(new[]{3,2,1,2}),"PDF descending page ranges or duplicates changed");
        // Hand-written content streams keep these fixtures independent of font resolution, and a fixed A4
        // page keeps them independent of the region (PdfSharp picks Letter on US machines).
        string Pdf(string name,string content,Action<PdfSharp.Pdf.PdfDocument>? edit=null,Action<PdfSharp.Pdf.PdfDocument,PdfSharp.Pdf.PdfDictionary>? resource=null)
        {
            var path=Path.Combine(root,name);using var pdf=new PdfSharp.Pdf.PdfDocument();var page=pdf.AddPage();page.Size=PdfSharp.PageSize.A4;
            var font=new PdfSharp.Pdf.PdfDictionary(pdf);font.Elements.SetName("/Type","/Font");font.Elements.SetName("/Subtype","/Type1");font.Elements.SetName("/BaseFont","/Helvetica");pdf.Internals.AddObject(font);
            var fonts=new PdfSharp.Pdf.PdfDictionary(pdf);fonts.Elements.SetReference("/F1",font);var resources=new PdfSharp.Pdf.PdfDictionary(pdf);resources.Elements["/Font"]=fonts;page.Elements["/Resources"]=resources;resource?.Invoke(pdf,resources);
            var stream=new PdfSharp.Pdf.PdfDictionary(pdf);stream.CreateStream(Encoding.ASCII.GetBytes(content));pdf.Internals.AddObject(stream);page.Elements.SetReference("/Contents",stream);edit?.Invoke(pdf);pdf.Save(path);return path;
        }
        void Catalog(PdfSharp.Pdf.PdfDocument pdf,string key,PdfSharp.Pdf.PdfItem value)=>pdf.Internals.Catalog.Elements[key]=value;
        var textPdf=Pdf("Text.pdf","BT /F1 24 Tf 72 700 Td (Nashi fixture) Tj ET");var vectorPdf=Pdf("Vector.pdf","0 0 1 rg 72 72 200 200 re f");
        var titledPdf=Pdf("Titled.pdf","BT /F1 24 Tf 72 700 Td (Report) Tj ET",pdf=>{pdf.Info.Title="Rapor";pdf.Info.Author="Ayşe Yılmaz";pdf.Info.Subject="Satış";pdf.Info.Keywords="q3";});
        using(var compressed=PdfSharp.Pdf.IO.PdfReader.Open(DocumentEngine.Pdf("compress",[titledPdf],root)[0],PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))check(compressed.Info is {Title:"Rapor",Author:"Ayşe Yılmaz",Subject:"Satış",Keywords:"q3"},"Compressing a PDF dropped its title, author, subject or keywords");
        // Like the Mac, Compress keeps a PDF's password and permissions; a rebuilt copy was saved unprotected.
        foreach(var (userPassword,typed) in new[]{("user-secret","user-secret"),("user-secret","owner-secret"),("","")})
        {
            var protectedPdf=Pdf("Protected "+typed.Length+userPassword.Length+".pdf","BT /F1 24 Tf 72 700 Td (Private) Tj ET",pdf=>{if(userPassword.Length>0)pdf.SecuritySettings.UserPassword=userPassword;pdf.SecuritySettings.OwnerPassword="owner-secret";pdf.SecuritySettings.PermitPrint=false;});
            var protectedCompressed=DocumentEngine.Pdf("compress",[protectedPdf],root,password:typed)[0];var opensWithoutPassword=true;try{using var _=PdfSharp.Pdf.IO.PdfReader.Open(protectedCompressed,PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);}catch(PdfSharp.Pdf.IO.PdfReaderException){opensWithoutPassword=false;}
            using var reopened=PdfSharp.Pdf.IO.PdfReader.Open(protectedCompressed,userPassword,PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            check(opensWithoutPassword==(userPassword.Length==0)&&reopened.SecurityHandler.Elements.ContainsKey("/Filter")&&reopened.PageCount==1,"Compressing a protected PDF saved a copy without its password or permissions");
        }
        using(var cleaned=PdfSharp.Pdf.IO.PdfReader.Open(DocumentEngine.Pdf("removeMetadata",[titledPdf],root)[0],PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))check(cleaned.Info is {Title:"",Author:"",Subject:"",Keywords:""},"Remove Metadata kept the title, author, subject or keywords");
        var textHtml=OfficeDocuments.PdfHtml(textPdf,"",(_,_)=>[9],out var textBlank);var vectorHtml=OfficeDocuments.PdfHtml(vectorPdf,"",(_,area)=>area is null?[1,2,3]:null,out var vectorBlank);
        check(textBlank.Length==0&&textHtml.Contains("Nashi")&&!textHtml.Contains("data:image")&&vectorBlank.SequenceEqual(new[]{1})&&vectorHtml.Contains("data:image/png;base64,AQID"),"A PDF page without extractable text or images converted to a blank page");
        // macOS (Quartz) PDFs set "1 Tf" and scale the text matrix; the drawn size is what matters.
        check(OfficeDocuments.PdfHtml(Pdf("Scaled text.pdf","BT /F1 1 Tf 24 0 0 24 72 700 Tm (Scaled) Tj ET")).Contains("<span style=\"font-size:24pt;\">Scaled</span>"),"Editable PDF text drawn through a scaled text matrix lost its size");
        // A zero font size under an overflowing matrix gives a NaN drawn size; keep the default instead of writing "NaNpt".
        check(OfficeDocuments.PdfHtml(Pdf("Overflowing text.pdf","1e300 0 0 1e300 0 0 cm BT /F1 0 Tf 1e300 0 0 1e300 72 700 Tm (Overflow) Tj ET")).Contains("<span style=\"font-size:12pt;\">Overflow</span>"),"Editable PDF text wrote a NaN font size");
        // A JPEG behind a Flate filter: PDFium draws it, PdfPig cannot hand it over.
        var tinyJpeg=System.Convert.FromBase64String("/9j/4AAQSkZJRgABAgAAAQABAAD/2wBDAAgKCgsKCw0NDQ0NDRAPEBAQEBAQEBAQEBASEhIVFRUSEhIQEBISFBQVFRcXFxUVFRUXFxkZGR4eHBwjIyQrKzP/xABLAAEBAAAAAAAAAAAAAAAAAAAAAgEBAAAAAAAAAAAAAAAAAAAABhABAAAAAAAAAAAAAAAAAAAAABEBAAAAAAAAAAAAAAAAAAAAAP/AABEIAAgACAMBIgACEQADEQD/2gAMAwEAAhEDEQA/AIAFDV//2Q==");
        byte[] chained;using(var packed=new MemoryStream()){using(var zlib=new ZLibStream(packed,CompressionLevel.Optimal,true))zlib.Write(tinyJpeg);chained=packed.ToArray();}
        var mixedPdf=Pdf("Text and image.pdf","BT /F1 24 Tf 72 760 Td (Caption) Tj ET q 200 0 0 100 72 500 cm /Im1 Do Q",resource:(pdf,resources)=>{var image=new PdfSharp.Pdf.PdfDictionary(pdf);image.Elements.SetName("/Type","/XObject");image.Elements.SetName("/Subtype","/Image");image.Elements.SetInteger("/Width",8);image.Elements.SetInteger("/Height",8);image.Elements.SetName("/ColorSpace","/DeviceRGB");image.Elements.SetInteger("/BitsPerComponent",8);image.Elements["/Filter"]=new PdfSharp.Pdf.PdfArray(pdf,new PdfSharp.Pdf.PdfName("/FlateDecode"),new PdfSharp.Pdf.PdfName("/DCTDecode"));image.CreateStream(chained);pdf.Internals.AddObject(image);var objects=new PdfSharp.Pdf.PdfDictionary(pdf);objects.Elements.SetReference("/Im1",image);resources.Elements["/XObject"]=objects;});
        OfficeDocuments.PdfArea? asked=null;var mixedHtml=OfficeDocuments.PdfHtml(mixedPdf,"",(_,area)=>{asked=area;return area is null?null:[4,5,6];},out var mixedPages);
        check(mixedPages.SequenceEqual(new[]{1})&&asked is {Left:72,Width:200,Height:100} found&&Math.Abs(found.Top-(842-600))<.5&&mixedHtml.Contains("Caption")&&mixedHtml.Contains("data:image/png;base64,BAUG"),"An image PdfPig cannot decode vanished from a page with text: "+asked);
        var large=new byte[20_000_001];check(OfficeDocuments.PdfHtml(mixedPdf,"",(_,area)=>area is null?null:large,out _).Contains(Convert.ToBase64String(large)),"A large render of an image area was dropped although whole-page renders keep it");
        PdfSharp.Pdf.PdfDictionary Form(PdfSharp.Pdf.PdfDocument pdf,bool field){var form=new PdfSharp.Pdf.PdfDictionary(pdf);var fields=new PdfSharp.Pdf.PdfArray(pdf);if(field){var item=new PdfSharp.Pdf.PdfDictionary(pdf);item.Elements.SetString("/T","Name");pdf.Internals.AddObject(item);fields.Elements.Add(item.Reference!);}form.Elements["/Fields"]=fields;pdf.Internals.AddObject(form);return form;}
        var formPdf=Pdf("Form.pdf","BT /F1 24 Tf 72 700 Td (Form) Tj ET",pdf=>pdf.Internals.Catalog.Elements.SetReference("/AcroForm",Form(pdf,true)));
        var emptyFormPdf=Pdf("Empty form.pdf","BT /F1 24 Tf 72 700 Td (No fields) Tj ET",pdf=>pdf.Internals.Catalog.Elements.SetReference("/AcroForm",Form(pdf,false)));
        check(DocumentEngine.Pdf("splitPDF",[emptyFormPdf],root).Count==1&&DocumentEngine.Pdf("organizePDF",[emptyFormPdf],root,[1],uniformWidth:true).Count==1,"A PDF with an empty AcroForm and no fields was refused");
        var attachmentPdf=Pdf("Attachment.pdf","BT /F1 24 Tf 72 700 Td (Files) Tj ET",pdf=>{var names=new PdfSharp.Pdf.PdfDictionary(pdf);names.Elements["/EmbeddedFiles"]=new PdfSharp.Pdf.PdfDictionary(pdf);Catalog(pdf,"/Names",names);});
        var taggedPdf=Pdf("Tagged.pdf","BT /F1 24 Tf 72 700 Td (Tags) Tj ET",pdf=>Catalog(pdf,"/StructTreeRoot",new PdfSharp.Pdf.PdfDictionary(pdf)));
        bool Refused(Func<IReadOnlyList<string>> run){try{run();return false;}catch(InvalidDataException error){return error.Message.EndsWith("No copy was exported.",StringComparison.Ordinal);}}
        check(new[]{formPdf,attachmentPdf}.All(pdf=>Refused(()=>DocumentEngine.Pdf("splitPDF",[pdf],root))&&Refused(()=>DocumentEngine.Pdf("organizePDF",[pdf],root,[1]))&&Refused(()=>DocumentEngine.Pdf("removeMetadata",[pdf],root))&&Refused(()=>DocumentEngine.Pdf("mergePDFs",[textPdf,pdf],root))),"Rebuilding a PDF silently dropped its form fields or attachments");
        // A name typed for a combined PDF: safe stem, own extension, reserved names and paths neutralised.
        check(SafePaths.FileStem("Tax papers 2026.pdf","Merged",".pdf")=="Tax papers 2026"&&SafePaths.FileStem("  ..\\evil/name?.PDF ","Merged",".pdf")=="..evilname"&&SafePaths.FileStem("","Merged",".pdf")=="Merged"&&SafePaths.FileStem("CON","Merged",".pdf")=="CON_"&&SafePaths.FileStem("report v1.2","Merged",".pdf")=="report v1.2"&&SafePaths.FileStem("CON.report","Merged",".pdf")=="CON_.report"&&SafePaths.FileStem("COM\u00b9","Merged",".pdf")=="COM\u00b9_"&&SafePaths.FileStem("lpt\u00b2.txt","Merged",".pdf")=="lpt\u00b2_.txt"&&SafePaths.FileStem("a?b*c:d","Merged",".pdf")=="abcd"&&SafePaths.FileStem("one\ntwo\tthree","Merged",".pdf")=="onetwothree"&&SafePaths.FileStem(new string('a',300),"x").Length==120,"File name stem was not made safe");
        var named=DocumentEngine.Pdf("mergePDFs",[textPdf,vectorPdf],root,outputName:"My combined file.pdf");
        check(named.Count==1&&Path.GetFileName(named[0])=="My combined file.pdf"&&Path.GetDirectoryName(named[0])==root,"Merged PDF ignored the typed name");
        check(DocumentEngine.Pdf("splitPDF",[taggedPdf],root).Count==1&&DocumentEngine.Pdf("mergePDFs",[textPdf,vectorPdf],root).Count==1&&DocumentEngine.Pdf("removeMetadata",[taggedPdf],root).Count==1,"Tagged PDFs without forms or attachments were refused");
        var outlinePdf=Pdf("Outline.pdf","BT /F1 24 Tf 72 700 Td (Chapter 1) Tj ET",pdf=>{var page=pdf.AddPage();page.Size=PdfSharp.PageSize.A4;pdf.Outlines.Add("Bölüm 2",page).Top=500;pdf.Info.Title="Private title";});
        using(var cleaned=PdfSharp.Pdf.IO.PdfReader.Open(DocumentEngine.Pdf("removeMetadata",[outlinePdf],root)[0],PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))check(cleaned.Info.Title==""&&cleaned.Outlines.Count==1&&cleaned.Outlines[0] is {Title:"Bölüm 2",Top:500} chapter&&chapter.DestinationPage==cleaned.Pages[1],"Metadata cleanup dropped the PDF's bookmarks");
        // Page labels (i, ii, Ön-5…) number pages by position and cleanup keeps every page in order, so they survive it; the Mac refuses such files.
        PdfSharp.Pdf.PdfDictionary LabelNode(PdfSharp.Pdf.PdfDocument pdf,string key,PdfSharp.Pdf.PdfArray value){var node=new PdfSharp.Pdf.PdfDictionary(pdf);pdf.Internals.AddObject(node);node.Elements[key]=value;return node;}
        var labelledPdf=Pdf("Page labels.pdf","BT /F1 24 Tf 72 700 Td (Preface) Tj ET",pdf=>
        {
            pdf.AddPage();pdf.AddPage();pdf.Info.Title="Private title";var roman=new PdfSharp.Pdf.PdfDictionary(pdf);roman.Elements.SetName("/S","/r");var appendix=new PdfSharp.Pdf.PdfDictionary(pdf);appendix.Elements["/P"]=new PdfSharp.Pdf.PdfString("Ön-");appendix.Elements.SetName("/S","/D");appendix.Elements.SetInteger("/St",5);
            var leaf=LabelNode(pdf,"/Nums",new PdfSharp.Pdf.PdfArray(pdf,new PdfSharp.Pdf.PdfInteger(0),roman,new PdfSharp.Pdf.PdfInteger(2),appendix));leaf.Elements["/Kids"]=new PdfSharp.Pdf.PdfArray(pdf,leaf.Reference!);
            var labels=new PdfSharp.Pdf.PdfDictionary(pdf);labels.Elements["/Kids"]=new PdfSharp.Pdf.PdfArray(pdf,leaf.Reference!);Catalog(pdf,"/PageLabels",labels);
        });
        using(var cleaned=PdfSharp.Pdf.IO.PdfReader.Open(DocumentEngine.Pdf("removeMetadata",[labelledPdf],root)[0],PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
        {
            var labelLeaf=(cleaned.Internals.Catalog.Elements.GetDictionary("/PageLabels")?.Elements.GetArray("/Kids")?.Elements[0] as PdfSharp.Pdf.Advanced.PdfReference)?.Value as PdfSharp.Pdf.PdfDictionary;var labelNumbers=labelLeaf?.Elements.GetArray("/Nums");
            check(cleaned.Info.Title==""&&labelNumbers is {Elements.Count:4}&&labelNumbers.Elements.GetInteger(2)==2&&labelNumbers.Elements.GetDictionary(1)?.Elements.GetName("/S")=="/r"&&labelNumbers.Elements.GetDictionary(3) is {} appendixLabel&&appendixLabel.Elements.GetString("/P")=="Ön-"&&appendixLabel.Elements.GetInteger("/St")==5&&(labelLeaf!.Elements.GetArray("/Kids")?.Elements[0] as PdfSharp.Pdf.Advanced.PdfReference)?.Value==labelLeaf,"Metadata cleanup dropped or changed the PDF's custom page labels");
        }
        var deepLabelsPdf=Pdf("Deep page labels.pdf","BT /F1 24 Tf 72 700 Td (Deep) Tj ET",pdf=>{var node=LabelNode(pdf,"/Nums",new PdfSharp.Pdf.PdfArray(pdf));for(var level=0;level<40;level++)node=LabelNode(pdf,"/Kids",new PdfSharp.Pdf.PdfArray(pdf,node.Reference!));Catalog(pdf,"/PageLabels",node.Reference!);});
        check(Refused(()=>DocumentEngine.Pdf("removeMetadata",[deepLabelsPdf],root)),"Metadata cleanup exported a PDF whose page labels it could not copy");
        var namedPdf=Pdf("Named outline.pdf","BT /F1 24 Tf 72 700 Td (Chapter 1) Tj ET",pdf=>
        {
            var page=pdf.AddPage();PdfSharp.Pdf.PdfDictionary Indirect(){var item=new PdfSharp.Pdf.PdfDictionary(pdf);pdf.Internals.AddObject(item);return item;}
            var (outlines,first,second,third,leaf)=(Indirect(),Indirect(),Indirect(),Indirect(),Indirect());outlines.Elements.SetReference("/First",first);outlines.Elements.SetReference("/Last",third);outlines.Elements.SetInteger("/Count",3);
            first.Elements["/Title"]=new PdfSharp.Pdf.PdfString("Name tree");first.Elements.SetReference("/Parent",outlines);first.Elements.SetReference("/Next",second);first.Elements["/Dest"]=new PdfSharp.Pdf.PdfString("chapter.2");
            var action=new PdfSharp.Pdf.PdfDictionary(pdf);action.Elements.SetName("/S","/GoTo");action.Elements.SetName("/D","/Second");second.Elements["/Title"]=new PdfSharp.Pdf.PdfString("Dests");second.Elements.SetReference("/Parent",outlines);second.Elements.SetReference("/Prev",first);second.Elements.SetReference("/Next",third);second.Elements["/A"]=action;
            var remote=new PdfSharp.Pdf.PdfDictionary(pdf);remote.Elements.SetName("/S","/GoToR");remote.Elements["/F"]=new PdfSharp.Pdf.PdfString("other.pdf");remote.Elements["/D"]=new PdfSharp.Pdf.PdfString("chapter.2");third.Elements["/Title"]=new PdfSharp.Pdf.PdfString("Other file");third.Elements.SetReference("/Parent",outlines);third.Elements.SetReference("/Prev",second);third.Elements["/A"]=remote;
            var wrapper=new PdfSharp.Pdf.PdfDictionary(pdf);wrapper.Elements["/D"]=new PdfSharp.Pdf.PdfArray(pdf,page.Reference!,new PdfSharp.Pdf.PdfName("/Fit"));leaf.Elements["/Names"]=new PdfSharp.Pdf.PdfArray(pdf,new PdfSharp.Pdf.PdfString("chapter.2"),wrapper);
            var tree=new PdfSharp.Pdf.PdfDictionary(pdf);tree.Elements["/Kids"]=new PdfSharp.Pdf.PdfArray(pdf,leaf.Reference!);var names=new PdfSharp.Pdf.PdfDictionary(pdf);names.Elements["/Dests"]=tree;
            var dests=new PdfSharp.Pdf.PdfDictionary(pdf);dests.Elements["/Second"]=new PdfSharp.Pdf.PdfArray(pdf,page.Reference!,new PdfSharp.Pdf.PdfName("/XYZ"),new PdfSharp.Pdf.PdfInteger(0),new PdfSharp.Pdf.PdfInteger(500),new PdfSharp.Pdf.PdfInteger(0));
            var catalog=pdf.Internals.Catalog.Elements;catalog["/Outlines"]=outlines.Reference;catalog["/Names"]=names;catalog["/Dests"]=dests;
        });
        using(var cleaned=PdfSharp.Pdf.IO.PdfReader.Open(DocumentEngine.Pdf("removeMetadata",[namedPdf],root)[0],PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import)){check(cleaned.Outlines.Count==3&&cleaned.Outlines[0] is {Title:"Name tree"} fromTree&&fromTree.DestinationPage==cleaned.Pages[1]&&cleaned.Outlines[1] is {Title:"Dests",Top:500} fromDests&&fromDests.DestinationPage==cleaned.Pages[1],"Metadata cleanup dropped the targets of bookmarks that use named destinations");check(cleaned.Outlines.Count==3&&cleaned.Outlines[2] is {Title:"Other file",DestinationPage:null},"Metadata cleanup pointed a bookmark into another file at a page of this one");}
        var entities=OfficeDocuments.NumericEntities("<p>a&nbsp;b &amp; &mdash;&lt;&unknown;</p>");
        check(entities=="<p>a&#160;b &amp; &#8212;&lt;&unknown;</p>"&&System.Xml.Linq.XElement.Parse(OfficeDocuments.NumericEntities("<p>a&nbsp;b&hellip;</p>")).Value=="a b…","XHTML named entities still break EPUB parsing");
        string Chapter(byte[] data)=>System.Xml.Linq.XDocument.Parse(OfficeDocuments.NumericEntities(OfficeDocuments.XmlText(data))).Root!.Value;
        check(Chapter([..Encoding.ASCII.GetBytes("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?><p>caf"),0xe9,..Encoding.ASCII.GetBytes("&nbsp;</p>")])=="café "&&Chapter(Encoding.UTF8.GetBytes("<?xml version='1.0'?><p>Şubat&mdash;</p>"))=="Şubat—"&&Chapter([..Encoding.Unicode.GetPreamble(),..Encoding.Unicode.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-16\"?><p>Ğ&hellip;</p>")])=="Ğ…","EPUB chapter text ignored the encoding in its XML declaration");
        var unsupported=false;try{OfficeDocuments.XmlText(Encoding.ASCII.GetBytes("<?xml version=\"1.0\" encoding=\"x-nashi-unknown\"?><p/>"));}catch(System.Xml.XmlException){unsupported=true;}
        check(Chapter(Encoding.Unicode.GetBytes("<?xml version=\"1.0\"?><p>\u011e&hellip;</p>"))=="\u011e\u2026"&&Chapter(Encoding.BigEndianUnicode.GetBytes("<p>\u011e</p>".Insert(0,"<?xml version=\"1.0\"?>")))=="\u011e"&&unsupported&&Chapter([..Encoding.ASCII.GetBytes("<?xml version=\"1.0\""+new string(' ',600)+"encoding=\"windows-1252\"?><p>"),0x80,..Encoding.ASCII.GetBytes("</p>")])=="€","EPUB chapters in UTF-16 without a byte order mark or with a long declaration failed, or an unsupported encoding was read as UTF-8");
        bool Invalid(byte[] data){try{OfficeDocuments.XmlText(data);return false;}catch(System.Xml.XmlException){return true;}}
        var utf32=new UTF32Encoding(false,true);
        check(Invalid([..Encoding.ASCII.GetBytes("<p>caf"),0xe9,..Encoding.ASCII.GetBytes("</p>")])&&Invalid([..Encoding.Unicode.GetPreamble(),0x3c,0,0,0xd8,0x3e,0])&&Chapter([..utf32.GetPreamble(),..utf32.GetBytes("<p>\u011e&hellip;</p>")])=="\u011e\u2026"&&Chapter([..Encoding.UTF8.GetPreamble(),..Encoding.UTF8.GetBytes("<p>\u015e</p>")])=="\u015e","EPUB chapter bytes that are invalid in their encoding were replaced instead of refused, or a UTF-32 or UTF-8 byte order mark was misread");
        check(Chapter(new UTF32Encoding(false,false).GetBytes("<?xml version=\"1.0\"?><p>\u011e</p>"))=="\u011e"&&Chapter(new UTF32Encoding(true,false).GetBytes("<p>\u011e</p>"))=="\u011e"&&Chapter(Encoding.Unicode.GetBytes("<p>\u011e</p>"))=="\u011e"&&Chapter(Encoding.BigEndianUnicode.GetBytes("<p>\u011e</p>"))=="\u011e","EPUB chapters in UTF-32, or in UTF-16 without a declaration, and without a byte order mark were read as UTF-8");
        check(Chapter(Encoding.UTF8.GetBytes("<p><![CDATA[a&mdash;b]]>&mdash;<!-- &nbsp; --><?nashi &nbsp;?></p>"))=="a&mdash;b\u2014"&&OfficeDocuments.NumericEntities("<!-- &nbsp; --><?nashi &nbsp;?>")=="<!-- &nbsp; --><?nashi &nbsp;?>","Named entities inside CDATA, comments or processing instructions were rewritten");
        foreach(var (flag,enabled,implicitType) in new[]{("1",true,false),("true",true,false),("on",true,false),("0",false,false),("false",false,false),("off",false,false),("1",true,true)})
        {
            var defaultDocx=Path.Combine(root,"Default paragraph "+flag+" "+implicitType+".docx");using(var archive=ZipFile.Open(defaultDocx,ZipArchiveMode.Create))
            {
                void Part(string name,string text){using var writer=new StreamWriter(archive.CreateEntry(name).Open(),new UTF8Encoding(false));writer.Write(implicitType?text.Replace(" w:type=\"paragraph\" w:default="," w:default=",StringComparison.Ordinal):text);}
                Part("word/styles.xml","<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Arial\"/><w:sz w:val=\"20\"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after=\"60\"/></w:pPr></w:pPrDefault></w:docDefaults><w:style w:type=\"character\" w:default=\"1\" w:styleId=\"DefaultCharacter\"><w:rPr><w:sz w:val=\"198\"/></w:rPr></w:style><w:style w:type=\"paragraph\" w:styleId=\"Parent\"><w:pPr><w:spacing w:after=\"240\"/></w:pPr><w:rPr><w:rFonts w:ascii=\"Courier New\"/><w:sz w:val=\"44\"/></w:rPr></w:style><w:style w:type=\"paragraph\" w:default=\""+flag+"\" w:styleId=\"CustomBody\"><w:basedOn w:val=\"Parent\"/><w:rPr><w:b/></w:rPr></w:style><w:style w:type=\"paragraph\" w:styleId=\"Heading\"><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:rPr><w:sz w:val=\"36\"/></w:rPr></w:style></w:styles>");
                Part("word/document.xml","<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Default body</w:t></w:r></w:p><w:p><w:pPr><w:spacing w:after=\"0\"/></w:pPr><w:r><w:rPr><w:sz w:val=\"28\"/></w:rPr><w:t>Direct override</w:t></w:r></w:p><w:p><w:pPr><w:pStyle w:val=\"Heading\"/></w:pPr><w:r><w:t>Explicit heading</w:t></w:r></w:p></w:body></w:document>");
            }
            using var model=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(OfficeDocuments.ReadDocx(defaultDocx)));var blocks=model.RootElement.GetProperty("blocks");
            var body=blocks[0].GetProperty("runs")[0].GetProperty("character");
            check(body.GetProperty("fontSize").GetDouble()==(enabled?22:10)&&body.GetProperty("fontName").GetString()==(enabled?"Courier New":"Arial")&&blocks[0].GetProperty("paragraph").GetProperty("spaceAfter").GetDouble()==(enabled?12:3),"Word's default paragraph style flag '"+flag+"' lost inherited typography or spacing");
            check(blocks[1].GetProperty("runs")[0].GetProperty("character").GetProperty("fontSize").GetDouble()==14&&blocks[1].GetProperty("paragraph").GetProperty("spaceAfter").GetDouble()==0,"A default paragraph style overrode direct formatting");
            check(blocks[2].GetProperty("runs")[0].GetProperty("character").GetProperty("fontSize").GetDouble()==18&&blocks[2].GetProperty("paragraph").GetProperty("alignment").GetInt32()==2,"A default paragraph style replaced an explicit paragraph style");
        }
        var preferences=new PreferencesStore(Path.Combine(root,"Preferences"));preferences.Save(preferences.Value with{Feedback=false,WheelScale=1.2,FloatBehavior="addToExisting"});preferences.SetProfile("jpg",new(Quality:.7));var persisted=new PreferencesStore(Path.Combine(root,"Preferences"));check(!persisted.Value.Feedback&&persisted.Value.WheelScale==1.2&&persisted.Profile("jpg").Quality==.7,"Windows settings/export defaults did not persist");try{persisted.Save(persisted.Value with{ToolsModifier="Shift"});throw new Exception("Conflicting drag shortcuts accepted");}catch(ArgumentException){check(true,"Drag shortcut collision validation");}
        var oversized=Path.Combine(root,"Oversized preferences");Directory.CreateDirectory(oversized);File.WriteAllText(Path.Combine(oversized,"preferences.json"),new string(' ',1_000_001));var oversizedSettings=new PreferencesStore(oversized);check(oversizedSettings.RecoveryNotice is not null&&oversizedSettings.Value.Language=="en"&&oversizedSettings.Value.Enabled,"Oversized settings crashed startup or did not fall back to defaults");
        check(WebMediaPayload.Parse(null,null,"https://nvfile/C:/Program%20Files/NVIDIA%20Corporation/NVIDIA%20App/osc/index.html#/") is null&&WebMediaPayload.Parse("http://localhost/video.mp4") is null&&WebMediaPayload.Parse("http://[::1]/video.mp4") is null&&WebMediaPayload.Parse("http://[2001:db8::1]/video.mp4") is {Kind:"media"},"A local app page under the drag start point was treated as web media");
        check(WebMediaPayload.Parse("https://i.ytimg.com/vi/mXS5W7_kdzk/hqdefault.jpg") is {Kind:"youtube",Url:"https://www.youtube.com/watch?v=mXS5W7_kdzk"},"YouTube home thumbnail did not recover its video");
        check(WebMediaPayload.Parse("https://music.youtube.com/watch?v=mXS5W7_kdzk&list=synthetic") is {Kind:"youtube",Url:"https://www.youtube.com/watch?v=mXS5W7_kdzk"},
            "YouTube Music share links did not resolve to the video/thumbnail card.");
        check(WebMediaPayload.Parse(null,"<a href=https://youtu.be/mXS5W7_kdzk>Video</a>") is {Kind:"youtube",Url:"https://www.youtube.com/watch?v=mXS5W7_kdzk"},"Unquoted HTML video link was lost");
        check(WebMediaPayload.Parse(null,"<img src=https://cdn.example.invalid/photo.png>") is {Kind:"image"},"Unquoted HTML image source was lost");
        foreach(var (link,kind,canonical) in new[]{("https://m.instagram.com/p/C1a2B3c4D5e/","instagramPhotos","https://www.instagram.com/p/C1a2B3c4D5e/"),("https://www.instagram.com/natgeo/reel/C1a2B3c4D5e/","instagramReel","https://www.instagram.com/reel/C1a2B3c4D5e/"),("https://instagram.com/some.user_1/p/C1a2B3c4D5e/","instagramPhotos","https://www.instagram.com/p/C1a2B3c4D5e/")})
            check(WebMediaPayload.Parse(link) is {} parsed&&parsed.Kind==kind&&parsed.Url==canonical,$"Instagram mobile or profile link not recognized like on the Mac: {link}");
        check(WebMediaPayload.Parse("https://m.instagram.com/natgeo/")?.Kind is not ("instagramPhotos" or "instagramReel"),"An Instagram profile page was taken for a post");
        check(WebMediaPayload.Parse("https://cdn.example/image.jpg","<a href=\"https://www.instagram.com/p/FixtureCode/\"><img src=\"https://cdn.example/image.jpg\"/></a>") is {Kind:"instagramPhotos"},"Instagram grid image chose video/thumbnail");check(WebMediaPayload.Parse("https://www.instagram.com/reel/FixtureCode/") is {Kind:"instagramReel"},"Reel classification failed");check(WebMediaPayload.Parse("https://cdn.example/image.jpg") is {Kind:"image"},"Direct image classification failed");
        var info=WebDownload.ParseInfo("{\"title\":\"Nashi fixture\",\"duration\":3,\"formats\":[{\"height\":720},{\"height\":1080},{\"height\":2160,\"has_drm\":true}]}","fixture");check(info.Heights.SequenceEqual(new[]{720,1080})&&info.Duration==3,"Web inspection included DRM format or lost quality choices");
        var partialInfo=WebDownload.ParseInfo("{\"title\":123,\"live_status\":false,\"vcodec\":17,\"formats\":[null,{\"height\":720,\"vcodec\":17},{\"height\":1080,\"vcodec\":\"h264\"}]}","fixture");
        check(partialInfo.Title=="Web media"&&partialInfo.Heights.SequenceEqual(new[]{720,1080})&&!partialInfo.AudioOnly,
            "Malformed optional yt-dlp metadata must not hide playable formats.");try{WebDownload.ParseInfo("{\"is_live\":true}","fixture");throw new Exception("Live stream accepted");}catch(InvalidDataException){check(true,"Web live stream validation");}
        var photoFixture="{\"entries\":[{\"thumbnails\":[{\"url\":\"https://scontent.cdninstagram.com/photo.jpg?stp=c0.0.720.720a_s640x640\",\"width\":640,\"height\":640},{\"url\":\"https://scontent.cdninstagram.com/original.jpg?sig=keep\",\"width\":1440,\"height\":1920}]},{\"duration\":5,\"formats\":[{\"url\":\"https://scontent.cdninstagram.com/video.mp4\"}]}]}";var photos=WebPhotos.Instagram(photoFixture);check(photos.Length==1&&photos[0].Url.EndsWith("original.jpg?sig=keep",StringComparison.Ordinal),"Instagram carousel did not choose full-frame photo or skipped video incorrectly");
        var partialPhotos=WebPhotos.Instagram("{\"entries\":[{\"duration\":null,\"thumbnails\":[null,{\"url\":42},{\"url\":\"https://cdninstagram.com/synthetic-photo.jpg\",\"width\":900,\"height\":600}]}]}");
        check(partialPhotos.Length==1&&partialPhotos[0].Width==900&&partialPhotos[0].Height==600,
            "Malformed Instagram thumbnail variants hid a valid public image.");
        // YouTube's hover preview is an animated thumbnail under /an_webp/; like on the Mac, it stands for its video.
        check(WebMediaPayload.Parse("https://i.ytimg.com/an_webp/mXS5W7_kdzk/mqdefault_6s.webp?du=3000&sqp=synthetic") is {Kind:"youtube",Url:"https://www.youtube.com/watch?v=mXS5W7_kdzk"},"YouTube hover preview was downloaded as an image instead of its video");
        // Embeds often use YouTube's older thumbnail hosts; the Mac maps img.youtube.com and ytimg.com to the video too.
        foreach(var thumbnail in new[]{"https://img.youtube.com/vi/mXS5W7_kdzk/maxresdefault.jpg","https://ytimg.com/vi/mXS5W7_kdzk/hqdefault.jpg"})
            check(WebMediaPayload.Parse(thumbnail) is {Kind:"youtube",Url:"https://www.youtube.com/watch?v=mXS5W7_kdzk"} parsed&&parsed.Thumbnail==thumbnail,$"YouTube thumbnail was downloaded as an image instead of its video: {thumbnail}");
        check(WebMediaPayload.Parse("https://img.youtube.com/favicon.jpg") is {Kind:"image"}&&WebMediaPayload.Parse("https://img.youtube.com.evil.example/vi/mXS5W7_kdzk/0.jpg") is {Kind:"image"},"A non-thumbnail image was taken for a YouTube video");
        check(LegacyTextCodec.Decode(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Türkçe 🍐")).ToArray())=="Türkçe 🍐","UTF-16 text lost Unicode");
        check(LegacyTextCodec.Decode(new UTF32Encoding(false,true).GetPreamble().Concat(Encoding.UTF32.GetBytes("Türkçe 🍐")).ToArray())=="Türkçe 🍐","UTF-32 text was read as UTF-16");
        // Files from Turkish Windows, 7-Zip and Excel: ANSI 1254 text, OEM 857 ZIP names, semicolon CSV.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);var culture=System.Globalization.CultureInfo.CurrentCulture;var codePages=LegacyTextCodec.SystemCodePages;
        try
        {
            LegacyTextCodec.SystemCodePages=(1252,437);check(LegacyTextCodec.Decode(new byte[]{0x63,0x61,0x66,0xe9})=="café","Legacy text decoding failed");
            LegacyTextCodec.SystemCodePages=(1254,857);System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
            var turkish=LegacyTextCodec.Decode(Encoding.GetEncoding(1254).GetBytes("1\n00:00:01,000 --> 00:00:02,000\nŞimdi ığdır, İzmir\n"));
            check(turkish.Contains("Şimdi ığdır, İzmir")&&SubtitleCodec.Convert(turkish,"vtt").Contains("Şimdi ığdır, İzmir"),"Turkish ANSI text or subtitles were decoded as Western European");
            var legacyZip=Path.Combine(root,"Legacy names.zip");using(var stream=File.Create(legacyZip))using(var archive=new ZipArchive(stream,ZipArchiveMode.Create,false,Encoding.GetEncoding(857)))foreach(var name in new[]{"Şubat.txt","Ğubat.txt"})using(var writer=new StreamWriter(archive.CreateEntry(name).Open()))writer.Write(name);
            // macOS Archive Utility writes UTF-8 names without setting the UTF-8 flag.
            var macZip=Path.Combine(root,"Mac names.zip");using(var stream=File.Create(macZip))using(var archive=new ZipArchive(stream,ZipArchiveMode.Create,false,Encoding.Latin1))using(var writer=new StreamWriter(archive.CreateEntry(Encoding.Latin1.GetString(Encoding.UTF8.GetBytes("Şubat.txt"))).Open()))writer.Write("Şubat");
            var legacy=ArchiveEngine.Extract(legacyZip,root)[0];var mac=ArchiveEngine.Extract(macZip,root)[0];
            check(File.Exists(Path.Combine(legacy,"Şubat.txt"))&&File.Exists(Path.Combine(legacy,"Ğubat.txt"))&&File.Exists(Path.Combine(mac,"Şubat.txt")),"ZIP names without the UTF-8 flag lost Turkish letters");
            var excel=Path.Combine(root,"Excel.csv");File.WriteAllText(excel,"\"Ad;Soyad\";Tutar\nAyşe;1,5\n");var plain=Path.Combine(root,"Plain.csv");File.WriteAllText(plain,"\"a;b\",c,d\n");
            var notes=Path.Combine(root,"Notes.csv");File.WriteAllText(notes,"name,notes;detail;status\nAda,a;b;c\n");
            check(OfficeDocuments.DelimitedLayout(excel,"csv")==(59,1055)&&OfficeDocuments.DelimitedLayout(plain,"csv")==(44,1033)&&OfficeDocuments.DelimitedLayout(notes,"csv")==(44,1033),"Semicolon CSV from Turkish Excel was read as comma-separated English, or a comma CSV with semicolons in it as semicolon-separated");
            var streamed=Path.Combine(root,"Streamed.csv");var normalized=Path.Combine(root,"Streamed UTF-8.csv");const string table="Ad;Şehir\nAyşe;İzmir\n";
            foreach(var bytes in new[]{Encoding.GetEncoding(1254).GetBytes(table),Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(table)).ToArray(),Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(table)).ToArray(),new UTF32Encoding(true,true).GetPreamble().Concat(new UTF32Encoding(true,false).GetBytes(table)).ToArray()})
            {File.WriteAllBytes(streamed,bytes);LegacyTextCodec.ToUtf8Async(streamed,normalized).GetAwaiter().GetResult();check(LegacyTextCodec.Decode(bytes)==table&&File.ReadAllText(normalized)==table&&File.ReadAllBytes(normalized).Length==Encoding.UTF8.GetByteCount(table),"Streamed text normalization did not match Decode");}
        }
        finally{System.Globalization.CultureInfo.CurrentCulture=culture;LegacyTextCodec.SystemCodePages=codePages;}
        foreach(var (color,rgb) in new[]{("black","0,0,0"),("blue","0,0,255"),("cyan","0,255,255"),("green","0,255,0"),("magenta","255,0,255"),("red","255,0,0"),("yellow","255,255,0"),("white","255,255,255"),("darkBlue","0,0,128"),("darkCyan","0,128,128"),("darkGreen","0,128,0"),("darkMagenta","128,0,128"),("darkRed","128,0,0"),("darkYellow","128,128,0"),("darkGray","128,128,128"),("lightGray","192,192,192")})
        {
            var highlighted=Path.Combine(root,"Highlight "+color+".docx");
            using(var archive=ZipFile.Open(highlighted,ZipArchiveMode.Create))
            {
                using var writer=new StreamWriter(archive.CreateEntry("word/document.xml").Open(),new UTF8Encoding(false));
                writer.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:rPr><w:highlight w:val=\""+color+"\"/></w:rPr><w:t>Highlighted text</w:t></w:r></w:p></w:body></w:document>");
            }
            using var model=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(OfficeDocuments.ReadDocx(highlighted)));
            check(OfficeDocuments.ModelHtml(model.RootElement).Contains("background-color:rgb("+rgb+")"),"Word highlight color '"+color+"' was lost in the interchange");
        }
        // Word keeps a link's #section in w:anchor; like the Mac, it is rejoined (percent-encoded) unless the target already has a fragment.
        var anchoredLinks=Path.Combine(root,"Anchored links.docx");using(var archive=ZipFile.Open(anchoredLinks,ZipArchiveMode.Create))
        {
            void Part(string name,string text){using var writer=new StreamWriter(archive.CreateEntry(name).Open(),new UTF8Encoding(false));writer.Write(text);}
            Part("word/_rels/document.xml.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rA\" Target=\"https://example.com/page\" TargetMode=\"External\"/><Relationship Id=\"rF\" Target=\"https://example.com/page#kept\" TargetMode=\"External\"/></Relationships>");
            string Link(string attributes,string text)=>"<w:hyperlink "+attributes+"><w:r><w:t>"+text+"</w:t></w:r></w:hyperlink>";
            Part("word/document.xml","<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><w:body><w:p>"+Link("r:id=\"rA\" w:anchor=\"part-2\"","A")+Link("r:id=\"rA\" w:anchor=\"Bölüm 2\"","B")+Link("r:id=\"rF\" w:anchor=\"other\"","C")+Link("r:id=\"rA\"","D")+Link("w:anchor=\"Bookmark\"","E")+"</w:p></w:body></w:document>");
        }
        using(var anchoredModel=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(OfficeDocuments.ReadDocx(anchoredLinks))))
        {
            var anchoredRuns=string.Join(" ",anchoredModel.RootElement.GetProperty("blocks")[0].GetProperty("runs").EnumerateArray().Select(r=>r.GetProperty("text").GetString()+"="+(r.GetProperty("link").ValueKind==System.Text.Json.JsonValueKind.String?r.GetProperty("link").GetString():"none")));
            check(anchoredRuns=="A=https://example.com/page#part-2 B=https://example.com/page#B%C3%B6l%C3%BCm%202 C=https://example.com/page#kept D=https://example.com/page E=none","Pages interchange lost the #section of a Word hyperlink: "+anchoredRuns);
        }
        var clearedHighlight=Path.Combine(root,"Cleared highlight.docx");using(var archive=ZipFile.Open(clearedHighlight,ZipArchiveMode.Create))
        {
            void Part(string name,string text){using var writer=new StreamWriter(archive.CreateEntry(name).Open(),new UTF8Encoding(false));writer.Write(text);}
            Part("word/styles.xml","<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:style w:type=\"paragraph\" w:styleId=\"Marked\"><w:rPr><w:highlight w:val=\"yellow\"/></w:rPr></w:style></w:styles>");
            Part("word/document.xml","<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:pPr><w:pStyle w:val=\"Marked\"/></w:pPr><w:r><w:t>Inherited</w:t></w:r><w:r><w:rPr><w:highlight w:val=\"none\"/></w:rPr><w:t>Cleared</w:t></w:r></w:p></w:body></w:document>");
        }
        // A Pages header or footer can have several lines; like the Mac (and table cells), each line break is kept in the HTML that Office imports.
        string HeaderFooterHtml(string header,string footer){using var model=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new{header,footer,blocks=Array.Empty<object>()}));return OfficeDocuments.ModelHtml(model.RootElement);}
        check(HeaderFooterHtml("Header one\nHeader two","<b>Footer</b>\nline 2\nline 3").Contains("<header>Header one<br/>Header two</header><footer>&lt;b&gt;Footer&lt;/b&gt;<br/>line 2<br/>line 3</footer>",StringComparison.Ordinal)&&HeaderFooterHtml("Only header","Only footer").Contains("<header>Only header</header><footer>Only footer</footer>",StringComparison.Ordinal),"Pages header or footer line breaks were collapsed into spaces");
        using(var model=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(OfficeDocuments.ReadDocx(clearedHighlight))))
        {
            var runs=model.RootElement.GetProperty("blocks")[0].GetProperty("runs");var inherited=runs[0].GetProperty("character").GetProperty("backgroundColor");
            check(inherited.GetProperty("r").GetDouble()==1&&inherited.GetProperty("g").GetDouble()==1&&inherited.GetProperty("b").GetDouble()==0,"A Word style's highlight was not inherited");
            check(runs[1].GetProperty("character").GetProperty("backgroundColor").ValueKind==System.Text.Json.JsonValueKind.Null,"Explicitly cleared Word highlighting remained inherited");
            var paragraph=System.Xml.Linq.XDocument.Parse(OfficeDocuments.ModelHtml(model.RootElement)).Descendants("p").Single();var spans=paragraph.Elements("span").ToArray();
            check(!paragraph.Attribute("style")!.Value.Contains("background-color:")&&spans[0].Attribute("style")!.Value.Contains("background-color:rgb(255,255,0)")&&!spans[1].Attribute("style")!.Value.Contains("background-color:"),"Explicitly cleared Word highlighting remained visible behind the run");
        }
        var clipping=LegacyTextCodec.Clipping(Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><plist><dict><key>public.utf8-plain-text</key><data>VMO8cmvDp2U=</data></dict></plist>"));check(Encoding.UTF8.GetString(clipping.Bytes)=="Türkçe","Text clipping plist lost content");
        var binary=LegacyTextCodec.Clipping(System.Convert.FromBase64String("YnBsaXN0MDDRAQJfEBZwdWJsaWMudXRmOC1wbGFpbi10ZXh0aQBUAPwAcgBrAOcAZQAg2DzfUAgLJAAAAAAAAAEBAAAAAAAAAAMAAAAAAAAAAAAAAAAAAAA3"));check(Encoding.UTF8.GetString(binary.Bytes)=="Türkçe 🍐","Binary text clipping lost Unicode");
        // Like the Mac, a manual page break (w:br w:type="page") splits the paragraph and the next part starts a new page; other breaks stay line breaks.
        using(var pageBreakModel=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(OfficeDocuments.ReadDocx(Nested("Page breaks.docx","<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:t>One</w:t><w:br w:type=\"page\"/><w:t>Two</w:t><w:br/><w:t>soft</w:t><w:br w:type=\"textWrapping\"/><w:t>wrap</w:t><w:lastRenderedPageBreak/><w:cr/></w:r></w:p><w:p><w:r><w:br w:type=\"page\"/></w:r></w:p><w:p><w:pPr><w:pageBreakBefore/></w:pPr><w:r><w:t>Chapter</w:t></w:r></w:p>")))))
        {
            var pageBreakBlocks=pageBreakModel.RootElement.GetProperty("blocks").EnumerateArray().Select(b=>string.Concat(b.GetProperty("runs").EnumerateArray().Select(r=>r.GetProperty("text").GetString()))+(b.GetProperty("paragraph").TryGetProperty("pageBreakBefore",out var before)&&before.GetBoolean()?"[page]":"")+(b.GetProperty("paragraph").TryGetProperty("alignment",out var alignment)?"@"+alignment.GetInt32():"")).ToArray();
            check(string.Join("|",pageBreakBlocks)=="One@2|Two\nsoft\nwrap\n[page]@2||[page]|Chapter[page]","Pages interchange turned a Word page break into a line break: "+string.Join("|",pageBreakBlocks));
        }
        var docx=Path.Combine(root,"Styled.docx");using(var archive=ZipFile.Open(docx,ZipArchiveMode.Create))
        {
            void Part(string name,string text){using var writer=new StreamWriter(archive.CreateEntry(name).Open(),new UTF8Encoding(false));writer.Write(text);}
            Part("word/document.xml","<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><w:body><w:p><w:pPr><w:pStyle w:val=\"Heading\"/><w:jc w:val=\"center\"/></w:pPr><w:hyperlink r:id=\"link1\"><w:r><w:rPr><w:i/><w:color w:val=\"FF0000\"/></w:rPr><w:t>Pear link</w:t></w:r></w:hyperlink></w:p><w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:headerReference r:id=\"header1\"/></w:sectPr></w:body></w:document>");
            Part("word/styles.xml","<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:style w:styleId=\"Heading\"><w:rPr><w:b/><w:sz w:val=\"36\"/></w:rPr></w:style></w:styles>");Part("word/_rels/document.xml.rels","<Relationships><Relationship Id=\"link1\" Target=\"https://example.com/pear\" TargetMode=\"External\"/><Relationship Id=\"header1\" Target=\"header1.xml\"/></Relationships>");Part("word/header1.xml","<w:hdr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:r><w:t>Fruit header</w:t></w:r></w:p></w:hdr>");
        }
        using(var model=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(OfficeDocuments.ReadDocx(docx)))){var html=OfficeDocuments.ModelHtml(model.RootElement);check(html.Contains("font-weight:bold")&&html.Contains("font-size:18pt")&&html.Contains("font-style:italic")&&html.Contains("text-align:center"),"Pages interchange lost inherited typography or alignment");check(html.Contains("href=\"https://example.com/pear\"")&&html.Contains("Fruit header")&&html.Contains("size:612pt 792pt"),"Pages interchange lost hyperlinks, header or page dimensions");}
        // Word stores a non-breaking hyphen as its own w:noBreakHyphen element; like the Mac, it is U+2011 in the Pages text.
        using(var hyphenModel=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(OfficeDocuments.ReadDocx(Nested("Non-breaking hyphen.docx","<w:p><w:r><w:t>555</w:t><w:noBreakHyphen/><w:t>0100</w:t></w:r></w:p>")))))
            check(string.Concat(hyphenModel.RootElement.GetProperty("blocks")[0].GetProperty("runs").EnumerateArray().Select(r=>r.GetProperty("text").GetString()))=="555\u20110100","Pages interchange dropped a Word non-breaking hyphen");
        var controls=Path.Combine(root,"Controls.docx");using(var archive=ZipFile.Open(controls,ZipArchiveMode.Create)){using var writer=new StreamWriter(archive.CreateEntry("word/document.xml").Open(),new UTF8Encoding(false));writer.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:sdt><w:sdtPr><w:docPartObj><w:docPartGallery w:val=\"Table of Contents\"/></w:docPartObj></w:sdtPr><w:sdtContent><w:p><w:r><w:t>Contents entry</w:t></w:r></w:p></w:sdtContent></w:sdt><w:customXml><w:p><w:r><w:t>Custom block</w:t></w:r></w:p></w:customXml><w:tbl><w:tr><w:tc><w:sdt><w:sdtContent><w:p><w:r><w:t>Cell choice</w:t></w:r></w:p></w:sdtContent></w:sdt></w:tc></w:tr></w:tbl></w:body></w:document>");}
        var controlled=System.Text.Json.JsonSerializer.Serialize(OfficeDocuments.ReadDocx(controls));check(controlled.Contains("Contents entry")&&controlled.Contains("Custom block")&&controlled.Contains("Cell choice"),"Pages interchange dropped text inside Word content controls");
        // Like the Mac, runs inside simple fields, custom XML, tracked moves and direction wrappers are text; deleted and moved-away text is not.
        using(var wrapperModel=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(OfficeDocuments.ReadDocx(Nested("Inline wrappers.docx","<w:p><w:r><w:t>Page </w:t></w:r><w:fldSimple w:instr=\"PAGE\"><w:r><w:t>7</w:t></w:r></w:fldSimple><w:customXml><w:r><w:t> custom</w:t></w:r></w:customXml><w:moveTo><w:r><w:t> moved</w:t></w:r></w:moveTo><w:dir w:val=\"rtl\"><w:r><w:t> עברית</w:t></w:r></w:dir><w:bdo w:val=\"rtl\"><w:r><w:t> bdo</w:t></w:r></w:bdo><w:moveFrom><w:r><w:t> away</w:t></w:r></w:moveFrom><w:del><w:r><w:delText> deleted</w:delText></w:r></w:del></w:p>")))))
            check(string.Concat(wrapperModel.RootElement.GetProperty("blocks")[0].GetProperty("runs").EnumerateArray().Select(r=>r.GetProperty("text").GetString()))=="Page 7 custom moved עברית bdo","Pages interchange dropped Word runs inside fields, custom XML, moves or direction wrappers");
        string Nested(string name,string body,string? header=null){var path=Path.Combine(root,name);using var archive=ZipFile.Open(path,ZipArchiveMode.Create);void Entry(string entry,string xml){using var writer=new StreamWriter(archive.CreateEntry(entry).Open(),new UTF8Encoding(false));writer.Write(xml);}
            Entry("word/document.xml","<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><w:body>"+body+(header is null?"":"<w:p/><w:sectPr><w:headerReference r:id=\"rH\"/></w:sectPr>")+"</w:body></w:document>");
            if(header is not null){Entry("word/_rels/document.xml.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rH\" Target=\"header1.xml\"/></Relationships>");Entry("word/header1.xml","<w:hdr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"+header+"</w:hdr>");}return path;}
        string Deep(int levels,string open,string close,string inner="<w:r><w:t>Deep text</w:t></w:r>")=>string.Concat(Enumerable.Repeat(open,levels))+inner+string.Concat(Enumerable.Repeat(close,levels));
        bool TooDeep(string path){try{OfficeDocuments.ReadDocx(path);return false;}catch(InvalidDataException error){return error.Message.Contains("too deeply",StringComparison.Ordinal);}}
        var deepText="<w:r><w:t>"+Deep(300,"<w:x>","</w:x>","Deep text")+"</w:t></w:r>";
        check(System.Text.Json.JsonSerializer.Serialize(OfficeDocuments.ReadDocx(Nested("Nested controls.docx","<w:p>"+Deep(20,"<w:sdt><w:sdtContent>","</w:sdtContent></w:sdt>")+"</w:p>"+Deep(20,"<w:sdt><w:sdtContent>","</w:sdtContent></w:sdt>","<w:p><w:r><w:t>Body text</w:t></w:r></w:p>")))) is var shallow&&shallow.Contains("Deep text")&&shallow.Contains("Body text"),"Pages interchange rejected a document with a few nested content controls");
        check(TooDeep(Nested("Deep body.docx",Deep(150,"<w:sdt><w:sdtContent>","</w:sdtContent></w:sdt>","<w:p/>")))&&TooDeep(Nested("Deep cell.docx","<w:tbl><w:tr><w:tc>"+Deep(300,"<w:customXml>","</w:customXml>","<w:p/>")+"</w:tc></w:tr></w:tbl>"))&&TooDeep(Nested("Deep paragraph.docx","<w:p>"+Deep(300,"<w:smartTag>","</w:smartTag>")+"</w:p>"))&&TooDeep(Nested("Deep run.docx","<w:p>"+deepText+"</w:p>"))&&TooDeep(Nested("Deep header.docx","<w:p/>","<w:p>"+deepText+"</w:p>")),"Pages interchange accepted content nested deeper than it can safely read");
        check(TooDeep(Nested("Stack bomb.docx","<w:p><w:r><w:t>"+Deep(50_000,"<w:x>","</w:x>","Deep text")+"</w:t></w:r></w:p>")),"Pages interchange did not reject a deeply nested document before overflowing the stack");
        // Word table merges as the Mac reads them: a vertical merge stops at a wider, missing or narrower cell, a spanning restart keeps its width,
        // w:gridBefore shifts a row right, and legacy w:hMerge continuations join their restart cell. Rows read "texts|…", merges "row,column,rows x columns".
        string MergeCell(string text,string properties="")=>"<w:tc><w:tcPr>"+properties+"</w:tcPr><w:p><w:r><w:t>"+text+"</w:t></w:r></w:p></w:tc>";
        var mergedTables=0;string MergedTable(string name,params string[] rows)
        {
            using var model=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(OfficeDocuments.ReadDocx(Nested(name+" "+ ++mergedTables+".docx","<w:tbl>"+string.Concat(rows.Select(r=>"<w:tr>"+r+"</w:tr>"))+"</w:tbl>"))));var table=model.RootElement.GetProperty("blocks")[0];
            return string.Join("|",table.GetProperty("rows").EnumerateArray().Select(r=>string.Join(",",r.EnumerateArray().Select(c=>c.GetProperty("text").GetString()))))+";"+string.Join(" ",table.GetProperty("merges").EnumerateArray().Select(m=>$"{m.GetProperty("row")},{m.GetProperty("column")},{m.GetProperty("rowCount")}x{m.GetProperty("columnCount")}"));
        }
        const string vRestart="<w:vMerge w:val=\"restart\"/>",vContinue="<w:vMerge/>",spanTwo="<w:gridSpan w:val=\"2\"/>";
        foreach(var (mergeRows,mergeExpected) in new[]
        {
            (new[]{MergeCell("A")+MergeCell("B",vRestart),MergeCell("C",spanTwo),MergeCell("D")+MergeCell("",vContinue)},"A,B|C,|D,;1,0,1x2"),
            (new[]{MergeCell("A")+MergeCell("B",vRestart),MergeCell("C"),MergeCell("E")+MergeCell("",vContinue)},"A,B|C|E,;"),
            (new[]{MergeCell("A",vRestart)+MergeCell("B"),MergeCell("",vContinue)+MergeCell("C"),MergeCell("",vContinue)+MergeCell("D")},"A,B|,C|,D;0,0,3x1"),
            (new[]{MergeCell("A",spanTwo+vRestart),MergeCell("",vContinue)+MergeCell("Neighbour")},"A,|,Neighbour;0,0,1x2"),
            (new[]{MergeCell("A",spanTwo+vRestart),MergeCell("",spanTwo+vContinue)},"A,|,;0,0,2x2"),
            (new[]{MergeCell("A",spanTwo+vRestart),MergeCell("B")+MergeCell("C")},"A,|B,C;0,0,1x2"),
            (new[]{MergeCell("X")+MergeCell("Y"),MergeCell("",spanTwo+vContinue)},"X,Y|,;1,0,1x2"),
            (new[]{MergeCell("A")+MergeCell("B")+MergeCell("C"),"<w:trPr><w:gridBefore w:val=\"1\"/></w:trPr>"+MergeCell("E")+MergeCell("F")},"A,B,C|,E,F;"),
            (new[]{MergeCell("A")+MergeCell("B",vRestart)+MergeCell("C"),"<w:trPr><w:gridBefore w:val=\"1\"/></w:trPr>"+MergeCell("",vContinue)+MergeCell("F")},"A,B,C|,,F;0,1,2x1"),
            (new[]{MergeCell("A")+MergeCell("B")+MergeCell("C"),"<w:trPr><w:gridAfter w:val=\"1\"/></w:trPr>"+MergeCell("E")+MergeCell("F")},"A,B,C|E,F;"),
            (new[]{MergeCell("A",spanTwo+"<w:hMerge w:val=\"restart\"/>")+MergeCell("",spanTwo+"<w:hMerge/>")},"A,,,;0,0,1x4"),
            (new[]{MergeCell("A","<w:hMerge w:val=\"restart\"/>")+MergeCell("","<w:hMerge w:val=\"continue\"/>")+MergeCell("C")},"A,,C;0,0,1x2"),
            (new[]{MergeCell("A")+MergeCell("B","<w:hMerge/>")},"A,B;"),
            (new[]{MergeCell("A","<w:hMerge w:val=\"restart\"/>")+MergeCell("B",vRestart+"<w:hMerge/>")},"A,B;"),
        })check(MergedTable("Merged table",mergeRows) is var mergeActual&&mergeActual==mergeExpected,"Word table merges differ from the Mac: "+mergeActual+" instead of "+mergeExpected);
        try{MergedTable("Wide gridBefore","<w:trPr><w:gridBefore w:val=\"1024\"/></w:trPr>"+MergeCell("A"));throw new Exception("A row past 1,024 grid columns was accepted");}catch(InvalidDataException){check(true,"w:gridBefore column limit");}
        var srt="1\r\n00:00:01,005 --> 00:00:02,230\r\n<b>Türkçe 🍐</b>\r\n\r\n2\r\n00:00:03,000 --> 00:00:04,000\r\nSecond line\r\n";
        var vtt=SubtitleCodec.Convert(srt,"vtt");check(vtt.StartsWith("WEBVTT",StringComparison.Ordinal)&&vtt.Contains("00:00:01.005"),"SRT/VTT timestamps changed");check(SubtitleCodec.Parse(SubtitleCodec.Convert(vtt,"srt")).Length==2,"Subtitle round trip lost cues");check(SubtitleCodec.Convert(vtt,"txt").Contains("Türkçe 🍐")&&!SubtitleCodec.Convert(vtt,"txt").Contains("<b>"),"Subtitle plain text failed");
        // As on the Mac, WebVTT numeric references follow HTML: windows-1252 for 0x80–0x9F, U+FFFD for NUL, surrogates and values past U+10FFFF.
        var htmlNumbersVtt="WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nIt&#146;s 9&#150;5 &#X41; a&#0;b&#xD800;c&#99999999999;d &#x1F350; &#129; &amp;#146; &#38;lt;\n";
        check(SubtitleCodec.Convert(htmlNumbersVtt,"srt")=="1\n00:00:01,000 --> 00:00:02,000\nIt’s 9–5 A a�b�c�d 🍐 \u0081 &#146; &lt;\n"&&SubtitleCodec.Convert(htmlNumbersVtt,"txt")=="It’s 9–5 A a�b�c�d 🍐 \u0081 &#146; &lt;\n"&&SubtitleCodec.Convert(htmlNumbersVtt,"vtt").Contains("It&#146;s 9&#150;5 &#X41;",StringComparison.Ordinal),"WebVTT numeric character references did not follow the HTML rules in SRT or TXT, or the WebVTT output changed");
        var encodedVtt="WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nTom &amp; Jerry\n\n";
        var decodedSrt=SubtitleCodec.Convert(encodedVtt,"srt");
        check(decodedSrt.Contains("Tom & Jerry",StringComparison.Ordinal)&&!decodedSrt.Contains("&amp;",StringComparison.Ordinal),"WebVTT character references leaked into SRT output");
        try{SubtitleCodec.Parse("00:00:02.000 --> 00:00:01.000\ninvalid");throw new Exception("Reversed subtitle accepted");}catch(InvalidDataException){check(true,"Subtitle validation");}
        // Like the Mac, a timing line starts the next cue even without a blank line before it, and the SRT cue number just above it is dropped.
        foreach(var packedCues in new[]{"1\n00:00:01,000 --> 00:00:02,000\nHello\n2\n00:00:03,000 --> 00:00:04,000\nWorld\n","1\r\n00:00:01,000 --> 00:00:02,000\r\nHello\r\n2\r\n00:00:03,000 --> 00:00:04,000\r\nWorld\r\n","WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nHello\n00:00:03.000 --> 00:00:04.000\nWorld\n"})
            check(SubtitleCodec.Parse(packedCues).Select(c=>c.Text).SequenceEqual(new[]{"Hello","World"})&&SubtitleCodec.Convert(packedCues,"txt")=="Hello\n\nWorld\n","Subtitle cues without a blank line between them were merged into one cue");
        check(SubtitleCodec.Parse("1\n00:00:01,000 --> 00:00:02,000\nA --> B\n1999\n\n2\n00:00:03,000 --> 00:00:04,000\nSecond\n").Select(c=>c.Text).SequenceEqual(new[]{"A --> B\n1999","Second"})&&SubtitleCodec.Parse("1\n00:00:01,000 --> 00:00:02,000\n2\n00:00:03,000 --> 00:00:04,000\nWorld\n").Select(c=>c.Text).SequenceEqual(new[]{"","World"}),"Subtitle text that only looks like a cue number or timing was dropped, or an empty packed cue kept the next cue's number");
        var input=Path.Combine(root,"Archive source.txt");File.WriteAllText(input,"Türkçe 🍐 retained");var zip=DocumentEngine.Zip([input],root);
        foreach(var format in new[]{"tar","gz","zip"})
        {
            Console.WriteLine("Verifying archive conversion: "+format);
            var converted=ArchiveEngine.Convert(zip,format,root)[0];var restored=ArchiveEngine.Extract(converted,root)[0];check(File.ReadAllText(Path.Combine(restored,"Archive source.txt"))==File.ReadAllText(input),"Archive round trip changed contents: "+format);
        }
        var tarGz=Path.Combine(root,"Bundle.tar.gz");File.Copy(ArchiveEngine.Convert(zip,"gz",root)[0],tarGz);
        check(Path.GetFileName(ArchiveEngine.Extract(tarGz,root)[0])=="Bundle Extracted"&&Path.GetFileName(ArchiveEngine.Convert(tarGz,"zip",root)[0])=="Bundle.zip","TAR.GZ outputs kept the inner .tar in their names");
        var nested=Directory.CreateDirectory(Path.Combine(root,"Zip folder","Nested"));File.WriteAllText(Path.Combine(nested.FullName,"Fruit.txt"),"Pear");var folderZip=DocumentEngine.Zip([nested.Parent!.FullName],root);var folderRestored=ArchiveEngine.Extract(folderZip,root)[0];check(File.ReadAllText(Path.Combine(folderRestored,"Zip folder","Nested","Fruit.txt"))=="Pear","Folder ZIP lost nested files");
        Directory.CreateDirectory(Path.Combine(nested.FullName,"Empty"));var withEmpty=DocumentEngine.Zip([nested.Parent!.FullName],root);foreach(var format in new[]{"tar","gz","zip"}){var restored=ArchiveEngine.Extract(ArchiveEngine.Convert(withEmpty,format,root)[0],root)[0];check(Directory.Exists(Path.Combine(restored,"Zip folder","Nested","Empty")),"Archive conversion lost an empty folder: "+format);}
        var rar=Path.Combine(root,"Stored.rar");StoredRar(rar);var rarOutput=ArchiveEngine.Extract(rar,root)[0];check(File.ReadAllText(Path.Combine(rarOutput,"Fruit.txt"))=="Nashi pear fixture","Stored RAR extraction failed");
        var dotted=Path.Combine(root,"Dotted.tar.gz");using(var file=File.Create(dotted))using(var gz=new GZipStream(file,CompressionLevel.Optimal))using(var writer=new TarWriter(gz,TarEntryFormat.Gnu)){writer.WriteEntry(new GnuTarEntry(TarEntryType.Directory,"./"));writer.WriteEntry(new GnuTarEntry(TarEntryType.RegularFile,"./Fruit.txt"){DataStream=new MemoryStream(Encoding.UTF8.GetBytes("Pear"))});}
        string? dottedText=null;try{dottedText=File.ReadAllText(Path.Combine(ArchiveEngine.Extract(dotted,root)[0],"Fruit.txt"));}catch(InvalidDataException){}check(dottedText=="Pear","Tarball with ./ entries was refused");
        var dottedAttack=Path.Combine(root,"Dotted attack.tar");using(var file=File.Create(dottedAttack))using(var writer=new TarWriter(file,TarEntryFormat.Gnu))writer.WriteEntry(new GnuTarEntry(TarEntryType.RegularFile,"./../outside.txt"){DataStream=new MemoryStream([1])});
        try{ArchiveEngine.Extract(dottedAttack,root);throw new Exception("Dotted traversal tarball accepted");}catch(InvalidDataException){check(true,"Dotted archive traversal protection");}
        var attack=Path.Combine(root,"Unsafe second.zip");using(var archive=ZipFile.Open(attack,ZipArchiveMode.Create)){using var stream=archive.CreateEntry("../outside.txt").Open();stream.WriteByte(1);}
        try{ArchiveEngine.Extract(attack,root);throw new Exception("Traversal archive accepted");}catch(InvalidDataException){check(true,"New archive traversal protection");}
        var svg=Path.Combine(root,"Static.svg");File.WriteAllText(svg,"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"96\" height=\"64\"><rect width=\"96\" height=\"64\" fill=\"#E5BC59\"/></svg>");
        var png=PhotoCodec.Png(svg);using(var image=new MagickImage(png))check(image.Width==96&&image.Height==64,"SVG native rasterization failed");
        var external=Path.Combine(root,"External.svg");File.WriteAllText(external,"<svg xmlns=\"http://www.w3.org/2000/svg\"><image href=\"file:///private/example.png\"/></svg>");try{PhotoCodec.Png(external);throw new Exception("External SVG resource accepted");}catch(InvalidDataException){check(true,"SVG resource isolation");}
        foreach(var format in new[]{"png","jpg","webp","heic","avif","tiff","bmp"})
        {if(format=="heic"&&Environment.GetEnvironmentVariable("NASHI_VERIFY_FFMPEG") is null)continue;var output=Path.Combine(root,"Image codec."+format);PhotoCodec.EncodePng(png,output,format,encoder:Environment.GetEnvironmentVariable("NASHI_VERIFY_FFMPEG"));using var decoded=PhotoCodec.Read(output);check(decoded.Width==96&&decoded.Height==64,"Image codec round trip failed: "+format);}
        // Raw BGRA pixels (WPF's Bgra32) must keep channel order and alpha without an intermediate PNG.
        // 16 px quadrants (blue | red over half-transparent green | grey) survive AVIF/WebP chroma subsampling.
        var bgra=new byte[32*32*4];for(var y=0;y<32;y++)for(var x=0;x<32;x++){var i=(y*32+x)*4;byte[] c=y<16?(x<16?[255,0,0,255]:[0,0,255,255]):(x<16?[0,255,0,128]:[90,90,90,255]);c.CopyTo(bgra,i);}
        foreach(var format in new[]{"png","webp","avif"})
        {var output=Path.Combine(root,"Raw pixels."+format);PhotoCodec.EncodePixels(bgra,32,32,output,format,100);using var decoded=new MagickImage(output);check(decoded.Width==32&&decoded.Height==32,"Raw pixel encode changed size: "+format);
            using var pixels=decoded.GetPixels();var blue=pixels.GetPixel(8,8).ToColor()!;var red=pixels.GetPixel(24,8).ToColor()!;check(blue.B>200&&blue.R<55&&red.R>200&&red.B<55,"Raw pixel encode swapped colour channels: "+format);
            if(format=="png")check(pixels.GetPixel(8,24).ToColor()!.A==128,"Raw pixel encode lost alpha");}
        if(Environment.GetEnvironmentVariable("NASHI_VERIFY_FFMPEG") is {} encoder){using var oddImage=new MagickImage(MagickColors.Gold,97,65);var odd=Path.Combine(root,"Odd.heic");PhotoCodec.EncodePng(oddImage.ToByteArray(MagickFormat.Png),odd,"heic",encoder:encoder);using var decodedOdd=PhotoCodec.Read(odd);check(decodedOdd.Width==97&&decodedOdd.Height==65,"HEIC odd dimensions changed");}
        // Like macOS, HEIC keeps transparency in an alpha plane (opaque | half | clear columns, odd size); opaque photos get none.
        if(Environment.GetEnvironmentVariable("NASHI_VERIFY_FFMPEG") is {} heicEncoder)foreach(var clear in new[]{true,false})
        {
            var pixels=new byte[97*65*4];for(var i=0;i<pixels.Length;i+=4){var x=i/4%97;pixels[i]=200;pixels[i+1]=60;pixels[i+2]=30;pixels[i+3]=!clear||x<32?(byte)255:x<64?(byte)128:(byte)0;}
            var output=Path.Combine(root,clear?"Transparent.heic":"Opaque.heic");PhotoCodec.EncodePixels(pixels,97,65,output,"heic",85,heicEncoder);using var decoded=PhotoCodec.Read(output);using var values=decoded.GetPixels();
            var alphas=string.Join(",",new[]{10,48,90,96}.Select(x=>values.GetPixel(x,64).ToColor()!.A));var colour=values.GetPixel(10,30).ToColor()!;
            check(decoded.Width==97&&decoded.Height==65&&decoded.HasAlpha==clear&&alphas==(clear?"255,128,0,0":"255,255,255,255")&&Math.Abs(colour.B-200)<8&&Math.Abs(colour.R-30)<8,$"HEIC transparency was not kept: alpha {alphas}, colour {colour}, has alpha {decoded.HasAlpha}");
        }
        var taggedColor=Path.Combine(root,"Wide gamut.webp");using(var wide=new MagickImage(new MagickColor(100,128,30),16,16)){wide.SetProfile(ColorProfiles.AdobeRGB1998);wide.Write(taggedColor,MagickFormat.WebP);}using(var expectedColor=new MagickImage(taggedColor))using(var actualColor=PhotoCodec.Read(taggedColor)){expectedColor.TransformColorSpace(ColorProfiles.SRGB);check(expectedColor.Compare(actualColor,ErrorMetric.Absolute)==0,"Wide-gamut image profile was ignored");}
        byte[] Flat(bool gray,bool alpha){using var flat=new MagickImage(new MagickColor(128,128,128,(byte)(alpha?200:255)),4,4){Format=MagickFormat.Png};if(gray)flat.ColorType=alpha?ColorType.GrayscaleAlpha:ColorType.Grayscale;else flat.Settings.SetDefine(MagickFormat.Png,"color-type",alpha?"6":"2");return flat.ToByteArray();}
        string Sample(byte[] bytes){using var decoded=new MagickImage(bytes);decoded.ColorSpace=ColorSpace.sRGB;return string.Join(",",decoded.GetPixels().GetValue(1,1)!.Take(decoded.HasAlpha?4:3));}
        foreach(var alpha in new[]{false,true})foreach(var adjustment in new[]{new PhotoAdjustments{Saturation=1.5},new(){Temperature=.5},new(){Tint=.4,Vibrance=.3}})
        {
            var adjustedGray=PhotoCodec.Adjust(Flat(true,alpha),adjustment);
            check(Sample(adjustedGray)==Sample(PhotoCodec.Adjust(Flat(false,alpha),adjustment)),"A grayscale photo adjusted differently from the same RGB photo");
            if(alpha){using var decodedGray=new MagickImage(adjustedGray);check(decodedGray.GetPixels().GetPixel(1,1).ToColor()!.A==200,"Photo adjustment changed grayscale alpha");}
        }
        var neutral=PhotoCodec.Adjust(png,new());using(var decoded=new MagickImage(neutral))check(decoded.Width==96&&decoded.Height==64,"Neutral photo adjustment changed dimensions");
        var changed=PhotoCodec.Adjust(png,new(){Brightness=.2,Saturation=.3,Temperature=.2,Shadows=.1});using(var a=new MagickImage(png))using(var b=new MagickImage(changed))check(a.Compare(b,ErrorMetric.Absolute)>0,"Photo adjustments did not change pixels");
        foreach(var effect in new[]{new PhotoAdjustments{Blur=2},new(){Sharpen=1},new(){NoiseReduction=.5},new(){Grain=.3},new(){Vignette=.4},new(){Gamma=1.2},new(){Exposure=.5},new(){Hue=50},new(){Dehaze=.2}})check(PhotoCodec.Adjust(png,effect).Length>0,"Photo effect failed");
        // Like macOS, tone edits change colour only: opaque, translucent and transparent pixels keep their alpha.
        byte[] translucent;using(var strip=new MagickImage()){strip.ReadPixels(new byte[]{128,128,128,255,128,128,128,128,128,128,128,0},new PixelReadSettings(3,1,StorageType.Char,PixelMapping.BGRA));translucent=strip.ToByteArray(MagickFormat.Png);}
        foreach(var (name,tone) in new[]{("Brightness",new PhotoAdjustments{Brightness=.3}),("Contrast",new(){Contrast=1.5}),("Low contrast",new(){Contrast=.5}),("Dehaze",new(){Dehaze=.5})})
        {using var toned=new MagickImage(PhotoCodec.Adjust(translucent,tone));using var pixels=toned.GetPixels();var alphas=string.Join(",",Enumerable.Range(0,3).Select(x=>pixels.GetPixel(x,0).ToColor()!.A));check(alphas=="255,128,0",$"{name} changed photo transparency: alpha {alphas}");}
        try{PhotoCodec.Adjust(png,new(){Gamma=double.NaN});throw new Exception("Invalid adjustment accepted");}catch(ArgumentException){check(true,"Photo finite validation");}
        foreach(var hue in new[]{180d,-150d}){using var a=new MagickImage(png);using var b=new MagickImage(PhotoCodec.Adjust(png,new(){Hue=hue}));check(a.Compare(b,ErrorMetric.Absolute)>0,"Hue beyond 100 degrees did not change pixels");}
        try{PhotoCodec.Adjust(png,new(){Hue=181});throw new Exception("Out of range hue accepted");}catch(ArgumentException){check(true,"Photo hue range validation");}
        try{PhotoCodec.Adjust(png,new(){Blur=101});throw new Exception("Out of range blur accepted");}catch(ArgumentException){check(true,"Photo adjustment range validation");}
        // Redaction blur (24) must erase 4-pixel stripes the way macOS CIGaussianBlur does, not leave them readable.
        // Only the centre is measured: edge pixels are clamped, so border stripes cannot fully average out.
        using(var stripes=new MagickImage(MagickColors.White,200,80)){for(var x=0;x<200;x+=8)new ImageMagick.Drawing.Drawables().FillColor(MagickColors.Black).Rectangle(x,0,x+3,79).Draw(stripes);
            using var blurred=new MagickImage(PhotoCodec.Adjust(stripes.ToByteArray(MagickFormat.Png),new(){Blur=24}));using var centre=blurred.CloneArea(new MagickGeometry(50,20,100,40));var spread=centre.Statistics().GetChannel(PixelChannel.Red)!.StandardDeviation/Quantum.Max;check(spread<.02,$"Redaction blur left stripe detail visible (spread {spread:0.000})");}
        // A killed tool can still hold a staged file when the user cancels.
        Exception? outcome=null;var hidden=false;FileStream? held=null;string? staging=null;var cleanup=new System.Diagnostics.Stopwatch();
        try{using var tx=new ExportTransaction(Path.Combine(root,"Held export"));staging=tx.StagingDirectory;hidden=File.GetAttributes(staging).HasFlag(FileAttributes.Hidden);held=File.Create(Path.Combine(staging,"partial.mp4"));cleanup.Start();throw new OperationCanceledException();}
        catch(Exception error){cleanup.Stop();outcome=error;}finally{held?.Dispose();}
        check(hidden&&outcome is OperationCanceledException&&cleanup.ElapsedMilliseconds<300,"Staging cleanup replaced a cancellation with "+outcome?.GetType().Name+", blocked for "+cleanup.ElapsedMilliseconds+" ms, or staging was visible");
        var released=System.Diagnostics.Stopwatch.StartNew();while(Directory.Exists(staging)&&released.Elapsed.TotalSeconds<5)Thread.Sleep(50);
        check(!Directory.Exists(staging),"A staging folder was left behind after its file was released");
    }
    private static void StoredRar(string output)
    {
        static uint Crc(byte[] data){var crc=uint.MaxValue;foreach(var b in data){crc^=b;for(var i=0;i<8;i++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}return ~crc;}
        using var file=new BinaryWriter(File.Create(output));file.Write(new byte[]{0x52,0x61,0x72,0x21,0x1a,0x07,0x00});
        void Header(byte type,ushort flags,byte[] payload){using var memory=new MemoryStream();using(var header=new BinaryWriter(memory,Encoding.UTF8,true)){header.Write(type);header.Write(flags);header.Write((ushort)(7+payload.Length));header.Write(payload);}var bytes=memory.ToArray();file.Write((ushort)Crc(bytes));file.Write(bytes);}
        Header(0x73,0,new byte[6]);var data=Encoding.UTF8.GetBytes("Nashi pear fixture");var name=Encoding.ASCII.GetBytes("Fruit.txt");using var content=new MemoryStream();using(var header=new BinaryWriter(content,Encoding.UTF8,true)){header.Write((uint)data.Length);header.Write((uint)data.Length);header.Write((byte)2);header.Write(Crc(data));header.Write(0u);header.Write((byte)20);header.Write((byte)0x30);header.Write((ushort)name.Length);header.Write(0x20u);header.Write(name);}Header(0x74,0x8000,content.ToArray());file.Write(data);Header(0x7b,0,[]);
    }

}
