using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Nashi.Core;
internal static class UdfChecks
{
    private const string Png="iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
    internal static void Run(Action<bool,string> check,string root)
    {
        check(FeatureCatalog.Formats(["Evrak.udf"]).SequenceEqual(new[]{"docx","pages","pdf","txt","md","html","epub","png","jpg"})&&FeatureCatalog.Tools(["Evrak.udf"]).Length==0,"UDF lost its document routes");
        check(new[]{"a.docx","a.pdf","a.rtf","a.txt","a.md","a.pages","a.epub"}.All(f=>FeatureCatalog.Formats([f]).Contains("udf"))&&!FeatureCatalog.Formats(["a.html"]).Contains("udf"),"Documents lost their UDF route");
        UyapToDocx(check,root);DocxToUyap(check,root);
        var missing=Path.Combine(root,"Missing content.udf");using(var zip=ZipFile.Open(missing,ZipArchiveMode.Create))Entry(zip,"documentproperties.xml","<properties/>");
        check(Throws(()=>OfficeDocuments.UdfToDocx(missing,Path.Combine(root,"Missing.docx"))),"A UDF without content.xml was converted");
        var other=Path.Combine(root,"Other root.udf");using(var zip=ZipFile.Open(other,ZipArchiveMode.Create))Entry(zip,"content.xml","<?xml version=\"1.0\"?><document/>");
        check(Throws(()=>OfficeDocuments.UdfToDocx(other,Path.Combine(root,"Other.docx"))),"An XML package that is not a UYAP template was converted");
    }
    private static bool Throws(Action action){try{action();return false;}catch(InvalidDataException){return true;}}
    private static void Entry(ZipArchive zip,string name,string text){using var writer=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false));writer.Write(text);}
    private static string Part(string package,string name){using var zip=ZipFile.OpenRead(package);using var reader=new StreamReader(zip.GetEntry(name)?.Open()??throw new Exception("Missing "+name+" in "+Path.GetFileName(package)));return reader.ReadToEnd();}
    // Every paragraph's leaves follow one another and end with its "\n", and paragraphs follow one another through the whole text, as in UYAP's files.
    private static string Validate(Action<bool,string> check,string udf)
    {
        var template=OfficeDocuments.ReadUdf(udf);var text=template.Element("content")!.Value;var next=0;
        foreach(var paragraph in template.Element("elements")!.Descendants("paragraph"))
        {
            foreach(var leaf in paragraph.Elements()){var start=int.Parse(leaf.Attribute("startOffset")!.Value);var length=int.Parse(leaf.Attribute("length")!.Value);check(start==next&&length>0,"UDF leaves are not contiguous at offset "+next);next=start+length;}
            check(next>0&&text[next-1]=='\n',"A UDF paragraph does not end with its newline");
        }
        check(next==text.Length,"UDF text and paragraphs differ in length");return text;
    }
    private static void UyapToDocx(Action<bool,string> check,string root)
    {
        // Built like a document from UYAP's editor: header text first, field and space leaves, edf_* styles, Swing tab stops,
        // a borderless table, a page break, an image and a footer page number.
        var text=new StringBuilder();
        string Leaf(string tag,string value,string attributes=""){var start=text.Length;text.Append(value);return "<"+tag+attributes+" startOffset=\""+start+"\" length=\""+value.Length+"\" />";}
        static string Paragraph(string attributes,params string[] leaves)=>"<paragraph"+attributes+">"+string.Concat(leaves)+"</paragraph>";
        var header="<header startPage=\"2\" foreground=\"-8355712\">"+Paragraph(" TabSet=\"325.0:0:0\" resolver=\"default\"",Leaf("content","T.C. "," family=\"Times New Roman\" bold=\"true\" size=\"11\""),Leaf("field","ANKARA"," fieldName=\"il_Ilce\" resolver=\"hvl-default\""),Leaf("tab","\t"),Leaf("content","Dosya No: 2026/1\n"," resolver=\"default\""))+"</header>";
        var item=" Numbered=\"true\" NumberType=\"NUMBER_TYPE_NUMBER_TRE\" ListId=\"7\" ListLevel=\"1\" LeftIndent=\"18.0\"";
        var body=Paragraph(" Alignment=\"1\" resolver=\"edf_1\"",Leaf("content","TENSİP ZAPTI"," bold=\"true\" resolver=\"edf_1\""),Leaf("content","\n"))
            +Paragraph(" TabSet=\"69.0:2:0,120.0:0:1\"",Leaf("tab","\t"),Leaf("content","DAVACI"),Leaf("tab","\t"),Leaf("content",": Çağrı Şükrü İĞDE\n"))
            +Paragraph(" Alignment=\"3\" FirstLineIndent=\"36.0\" LineSpacing=\"0.5\" SpaceAbove=\"6.0\"",Leaf("content","Kalın"," bold=\"true\""),Leaf("space"," "),Leaf("content","italik"," italic=\"true\" foreground=\"-65536\""),Leaf("content"," altı çizili ]]> son\n"," underline=\"true\" background=\"-256\""))
            +Paragraph(item,Leaf("content","Birinci madde\n"))+Paragraph(item,Leaf("content","İkinci madde\n"))
            +"<table tableName=\"Sabit\" columnCount=\"3\" columnSpans=\"100,100,200\" border=\"borderNone\"><row rowName=\"row1\" rowType=\"dataRow\"><cell colspan=\"2\" fillColor=\"-3355444\">"+Paragraph("",Leaf("content","Birleşik\n"))+"</cell><cell rowspan=\"2\" align=\"vcenter\">"+Paragraph("",Leaf("content","Uzun\n"))+"</cell></row>"
            +"<row rowName=\"row2\" rowType=\"dataRow\"><cell>"+Paragraph("",Leaf("content","A\n"))+"</cell><cell>"+Paragraph("",Leaf("content","B\n"))+"</cell></row></table>"
            +"<page-break>"+Paragraph("",Leaf("content","\u200B\n"))+"</page-break>"+Paragraph("",Leaf("image","\uFFFC"," imageData=\""+Png+"\" width=\"40.0\" height=\"20.0\""),Leaf("content","Resim sonrası\n"))
            // The party block of a court document: the label, a tab to the colon and the address wrapping under the text.
            +Paragraph(" Alignment=\"3\" TabSet=\"183.0:0:0\" LeftIndent=\"0.0\" Hanging=\"190.0\"",Leaf("content","MÜŞTEKİ\t:"," bold=\"true\""),Leaf("content"," Uzun bir adres satırı\n"))+Paragraph("",Leaf("content","\u200B\n"));
        var footer="<footer pageNumber-spec=\"BSP32_2120\" pageNumber-foreStr=\"Sayfa\" pageNumber-seperator=\"/\" pageNumber-fontFace=\"Arial\" pageNumber-fontSize=\"11\">"+Paragraph("",Leaf("content","\n"))+"</footer>";
        var content="<?xml version=\"1.0\" encoding=\"UTF-8\" ?> \n\n<template format_id=\"1.8\" >\n<content><![CDATA["+text.ToString().Replace("]]>","]]]]><![CDATA[>")+"]]></content><properties><pageFormat mediaSizeName=\"1\" leftMargin=\"70.866\" rightMargin=\"70.866\" topMargin=\"56.7\" bottomMargin=\"56.7\" paperOrientation=\"1\" headerFOffset=\"15.0\" footerFOffset=\"28.3\" /></properties>\n<elements >\n"
            +header+body+footer+"</elements>\n<styles><style name=\"default\" family=\"Dialog\" size=\"12\" foreground=\"-16777216\" /><style name=\"hvl-default\" family=\"Times New Roman\" size=\"12\" /><style name=\"edf_1\" resolver=\"hvl-default\" size=\"14\" /></styles>\n<webID id=\"Ab12cD3  -  eF45gH6  -  iJ78kL9  -  MnOPQR=\" >\n\n</webID>\n</template>";
        var udf=Path.Combine(root,"Tensip zaptı.udf");
        using(var zip=ZipFile.Open(udf,ZipArchiveMode.Create)){Entry(zip,"documentproperties.xml","<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE properties SYSTEM \"http://java.sun.com/dtd/properties.dtd\">\n<properties><entry key=\"uyapsicil\">1</entry></properties>");Entry(zip,"content.xml",content);using var sign=zip.CreateEntry("sign.sgn").Open();sign.Write(new byte[]{1,2,3});}
        check(Validate(check,udf)==text.ToString(),"The UYAP-shaped fixture or its CDATA split is malformed");
        var docx=Path.Combine(root,"Tensip zaptı.docx");OfficeDocuments.UdfToDocx(udf,docx);var document=Part(docx,"word/document.xml");XDocument.Parse(document);
        check(document.Contains("<w:titlePg/>")&&Part(docx,"word/header1.xml").Contains("Dosya No")&&!Part(docx,"word/header2.xml").Contains("Dosya No")&&Part(docx,"word/header1.xml").Contains("w:ascii=\"Arial\""),"UDF header from page 2, or Java's Dialog font, was lost");
        check(document.Contains("<w:tab w:val=\"center\" w:leader=\"none\" w:pos=\"1380\"/><w:tab w:val=\"left\" w:leader=\"dot\" w:pos=\"2400\"/>"),"UDF tab stops were lost");
        check(document.Contains("<w:jc w:val=\"center\"/>")&&document.Contains("<w:jc w:val=\"both\"/>")&&document.Contains("w:firstLine=\"720\"")&&document.Contains("w:before=\"120\" w:after=\"0\" w:line=\"360\""),"UDF paragraph layout was lost");
        check(document.Contains("<w:sz w:val=\"28\"/>")&&document.Contains("<w:color w:val=\"FF0000\"/>")&&document.Contains("w:fill=\"FFFF00\"")&&document.Contains("<w:u w:val=\"single\"/>"),"UDF style resolvers or run formatting were lost");
        check(document.Contains("<w:gridSpan w:val=\"2\"/>")&&document.Contains("<w:vMerge w:val=\"restart\"/>")&&document.Contains("<w:vMerge/>")&&document.Contains("w:fill=\"CCCCCC\"")&&document.Contains("<w:vAlign w:val=\"center\"/>")&&document.Contains("<w:top w:val=\"nil\"/>"),"UDF table spans, fills or borders were lost");
        check(document.Contains("<w:pageBreakBefore/>")&&!document.Contains("\u200B")&&!document.Contains("\uFFFC")&&Part(docx,"word/media/image1.png").Length>0&&Part(docx,"word/numbering.xml").Contains("%1-"),"UDF page break, placeholders, image or list numbering were lost");
        // UYAP's Hanging moves the wrapped lines, not the first; its stops stay measured from the first line, and UYAP's default stops are 72 points apart.
        check(document.Contains("<w:tab w:val=\"left\" w:leader=\"none\" w:pos=\"3660\"/>")&&document.Contains("<w:ind w:left=\"3800\" w:right=\"0\" w:hanging=\"3800\"/>")&&Part(docx,"word/settings.xml").Contains("<w:defaultTabStop w:val=\"1440\"/>"),"UDF hanging indent or default tab stops were lost");
        var footerPart=Part(docx,"word/footer3.xml");
        // UYAP prints a served document's access code under every page, the first one too.
        check(footerPart.Contains("UYAP Bilişim Sistemindeki bu dokümana http://vatandas.uyap.gov.tr adresinden ")&&footerPart.Contains(">Ab12cD3 - eF45gH6 - iJ78kL9 - MnOPQR=<")&&Part(docx,"word/footer4.xml").Contains("Ab12cD3 - eF45gH6"),"The UYAP access code was not printed under the pages");check(footerPart.Contains(" PAGE ")&&footerPart.Contains(" NUMPAGES ")&&footerPart.Contains("Sayfa "),"UDF page number was lost");
        using(var model=JsonDocument.Parse(JsonSerializer.Serialize(OfficeDocuments.ReadDocx(docx))))
        {
            var words=string.Concat(model.RootElement.GetProperty("blocks").EnumerateArray().Where(b=>b.GetProperty("type").GetString()=="paragraph").Select(b=>string.Concat(b.GetProperty("runs").EnumerateArray().Select(r=>r.TryGetProperty("text",out var t)?t.GetString():"/"))+"|"));
            check(words.StartsWith("TENSİP ZAPTI|\tDAVACI\t: Çağrı Şükrü İĞDE|Kalın italik altı çizili ]]> son|Birinci madde|İkinci madde|",StringComparison.Ordinal)&&words.Contains("/Resim sonrası|",StringComparison.Ordinal),"UDF text did not survive DOCX: "+words);
        }
        // Back to UDF: Word list numbers become text, and the layout is UYAP's again.
        var back=Path.Combine(root,"Tensip zaptı again.udf");OfficeDocuments.DocxToUdf(docx,back);var again=Validate(check,back);
        check(again.StartsWith("T.C. ANKARA\tDosya No: 2026/1\nTENSİP ZAPTI\n\tDAVACI\t: Çağrı Şükrü İĞDE\nKalın italik altı çizili ]]> son\n1-\tBirinci madde\n2-\tİkinci madde\nBirleşik\nUzun\nA\nB\n\u200B\n\u200B\n\uFFFCResim sonrası\nMÜŞTEKİ\t: Uzun bir adres satırı\n",StringComparison.Ordinal),"UDF round trip changed the text: "+again);
        var party=OfficeDocuments.ReadUdf(back).Element("elements")!.Elements("paragraph").First(p=>p.Attribute("Hanging") is not null);
        check(party.Attribute("LeftIndent")?.Value=="0.0"&&party.Attribute("Hanging")?.Value=="190.0"&&party.Attribute("TabSet")?.Value?.StartsWith("183.0:0:0,190.0:0:0,216.0:0:0",StringComparison.Ordinal)==true,"A UYAP party block lost its hanging indent or colon stop in a round trip: "+party);
        check(OfficeDocuments.ReadUdf(back).Element("elements")!.Element("header")?.Attribute("startPage")?.Value=="2","A header starting on page 2 came back on the first page too");
        check(!again.Contains("vatandas.uyap.gov.tr",StringComparison.Ordinal)&&!again.Contains("Ab12cD3",StringComparison.Ordinal),"A copy written as UDF again kept the original's access code");
    }
    private static void DocxToUyap(Action<bool,string> check,string root)
    {
        const string w="xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"";
        static string Item(int level,string text,string indent="")=>"<w:p><w:pPr><w:numPr><w:ilvl w:val=\""+level+"\"/><w:numId w:val=\"1\"/></w:numPr>"+indent+"</w:pPr><w:r><w:t>"+text+"</w:t></w:r></w:p>";
        var docx=Path.Combine(root,"Dilekçe.docx");
        using(var zip=ZipFile.Open(docx,ZipArchiveMode.Create))
        {
            Entry(zip,"word/document.xml","<w:document "+w+"><w:body><w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:rPr><w:b/></w:rPr><w:t>DİLEKÇE</w:t></w:r></w:p>"
                +"<w:p><w:pPr><w:tabs><w:tab w:val=\"left\" w:pos=\"1440\"/></w:tabs></w:pPr><w:r><w:t>DAVACI</w:t></w:r><w:r><w:tab/></w:r><w:r><w:t xml:space=\"preserve\">: Ayşe </w:t></w:r><w:r><w:t>Öztürk</w:t></w:r><w:r><w:rPr><w:vanish/></w:rPr><w:t>gizli</w:t></w:r></w:p>"
                +Item(0,"Birinci","<w:ind w:left=\"720\" w:hanging=\"360\"/>")+Item(1,"Alt")+"<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr><w:r><w:t>İkinci</w:t></w:r><w:r><w:br/><w:t>satır</w:t></w:r></w:p>"
                +"<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> DATE \\@ \"dd.MM.yyyy\" </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>03.11.2026</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>"
                +"<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"3000\"/><w:gridCol w:w=\"4000\"/></w:tblGrid>"
                +"<w:tr><w:tc><w:tcPr><w:gridSpan w:val=\"2\"/><w:shd w:val=\"clear\" w:fill=\"DDDDDD\"/></w:tcPr><w:p><w:r><w:t>Üst</w:t></w:r></w:p></w:tc><w:tc><w:tcPr><w:vMerge w:val=\"restart\"/></w:tcPr><w:p><w:r><w:t>Sağ</w:t></w:r></w:p></w:tc></w:tr>"
                +"<w:tr><w:tc><w:p><w:r><w:t>X</w:t></w:r></w:p></w:tc><w:tc><w:p/></w:tc><w:tc><w:tcPr><w:vMerge/></w:tcPr><w:p><w:r><w:t>birleşik hücrenin devamı</w:t></w:r></w:p></w:tc></w:tr></w:tbl>"
                +"<w:p><w:r><w:br w:type=\"page\"/></w:r><w:r><w:drawing><wp:inline><wp:extent cx=\"952500\" cy=\"476250\"/><a:graphic><a:graphicData><pic:pic><pic:blipFill><a:blip r:embed=\"rImage\"/></pic:blipFill></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r><w:r><w:t>Son</w:t></w:r></w:p>"
                +"<w:sectPr><w:footerReference w:type=\"default\" r:id=\"rFooter\"/><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1134\" w:right=\"1134\" w:bottom=\"1134\" w:left=\"1701\" w:header=\"709\" w:footer=\"709\"/></w:sectPr></w:body></w:document>");
            Entry(zip,"word/styles.xml","<w:styles "+w+"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Liberation Serif\" w:hAnsi=\"Liberation Serif\"/><w:sz w:val=\"24\"/></w:rPr></w:rPrDefault></w:docDefaults><w:style w:type=\"table\" w:styleId=\"TableGrid\"><w:tblPr><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\"/><w:insideV w:val=\"single\" w:sz=\"4\"/></w:tblBorders></w:tblPr></w:style></w:styles>");
            Entry(zip,"word/numbering.xml","<w:numbering "+w+"><w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl><w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"lowerLetter\"/><w:lvlText w:val=\"%2)\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num></w:numbering>");
            Entry(zip,"word/footer1.xml","<w:ftr "+w+"><w:p><w:r><w:t xml:space=\"preserve\">Sayfa </w:t></w:r><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> PAGE </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>1</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r><w:r><w:t xml:space=\"preserve\"> / </w:t></w:r><w:fldSimple w:instr=\" NUMPAGES \"><w:r><w:t>1</w:t></w:r></w:fldSimple></w:p></w:ftr>");
            Entry(zip,"word/_rels/document.xml.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rFooter\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" Target=\"footer1.xml\"/><Relationship Id=\"rImage\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image1.png\"/></Relationships>");
            using var image=zip.CreateEntry("word/media/image1.png").Open();image.Write(Convert.FromBase64String(Png));
        }
        var udf=Path.Combine(root,"Dilekçe.udf");OfficeDocuments.DocxToUdf(docx,udf);var text=Validate(check,udf);var xml=Part(udf,"content.xml");
        check(text=="DİLEKÇE\nDAVACI\t: Ayşe Öztürk\n1.\tBirinci\na)\tAlt\n2.\tİkinci\nsatır\n03.11.2026\nÜst\nSağ\nX\n\u200B\n\u200B\n\u200B\n\uFFFCSon\n\u200B\n","DOCX text, list numbers, fields, merges or breaks changed in UDF: "+text);
        var template=OfficeDocuments.ReadUdf(udf);var paragraphs=template.Element("elements")!.Elements("paragraph").ToList();
        check(template.Attribute("format_id")?.Value=="1.8"&&xml.Contains("<elements resolver=\"hvl-default\">")&&xml.Contains("<style name=\"hvl-default\" family=\"Times New Roman\" size=\"12\" description=\"Gövde\" />"),"UDF template differs from UYAP's");
        check(paragraphs[0].Attribute("Alignment")?.Value=="1"&&paragraphs[0].Element("content")?.Attribute("bold")?.Value=="true"&&paragraphs[0].Element("content")?.Attribute("family")?.Value=="Times New Roman"&&paragraphs[0].Element("content")?.Attribute("size")?.Value=="12","DOCX alignment, bold or Liberation font mapping was lost");
        // Word's default stops (36 points without settings.xml) follow the custom ones up to the text width.
        check(paragraphs[1].Attribute("TabSet")?.Value?.StartsWith("72.0:0:0,108.0:0:0,144.0:0:0",StringComparison.Ordinal)==true&&paragraphs[1].Attribute("TabSet")!.Value.EndsWith(",432.0:0:0",StringComparison.Ordinal)&&paragraphs[0].Attribute("TabSet") is null,"DOCX tab stops or Word's default stops were lost");
        // Word's hanging indent becomes UYAP's Hanging, and the label's tab ends where the wrapped lines begin.
        check(paragraphs[2].Attribute("LeftIndent")?.Value=="18.0"&&paragraphs[2].Attribute("Hanging")?.Value=="18.0"&&paragraphs[2].Attribute("FirstLineIndent") is null&&paragraphs[2].Attribute("TabSet")?.Value?.StartsWith("18.0:0:0,54.0:0:0",StringComparison.Ordinal)==true,"The hanging list indent or its tab lost its place");
        var table=template.Element("elements")!.Element("table");
        check(table?.Attribute("columnCount")?.Value=="3"&&table.Attribute("columnSpans")?.Value=="100,150,200"&&table.Attribute("border")?.Value=="borderCell"&&table.Elements("row").All(r=>r.Elements("cell").Sum(c=>int.Parse(c.Attribute("colspan")?.Value??"1"))==3)&&xml.Contains("colspan=\"2\" fillColor=\"-2236963\""),"DOCX table columns, style borders or fills were lost");
        var footer=template.Element("elements")!.Element("footer");
        check(template.Element("elements")!.Elements("page-break").Count()==1&&xml.Contains("width=\"100.0\" height=\"50.0\"")&&footer?.Attribute("pageNumber-spec") is not null&&footer.Attribute("pageNumber-foreStr")?.Value=="Sayfa "&&footer.Attribute("pageNumber-seperator")?.Value=="/","DOCX page break, image size or footer page number was lost");
        // Word keeps the mark of a paragraph that only holds a page break on the page before; UYAP must not start the next page with an empty line.
        var breakOnly=Path.Combine(root,"Page break only.docx");using(var zip=ZipFile.Open(breakOnly,ZipArchiveMode.Create))Entry(zip,"word/document.xml","<w:document "+w+"><w:body><w:p><w:r><w:t>Bir</w:t></w:r></w:p><w:p><w:r><w:br w:type=\"page\"/></w:r></w:p><w:p><w:r><w:t>İki</w:t></w:r></w:p></w:body></w:document>");
        var breakUdf=Path.Combine(root,"Page break only.udf");OfficeDocuments.DocxToUdf(breakOnly,breakUdf);check(Validate(check,breakUdf)=="Bir\n\u200B\nİki\n"&&OfficeDocuments.ReadUdf(breakUdf).Element("elements")!.Elements("page-break").Count()==1,"A paragraph holding only a page break started the next page with an empty line");
        check(xml.Contains("leftMargin=\"85.05\"")&&xml.Contains("paperOrientation=\"1\"")&&xml.Contains("headerFOffset=\"35.45\""),"DOCX page setup was lost");
        // DOCX -> UDF refuses a content.xml larger than UDF -> DOCX reads (256,000,000 bytes) before it creates the file. The limit is
        // lowered to this document's own size, counted in UTF-8 bytes (its Turkish letters take two), to reach both sides of it.
        static long ContentBytes(string package){using var zip=ZipFile.OpenRead(package);return zip.GetEntry("content.xml")!.Length;}
        var written=ContentBytes(udf);var fits=Path.Combine(root,"Dilekçe at the limit.udf");var refused=Path.Combine(root,"Dilekçe over the limit.udf");OfficeDocuments.DocxToUdf(docx,fits,written);
        check(written>xml.Length&&ContentBytes(fits)==written&&Throws(()=>OfficeDocuments.DocxToUdf(docx,refused,written-1))&&!File.Exists(refused),"DOCX -> UDF wrote a content.xml larger than UDF -> DOCX reads, or refused one that fits");
        // A field's instruction ends at its first separate. Instruction text after it must not turn a footer field into a page number,
        // nor be read again at every later separate (40,000 of them, each after more instruction text, took about 16 s before).
        // A field inside another field's instruction, complex or simple, is neither a page number nor text, a PAGE field without a
        // cached result still numbers the pages, and a field mark in a hidden run still closes its field.
        string Footer(string name,string field)
        {
            var path=Path.Combine(root,name+".docx");
            using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
            Entry(zip,"word/document.xml","<w:document "+w+"><w:body><w:p><w:r><w:t>Metin</w:t></w:r></w:p><w:sectPr><w:footerReference w:type=\"default\" r:id=\"rFooter\"/></w:sectPr></w:body></w:document>");
            Entry(zip,"word/_rels/document.xml.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rFooter\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" Target=\"footer1.xml\"/></Relationships>");
            Entry(zip,"word/footer1.xml","<w:ftr "+w+"><w:p>"+field+"</w:p></w:ftr>");return path;
        }
        string Udf(string docx){var output=Path.ChangeExtension(docx,".udf");OfficeDocuments.DocxToUdf(docx,output);return output;}
        static bool Numbered(string udf)=>OfficeDocuments.ReadUdf(udf).Element("elements")!.Element("footer")?.Attribute("pageNumber-spec") is not null;
        static string Instruction(string code)=>"<w:r><w:instrText xml:space=\"preserve\">"+code+"</w:instrText></w:r>";
        static string Plain(string words)=>"<w:r><w:t xml:space=\"preserve\">"+words+"</w:t></w:r>";
        const string begin="<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>",separate="<w:fldChar w:fldCharType=\"separate\"/>",end="<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>",page="<w:instrText xml:space=\"preserve\"> PAGE </w:instrText>";
        var late=Udf(Footer("Late instruction",begin+"<w:r>"+separate+"</w:r><w:r>"+page+separate+"</w:r>"+Plain("7")+end));
        check(Validate(check,late)=="Metin\n7\n"&&!Numbered(late),"Instruction text after a field's first separate made a footer field a page number");
        var nested=Udf(Footer("Nested field",begin+Instruction("IF ")+begin+Instruction(" PAGE ")+"<w:r>"+separate+"</w:r>"+Plain("1")+end+Instruction(" = 1 \"Ek\" \"\" ")+"<w:r>"+separate+"</w:r>"+Plain("Ek")+end));
        check(Validate(check,nested)=="Metin\nEk\n"&&!Numbered(nested),"A field inside another field's instruction became a page number or text");
        var simple=Udf(Footer("Nested simple field",begin+Instruction("IF ")+"<w:fldSimple w:instr=\" PAGE \">"+Plain("1")+"</w:fldSimple>"+Instruction(" = 1 \"Ek\" \"\" ")+"<w:r>"+separate+"</w:r>"+Plain("Ek")+end));
        check(Validate(check,simple)=="Metin\nEk\n"&&!Numbered(simple),"A simple field inside another field's instruction became a page number or text");
        var bare=Udf(Footer("Field without result",Plain("Sayfa ")+begin+"<w:r>"+page+"</w:r>"+end));
        check(Numbered(bare),"A footer PAGE field without a cached result lost its page number");
        var hidden=Udf(Footer("Hidden field end",begin+Instruction(" XE \"Dava\" ")+"<w:r><w:rPr><w:vanish/></w:rPr><w:fldChar w:fldCharType=\"end\"/></w:r></w:p><w:p>"+Plain("Sonra")));
        check(Validate(check,hidden).EndsWith("Sonra\n",StringComparison.Ordinal),"A field closed in a hidden run swallowed the text after it");
        var repeated=new StringBuilder(begin+"<w:r>"+page+separate+"</w:r>");var chunk="<w:r><w:instrText xml:space=\"preserve\"> "+new string('x',19)+"</w:instrText>"+separate+"<w:t>1</w:t></w:r>";
        for(var n=0;n<40_000;n++)repeated.Append(chunk);
        var repeatedDocx=Footer("Repeated separates",repeated.Append(end).ToString());var timer=System.Diagnostics.Stopwatch.StartNew();var many=Udf(repeatedDocx);var seconds=timer.Elapsed.TotalSeconds;
        check(seconds<5&&Numbered(many)&&!Validate(check,many).Contains('1'),"A footer page number with repeated separates was lost or read again at each one ("+seconds.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+" s)");
        // A Word TIFF is stored as PNG. Data without a known picture signature never reaches ImageMagick, which would otherwise pick
        // a coder from the bytes themselves; an SVG text is left out although ImageMagick could draw it.
        byte[] tiff;using(var picture=new ImageMagick.MagickImage(Convert.FromBase64String(Png),new ImageMagick.MagickReadSettings{Format=ImageMagick.MagickFormat.Png}))tiff=picture.ToByteArray(ImageMagick.MagickFormat.Tiff);
        static string Drawing(string id)=>"<w:r><w:drawing><wp:inline><wp:extent cx=\"952500\" cy=\"952500\"/><a:graphic><a:graphicData><pic:pic><pic:blipFill><a:blip r:embed=\""+id+"\"/></pic:blipFill></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r>";
        var pictures=Path.Combine(root,"Pictures.docx");
        using(var zip=ZipFile.Open(pictures,ZipArchiveMode.Create))
        {
            Entry(zip,"word/document.xml","<w:document "+w+"><w:body><w:p>"+Drawing("rTiff")+Drawing("rSvg")+"</w:p></w:body></w:document>");
            Entry(zip,"word/_rels/document.xml.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rTiff\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image1.tiff\"/><Relationship Id=\"rSvg\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image2.svg\"/></Relationships>");
            Entry(zip,"word/media/image2.svg","<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect width=\"8\" height=\"8\"/></svg>");
            using var stored=zip.CreateEntry("word/media/image1.tiff").Open();stored.Write(tiff);
        }
        var picturesUdf=Path.Combine(root,"Pictures.udf");OfficeDocuments.DocxToUdf(pictures,picturesUdf);var images=OfficeDocuments.ReadUdf(picturesUdf).Descendants("image").ToList();
        check(Validate(check,picturesUdf)=="\uFFFC\n"&&images.Count==1&&Convert.FromBase64String(images[0].Attribute("imageData")!.Value).AsSpan().StartsWith(new byte[]{0x89,0x50,0x4E,0x47}),"A Word TIFF was not stored as PNG, or unrecognised picture data reached ImageMagick");
        // A converted picture whose PNG would outgrow the limit UDF -> DOCX reads back is scaled down in proportion instead of being lost there.
        var noise=new byte[64*32*3];new Random(7).NextBytes(noise);byte[] noisy;using(var picture=new ImageMagick.MagickImage()){picture.ReadPixels(noise,new ImageMagick.PixelReadSettings(64,32,ImageMagick.StorageType.Char,ImageMagick.PixelMapping.RGB));noisy=picture.ToByteArray(ImageMagick.MagickFormat.Tiff);}
        var shrunk=PhotoCodec.Png(noisy,ImageMagick.MagickFormat.Tiff,3000);var full=PhotoCodec.Png(noisy,ImageMagick.MagickFormat.Tiff);var shrunkSize=PhotoCodec.Size(shrunk,ImageMagick.MagickFormat.Png);
        check(full.Length>3000&&shrunk.Length<=3000&&shrunk.AsSpan().StartsWith(new byte[]{0x89,0x50,0x4E,0x47})&&shrunkSize.Width<64&&Math.Abs(shrunkSize.Width/(double)shrunkSize.Height-2)<.15,"A converted picture over the size limit was not scaled down in proportion: "+shrunk.Length+" bytes, "+shrunkSize);
    }
}
