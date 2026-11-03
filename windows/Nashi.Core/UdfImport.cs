using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;
namespace Nashi.Core;
// UYAP Doküman Editörü (.udf) documents are ZIP packages around content.xml. Its CDATA block holds the whole text, and
// <elements> lays it out as paragraphs, runs and tables that point into that text by UTF-16 offset, as the editor's Java
// Swing document does; the attribute names are Swing's StyleConstants. Each paragraph's range ends with its "\n", an empty
// paragraph holds a zero-width space, header text comes first and footer text last. Both directions go through DOCX, so
// Office converts every other format and DOCX itself needs no Office tools.
public sealed partial class OfficeDocuments
{
    public static void UdfToDocx(string input,string output)=>new UdfImport(ReadUdf(input)).Write(output);
    internal static XElement ReadUdf(string input)
    {
        using var zip=ZipFile.OpenRead(input);if(zip.Entries.Count>1000)throw new InvalidDataException("This UDF document is too large.");
        var entry=zip.GetEntry("content.xml")??throw new InvalidDataException("This is not a UYAP document: content.xml is missing.");
        const int limit=256_000_000;if(entry.Length>limit)throw new InvalidDataException("This UDF document is too large.");
        using var memory=new MemoryStream();using(var stream=entry.Open()){var buffer=new byte[81920];int count;while((count=stream.Read(buffer))>0){if(memory.Length+count>limit)throw new InvalidDataException("This UDF document is too large.");memory.Write(buffer,0,count);}}
        // The editor writes UTF-8; content that is not valid UTF-8 comes from older exports in the Turkish code page.
        var data=memory.ToArray();string xml;try{xml=XmlText(data);}catch(XmlException){Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);xml=Encoding.GetEncoding(1254).GetString(data);}
        var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Ignore,XmlResolver=null,MaxCharactersInDocument=limit};
        using(var scan=XmlReader.Create(new StringReader(xml),settings))while(scan.Read())if(scan.Depth>256)throw new InvalidDataException("This document nests its content too deeply to convert.");
        using var reader=XmlReader.Create(new StringReader(xml),settings);var root=XDocument.Load(reader).Root;
        return root is not null&&root.Name.LocalName=="template"?root:throw new InvalidDataException("This is not a UYAP document: content.xml has no template.");
    }
    /// Characters XML 1.0 cannot hold (control characters, unpaired surrogates) are left out; offsets are counted after this.
    internal static string XmlSafe(string value)
    {
        if(value.All(XmlConvert.IsXmlChar))return value;var safe=new StringBuilder(value.Length);
        for(var i=0;i<value.Length;i++){if(XmlConvert.IsXmlChar(value[i]))safe.Append(value[i]);else if(i+1<value.Length&&XmlConvert.IsXmlSurrogatePair(value[i+1],value[i])){safe.Append(value,i,2);i++;}}
        return safe.ToString();
    }
    private static string? ImageExtension(byte[] data)=>data switch{[0x89,0x50,0x4E,0x47,..]=>"png",[0xFF,0xD8,..]=>"jpeg",[0x47,0x49,0x46,..]=>"gif",[0x42,0x4D,..]=>"bmp",_=>null};
    private sealed class UdfImport
    {
        private const string Namespaces=" xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"";
        private const string Relationship="http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
        private sealed class Part(string name,string? color){public string Name=name;public string? Color=color;public List<string> Relations=[];}
        private readonly XElement template;private readonly string text;private readonly Dictionary<string,XElement> styles=new(StringComparer.Ordinal);private readonly XElement? page;
        private readonly List<(string Name,byte[] Data)> media=[];private readonly Dictionary<string,int> lists=new(StringComparer.Ordinal);
        private readonly double width;private int pictures;private bool pageBreak;private XElement? header,footer;
        public UdfImport(XElement template)
        {
            this.template=template;text=template.Element("content")?.Value??"";page=template.Element("properties")?.Element("pageFormat");
            foreach(var style in template.Element("styles")?.Elements("style")??[])if(style.Attribute("name")?.Value is {} name)styles[name]=style;
            width=Math.Max(72,PageSize().Width-Margin("leftMargin",42.52)-Margin("rightMargin",28.35));
        }
        private static double? Number(string? value)=>double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)?n:null;
        private static string Twips(double points)=>((long)Math.Round(Math.Clamp(points,-31680,31680)*20)).ToString(CultureInfo.InvariantCulture);
        private static string? Rgb(string? argb)=>int.TryParse(argb,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var n)?(n&0xFFFFFF).ToString("X6",CultureInfo.InvariantCulture):null;
        private static string Escaped(string value)=>SecurityElement.Escape(XmlSafe(value));
        // Java's logical fonts have no file of their own; these are what they show on Windows.
        private static string? Font(string? family)=>family switch{null or ""=>null,"Dialog" or "SansSerif"=>"Arial","Serif"=>"Times New Roman","Monospaced" or "DialogInput"=>"Courier New",_=>family};
        private double Margin(string name,double fallback)=>Number(page?.Attribute(name)?.Value) is {} margin&&margin is >=0 and <1000?margin:fallback;
        // UYAP writes A4 (mediaSizeName 1) and Java's orientations, where only 1 is portrait.
        private bool Landscape=>page?.Attribute("paperOrientation")?.Value is {} orientation&&orientation!="1";
        private (double Width,double Height) PageSize()=>Landscape?(841.89,595.28):(595.28,841.89);
        // A missing attribute comes from the element's resolver style, then from that style's own resolver (edf_* styles resolve to hvl-default).
        private string? Value(XElement? element,string name)
        {
            for(var depth=0;element is not null&&depth<16;depth++){if(element.Attribute(name)?.Value is {} value)return value;element=element.Attribute("resolver")?.Value is {} resolver&&styles.TryGetValue(resolver,out var style)&&style!=element?style:null;}
            return null;
        }
        private string? Value(XElement leaf,XElement paragraph,string name)=>Value(leaf,name)??Value(paragraph,name);
        public void Write(string output)
        {
            var document=new Part("document",null);var body=new StringBuilder();if(template.Element("elements") is {} elements)Content(elements,body,0,document);
            if(body.Length==0||EndsWithTable(body))body.Append("<w:p/>");
            var headerLater=Number(header?.Attribute("startPage")?.Value)>=2;var footerLater=Number(footer?.Attribute("startPage")?.Value)>=2;var titlePage=header is not null&&headerLater||footer is not null&&footerLater;
            var parts=new List<(string File,string Xml,Part Part,string Type,string Kind)>();
            void Add(XElement container,string kind,string type,bool blank){var part=new Part(kind+(parts.Count+1),Rgb(container.Attribute("foreground")?.Value));parts.Add((part.Name+".xml",Furniture(blank?null:container,kind=="header"?"hdr":"ftr",part),part,type,kind));}
            // A header that starts on page 2 leaves the first page's header empty, as UYAP shows it.
            if(header is not null){Add(header,"header","default",false);if(titlePage)Add(header,"header","first",headerLater);}
            if(footer is not null){Add(footer,"footer","default",false);if(titlePage)Add(footer,"footer","first",footerLater);}
            var (pageWidth,pageHeight)=PageSize();var xml=new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document").Append(Namespaces).Append("><w:body>").Append(body).Append("<w:sectPr>");
            foreach(var part in parts)xml.Append("<w:").Append(part.Kind).Append("Reference w:type=\"").Append(part.Type).Append("\" r:id=\"rId").Append(part.Part.Name).Append("\"/>");
            xml.Append("<w:pgSz w:w=\"").Append(Twips(pageWidth)).Append("\" w:h=\"").Append(Twips(pageHeight)).Append('"').Append(Landscape?" w:orient=\"landscape\"":"").Append("/><w:pgMar w:top=\"").Append(Twips(Margin("topMargin",14.17))).Append("\" w:right=\"").Append(Twips(Margin("rightMargin",28.35))).Append("\" w:bottom=\"").Append(Twips(Margin("bottomMargin",14.17))).Append("\" w:left=\"").Append(Twips(Margin("leftMargin",42.52))).Append("\" w:header=\"").Append(Twips(Margin("headerFOffset",20))).Append("\" w:footer=\"").Append(Twips(Margin("footerFOffset",20))).Append("\" w:gutter=\"0\"/>");
            if(Number(footer?.Attribute("pageNumber-pageStartNumStr")?.Value) is {} first&&first is >=0 and <100000&&footer?.Attribute("pageNumber-spec") is not null)xml.Append("<w:pgNumType w:start=\"").Append((int)first).Append("\"/>");
            if(titlePage)xml.Append("<w:titlePg/>");xml.Append("</w:sectPr></w:body></w:document>");
            using var zip=ZipFile.Open(output,ZipArchiveMode.Create);
            void Entry(string name,string content){using var writer=new StreamWriter(zip.CreateEntry(name,CompressionLevel.Optimal).Open(),new UTF8Encoding(false));writer.Write(content);}
            string Relations(IEnumerable<string> relations)=>"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"+string.Concat(relations)+"</Relationships>";
            var types=new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            foreach(var extension in new[]{"png","jpeg","gif","bmp"})types.Append("<Default Extension=\"").Append(extension).Append("\" ContentType=\"image/").Append(extension).Append("\"/>");
            types.Append("<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/><Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>");
            if(lists.Count>0)types.Append("<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/>");
            foreach(var part in parts)types.Append("<Override PartName=\"/word/").Append(part.File).Append("\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.").Append(part.Kind).Append("+xml\"/>");
            Entry("[Content_Types].xml",types.Append("</Types>").ToString());
            Entry("_rels/.rels",Relations(["<Relationship Id=\"rId1\" Type=\""+Relationship+"officeDocument\" Target=\"word/document.xml\"/>"]));
            Entry("word/document.xml",xml.ToString());Entry("word/styles.xml",Styles());if(lists.Count>0)Entry("word/numbering.xml",Numbering());
            // Without a TabSet UYAP tabs every 72 points (Swing's default), not every 36 as Word would.
            Entry("word/settings.xml","<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:settings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:defaultTabStop w:val=\"1440\"/><w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat></w:settings>");
            Entry("word/_rels/document.xml.rels",Relations(document.Relations.Append("<Relationship Id=\"rIdStyles\" Type=\""+Relationship+"styles\" Target=\"styles.xml\"/>").Append("<Relationship Id=\"rIdSettings\" Type=\""+Relationship+"settings\" Target=\"settings.xml\"/>").Concat(lists.Count>0?new[]{"<Relationship Id=\"rIdNumbering\" Type=\""+Relationship+"numbering\" Target=\"numbering.xml\"/>"}:[]).Concat(parts.Select(p=>"<Relationship Id=\"rId"+p.Part.Name+"\" Type=\""+Relationship+p.Kind+"\" Target=\""+p.File+"\"/>"))));
            foreach(var part in parts){Entry("word/"+part.File,part.Xml);if(part.Part.Relations.Count>0)Entry("word/_rels/"+part.File+".rels",Relations(part.Part.Relations));}
            foreach(var (name,data) in media){using var stream=zip.CreateEntry("word/media/"+name,CompressionLevel.NoCompression).Open();stream.Write(data);}
        }
        private static bool EndsWithTable(StringBuilder xml)=>xml.Length>=8&&xml.ToString(xml.Length-8,8)=="</w:tbl>";
        private void Content(XElement container,StringBuilder xml,int depth,Part part)
        {
            if(depth>32)throw new InvalidDataException("This document nests its content too deeply to convert.");
            foreach(var element in container.Elements())switch(element.Name.LocalName)
            {
                case "paragraph":Paragraph(element,xml,part);break;
                case "table":Table(element,xml,depth,part);break;
                // udf-cli and UYAP wrap the paragraph after a page break; that paragraph only holds the break's placeholder.
                case "page-break":if(part.Name=="document"&&depth==0)pageBreak=true;break;
                case "header" when depth==0:header??=element;break;
                case "footer" when depth==0:footer??=element;break;
                default:if(element.Elements().Any())Content(element,xml,depth+1,part);break;
            }
        }
        private string Furniture(XElement? container,string root,Part part)
        {
            var xml=new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:").Append(root).Append(Namespaces).Append('>');var start=xml.Length;
            if(container is not null){Content(container,xml,1,part);if(root=="ftr"&&container.Attribute("pageNumber-spec") is not null)PageNumber(container,xml);}
            if(xml.Length==start||EndsWithTable(xml))xml.Append("<w:p/>");return xml.Append("</w:").Append(root).Append('>').ToString();
        }
        // UYAP draws the page number itself from the footer's pageNumber-* settings, at the bottom right: an optional label,
        // the page and, with a separator, the page count.
        private static void PageNumber(XElement footer,StringBuilder xml)
        {
            string? Setting(string name)=>footer.Attribute("pageNumber-"+name)?.Value;var properties=new StringBuilder("<w:rPr>");
            if(Font(Setting("fontFace")) is {} font)properties.Append("<w:rFonts w:ascii=\"").Append(Escaped(font)).Append("\" w:hAnsi=\"").Append(Escaped(font)).Append("\"/>");if(Setting("fontBold")=="true")properties.Append("<w:b/>");if(Setting("fontItalic")=="true")properties.Append("<w:i/>");
            if(Rgb(Setting("color")) is {} color)properties.Append("<w:color w:val=\"").Append(color).Append("\"/>");if(Number(Setting("fontSize")) is {} size&&size is >0 and <1000)properties.Append("<w:sz w:val=\"").Append((int)Math.Round(size*2)).Append("\"/>");
            var run="<w:r>"+properties.Append("</w:rPr>")+"<w:t xml:space=\"preserve\">";xml.Append("<w:p><w:pPr><w:jc w:val=\"right\"/></w:pPr>");
            if(Setting("foreStr") is {Length:>0} label)xml.Append(run).Append(Escaped(label+" ")).Append("</w:t></w:r>");xml.Append("<w:fldSimple w:instr=\" PAGE \">").Append(run).Append("1</w:t></w:r></w:fldSimple>");
            if(Setting("seperator") is {Length:>0} separator)xml.Append(run).Append(Escaped(separator)).Append("</w:t></w:r><w:fldSimple w:instr=\" NUMPAGES \">").Append(run).Append("1</w:t></w:r></w:fldSimple>");
            xml.Append("</w:p>");
        }
        private void Paragraph(XElement paragraph,StringBuilder xml,Part part)
        {
            // The leaves that point into the text, in document order; the paragraph's own "\n" ends the last of them.
            var leaves=new List<(XElement Leaf,string Text)>();
            foreach(var leaf in paragraph.Elements())
            {
                if(leaf.Name.LocalName=="image"){leaves.Add((leaf,""));continue;}
                if(!int.TryParse(leaf.Attribute("startOffset")?.Value,NumberStyles.None,CultureInfo.InvariantCulture,out var start)||!int.TryParse(leaf.Attribute("length")?.Value,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var length)||length<=0||start>=text.Length)continue;
                leaves.Add((leaf,text.Substring(start,Math.Min(length,text.Length-start))));
            }
            for(var i=leaves.Count-1;i>=0;i--)if(leaves[i].Leaf.Name.LocalName!="image"){if(leaves[i].Text.EndsWith('\n'))leaves[i]=(leaves[i].Leaf,leaves[i].Text[..^1]);break;}
            // Images take one U+FFFC each, and an empty paragraph holds a zero-width space so that it has a range at all.
            for(var i=0;i<leaves.Count;i++)leaves[i]=(leaves[i].Leaf,leaves[i].Text.Replace("\uFFFC",""));if(leaves.All(l=>l.Leaf.Name.LocalName!="image"&&l.Text.Trim('\u200B').Length==0))leaves.Clear();
            xml.Append("<w:p><w:pPr>");if(pageBreak&&part.Name=="document"){xml.Append("<w:pageBreakBefore/>");pageBreak=false;}
            var numbered=Value(paragraph,"Numbered")=="true";
            if(numbered||Value(paragraph,"Bulleted")=="true"){var level=Math.Clamp((int)(Number(Value(paragraph,"ListLevel"))??1),1,9);xml.Append("<w:numPr><w:ilvl w:val=\"").Append(level-1).Append("\"/><w:numId w:val=\"").Append(List((numbered?Value(paragraph,"NumberType")??"NUMBER_TYPE_NUMBER_DOT":Value(paragraph,"BulletType")??"BULLET_TYPE_ELLIPSE")+"\u0000"+(Value(paragraph,"ListId")??""))).Append("\"/></w:numPr>");}
            var left=Number(Value(paragraph,"LeftIndent"));var tabs=Tabs(Value(paragraph,"TabSet"),left??0);if(tabs.Length>0)xml.Append("<w:tabs>").Append(tabs).Append("</w:tabs>");
            // Swing's LineSpacing is the space added between lines as a share of the line height.
            xml.Append("<w:spacing w:before=\"").Append(Twips(Number(Value(paragraph,"SpaceAbove"))??0)).Append("\" w:after=\"").Append(Twips(Number(Value(paragraph,"SpaceBelow"))??0)).Append("\" w:line=\"").Append((int)Math.Round(240*(1+Math.Clamp(Number(Value(paragraph,"LineSpacing"))??0,0,10)))).Append("\" w:lineRule=\"auto\"/>");
            // UYAP's Hanging moves the lines after the first ("DAVACI<tab>: …" wraps under the text), while LeftIndent, the
            // first line and the tab stops stay where they are. A list paragraph without indents of its own keeps the numbering's.
            var right=Number(Value(paragraph,"RightIndent"));var firstLine=Number(Value(paragraph,"FirstLineIndent"));var hanging=Number(Value(paragraph,"Hanging")) is >0 and var h?h:0;
            if(left is not null||right is not null||firstLine is not null||hanging>0){var offset=(firstLine??0)-hanging;xml.Append("<w:ind w:left=\"").Append(Twips((left??0)+hanging)).Append("\" w:right=\"").Append(Twips(right??0)).Append(offset<0?"\" w:hanging=\"":"\" w:firstLine=\"").Append(Twips(Math.Abs(offset))).Append("\"/>");}
            xml.Append("<w:jc w:val=\"").Append(Value(paragraph,"Alignment") switch{"1"=>"center","2"=>"right","3"=>"both",_=>"left"}).Append("\"/></w:pPr>");
            foreach(var (leaf,value) in leaves)if(leaf.Name.LocalName=="image")Image(leaf,xml,part);else if(value.Length>0)Run(leaf,paragraph,value,xml,part);
            xml.Append("</w:p>");
        }
        // TabSet lists "position:alignment:leader" stops, comma separated, with Swing's TabStop constants. Swing measures them
        // from the paragraph's left indent, Word from the margin.
        private static string Tabs(string? set,double left)
        {
            var xml=new StringBuilder();
            foreach(var stop in (set??"").Split([',',';'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
            {
                var parts=stop.Split(':');if(Number(parts[0]) is not {} position||position is <0 or >2000)continue;
                var align=parts.Length>1?parts[1] switch{"1"=>"right","2"=>"center","4"=>"decimal","5"=>"bar",_=>"left"}:"left";var leader=parts.Length>2?parts[2] switch{"1"=>"dot","2"=>"hyphen","3"=>"underscore","4"=>"heavy",_=>"none"}:"none";
                xml.Append("<w:tab w:val=\"").Append(align).Append("\" w:leader=\"").Append(leader).Append("\" w:pos=\"").Append(Twips(left+position)).Append("\"/>");
            }
            return xml.ToString();
        }
        private void Run(XElement leaf,XElement paragraph,string value,StringBuilder xml,Part part)
        {
            var properties=new StringBuilder();string? Get(string name)=>Value(leaf,paragraph,name);
            if(Font(Get("family")) is {} family){var font=Escaped(family);properties.Append("<w:rFonts w:ascii=\"").Append(font).Append("\" w:hAnsi=\"").Append(font).Append("\" w:cs=\"").Append(font).Append("\" w:eastAsia=\"").Append(font).Append("\"/>");}
            if(Get("bold")=="true")properties.Append("<w:b/><w:bCs/>");if(Get("italic")=="true")properties.Append("<w:i/><w:iCs/>");if(Get("strikethrough")=="true")properties.Append("<w:strike/>");
            if((Rgb(Get("foreground"))??part.Color) is {} color)properties.Append("<w:color w:val=\"").Append(color).Append("\"/>");
            if(Number(Get("size")) is {} size&&size is >0 and <1000){var half=(int)Math.Round(size*2);properties.Append("<w:sz w:val=\"").Append(half).Append("\"/><w:szCs w:val=\"").Append(half).Append("\"/>");}
            if(Get("underline")=="true")properties.Append("<w:u w:val=\"single\"/>");
            if(Rgb(Get("background")) is {} background&&background!="FFFFFF")properties.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"").Append(background).Append("\"/>");
            if(Get("superscript")=="true")properties.Append("<w:vertAlign w:val=\"superscript\"/>");else if(Get("subscript")=="true")properties.Append("<w:vertAlign w:val=\"subscript\"/>");
            xml.Append("<w:r>");if(properties.Length>0)xml.Append("<w:rPr>").Append(properties).Append("</w:rPr>");var pending=new StringBuilder();
            void Flush(){if(pending.Length>0)xml.Append("<w:t xml:space=\"preserve\">").Append(Escaped(pending.ToString())).Append("</w:t>");pending.Clear();}
            foreach(var c in value){if(c=='\t'){Flush();xml.Append("<w:tab/>");}else if(c=='\n'){Flush();xml.Append("<w:br/>");}else if(c!='\r')pending.Append(c);}
            Flush();xml.Append("</w:r>");
        }
        private void Image(XElement image,StringBuilder xml,Part part)
        {
            byte[] data;try{data=Convert.FromBase64String(image.Attribute("imageData")?.Value??"");}catch(FormatException){return;}
            if(data.Length>20_000_000||ImageExtension(data) is not {} extension)return;
            // Sizes are pixels at 96 DPI, as udf-cli and the UDF toolkit write them; an image never grows past the text width.
            var w=Number(image.Attribute("width")?.Value);var h=Number(image.Attribute("height")?.Value);
            if(w is not >0||h is not >0){try{var info=new ImageMagick.MagickImageInfo(data);w=info.Width;h=info.Height;}catch(Exception error)when(error is ImageMagick.MagickException or ArgumentException){return;}if(w is not >0||h is not >0)return;}
            var scale=Math.Min(1,width*96/72/w.Value);var cx=(long)(w.Value*scale*9525);var cy=(long)(h.Value*scale*9525);
            pictures++;var id="rIdImage"+pictures;var name="image"+pictures+"."+extension;media.Add((name,data));part.Relations.Add("<Relationship Id=\""+id+"\" Type=\""+Relationship+"image\" Target=\"media/"+name+"\"/>");
            xml.Append("<w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"").Append(cx).Append("\" cy=\"").Append(cy).Append("\"/><wp:docPr id=\"").Append(pictures).Append("\" name=\"Picture ").Append(pictures)
                .Append("\"/><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:nvPicPr><pic:cNvPr id=\"").Append(pictures).Append("\" name=\"").Append(name).Append("\"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed=\"").Append(id)
                .Append("\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"").Append(cx).Append("\" cy=\"").Append(cy).Append("\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r>");
        }
        private void Table(XElement table,StringBuilder xml,int depth,Part part)
        {
            var rows=table.Elements("row").ToList();if(rows.Count==0)return;
            static int Span(XElement cell,string name)=>int.TryParse(cell.Attribute(name)?.Value??cell.Attribute(name.Replace("span","Span"))?.Value,NumberStyles.None,CultureInfo.InvariantCulture,out var n)?Math.Clamp(n,1,63):1;
            var columns=Math.Clamp(Math.Max((int)(Number(table.Attribute("columnCount")?.Value)??0),rows.Max(r=>r.Elements("cell").Sum(c=>Span(c,"colspan")))),1,63);
            // columnSpans are relative widths; the table fills the text width as in UYAP.
            var spans=(table.Attribute("columnSpans")?.Value??"").Split(',',StringSplitOptions.TrimEntries).Select(Number).ToArray();
            var widths=spans.Length==columns&&spans.All(s=>s>0)?spans.Select(s=>s!.Value/spans.Sum(v=>v!.Value)*width).ToArray():Enumerable.Repeat(width/columns,columns).ToArray();
            if(pageBreak&&part.Name=="document"){xml.Append("<w:p><w:pPr><w:pageBreakBefore/></w:pPr></w:p>");pageBreak=false;}
            var border=table.Attribute("border")?.Value=="borderNone"?"w:val=\"nil\"":"w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"000000\"";
            xml.Append("<w:tbl><w:tblPr><w:tblW w:w=\"").Append(Twips(widths.Sum())).Append("\" w:type=\"dxa\"/><w:tblBorders>");foreach(var side in new[]{"top","left","bottom","right","insideH","insideV"})xml.Append("<w:").Append(side).Append(' ').Append(border).Append("/>");
            xml.Append("</w:tblBorders><w:tblLayout w:type=\"fixed\"/><w:tblCellMar><w:left w:w=\"57\" w:type=\"dxa\"/><w:right w:w=\"57\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr><w:tblGrid>");foreach(var w in widths)xml.Append("<w:gridCol w:w=\"").Append(Twips(w)).Append("\"/>");xml.Append("</w:tblGrid>");
            // Rows still covered by a rowspan above, and how many grid columns that cell spans, per grid column.
            var covered=new int[columns];var coveredSpan=new int[columns];
            void Cell(int column,int span,string merge,string properties,XElement? content)
            {
                xml.Append("<w:tc><w:tcPr><w:tcW w:w=\"").Append(Twips(widths.Skip(column).Take(span).Sum())).Append("\" w:type=\"dxa\"/>");if(span>1)xml.Append("<w:gridSpan w:val=\"").Append(span).Append("\"/>");xml.Append(merge).Append(properties).Append("</w:tcPr>");
                var start=xml.Length;if(content is not null)Content(content,xml,depth+1,part);if(xml.Length==start||EndsWithTable(xml))xml.Append("<w:p/>");xml.Append("</w:tc>");
            }
            foreach(var row in rows)
            {
                xml.Append("<w:tr>");var cells=new Queue<XElement>(row.Elements("cell"));
                for(var column=0;column<columns;)
                {
                    if(covered[column]>0){covered[column]--;var span=coveredSpan[column];Cell(column,span,"<w:vMerge/>","",null);column+=span;continue;}
                    if(!cells.TryDequeue(out var cell)){Cell(column,1,"","",null);column++;continue;}
                    var colspan=Math.Min(Span(cell,"colspan"),columns-column);var rowspan=Span(cell,"rowspan");if(rowspan>1){covered[column]=rowspan-1;coveredSpan[column]=colspan;}
                    var properties=new StringBuilder();if(Rgb(cell.Attribute("fillColor")?.Value) is {} fill&&fill!="FFFFFF")properties.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"").Append(fill).Append("\"/>");
                    if(cell.Attribute("align")?.Value is "vcenter" or "center" or "middle")properties.Append("<w:vAlign w:val=\"center\"/>");else if(cell.Attribute("align")?.Value=="bottom")properties.Append("<w:vAlign w:val=\"bottom\"/>");
                    Cell(column,colspan,rowspan>1?"<w:vMerge w:val=\"restart\"/>":"",properties.ToString(),cell);column+=colspan;
                }
                xml.Append("</w:tr>");
            }
            xml.Append("</w:tbl>");
        }
        private int List(string key){if(!lists.TryGetValue(key,out var id))lists[key]=id=lists.Count+1;return id;}
        // UYAP numbers lists itself from these types; the same type and list id continue one list.
        private static (string Format,string Text) ListFormat(string type,int level)=>type switch
        {
            "NUMBER_TYPE_NUMBER_PARANTHESE"=>("decimal","%"+level+")"),"NUMBER_TYPE_NUMBER_TRE"=>("decimal","%"+level+"-"),
            "NUMBER_TYPE_CHAR_SMALL_DOT"=>("lowerLetter","%"+level+"."),"NUMBER_TYPE_CHAR_SMALL_PARANTHESE"=>("lowerLetter","%"+level+")"),"NUMBER_TYPE_CHAR_BIG_DOT"=>("upperLetter","%"+level+"."),
            "NUMBER_TYPE_ROMAN_SMALL_DOT"=>("lowerRoman","%"+level+"."),"NUMBER_TYPE_ROMAN_BIG_DOT"=>("upperRoman","%"+level+"."),
            "BULLET_TYPE_RECTANGLE" or "BULLET_TYPE_RECTANGLE_D"=>("bullet","\u25AA"),"BULLET_TYPE_DIAMOND"=>("bullet","\u25C6"),"BULLET_TYPE_ARROW" or "BULLET_TYPE_TRIANGLE"=>("bullet","\u25BA"),
            _ when type.StartsWith("BULLET",StringComparison.Ordinal)=>("bullet","\u2022"),_=>("decimal","%"+level+".")
        };
        private string Numbering()
        {
            var xml=new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:numbering xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">");
            foreach(var (key,id) in lists)
            {
                xml.Append("<w:abstractNum w:abstractNumId=\"").Append(id).Append("\"><w:multiLevelType w:val=\"multilevel\"/>");
                for(var level=0;level<9;level++){var (format,label)=ListFormat(key.Split('\u0000')[0],level+1);xml.Append("<w:lvl w:ilvl=\"").Append(level).Append("\"><w:start w:val=\"1\"/><w:numFmt w:val=\"").Append(format).Append("\"/><w:lvlText w:val=\"").Append(Escaped(label)).Append("\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"").Append(360*(level+1)).Append("\" w:hanging=\"360\"/></w:pPr></w:lvl>");}
                xml.Append("</w:abstractNum>");
            }
            foreach(var id in lists.Values)xml.Append("<w:num w:numId=\"").Append(id).Append("\"><w:abstractNumId w:val=\"").Append(id).Append("\"/></w:num>");
            return xml.Append("</w:numbering>").ToString();
        }
        // UYAP's body text is hvl-default (Times New Roman 12 unless the document says otherwise), with no space after paragraphs.
        private string Styles()
        {
            styles.TryGetValue("hvl-default",out var body);var font=Escaped(Font(Value(body,"family"))??"Times New Roman");var size=(int)Math.Round(Math.Clamp(Number(Value(body,"size"))??12,1,999)*2);
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\""+font+"\" w:hAnsi=\""+font+"\" w:cs=\""+font+"\" w:eastAsia=\""+font+"\"/><w:sz w:val=\""+size+"\"/><w:szCs w:val=\""+size+"\"/><w:lang w:val=\"tr-TR\" w:eastAsia=\"tr-TR\" w:bidi=\"ar-SA\"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>"
                +"<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style><w:style w:type=\"character\" w:default=\"1\" w:styleId=\"DefaultParagraphFont\"><w:name w:val=\"Default Paragraph Font\"/><w:uiPriority w:val=\"1\"/><w:semiHidden/></w:style>"
                +"<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:name w:val=\"Normal Table\"/><w:semiHidden/><w:tblPr><w:tblInd w:w=\"0\" w:type=\"dxa\"/><w:tblCellMar><w:top w:w=\"0\" w:type=\"dxa\"/><w:left w:w=\"57\" w:type=\"dxa\"/><w:bottom w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"57\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr></w:style></w:styles>";
        }
    }
}
