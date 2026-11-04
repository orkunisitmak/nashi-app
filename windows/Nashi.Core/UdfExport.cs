using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
namespace Nashi.Core;
public sealed partial class OfficeDocuments
{
    /// Writes a DOCX as a UYAP document laid out as UYAP and udf-cli write one (see UdfImport.cs). List numbers are written
    /// as the text Word shows, so a petition's "1." and "a)" stay what they were whatever numbering UYAP would apply.
    public static void DocxToUdf(string input,string output)
    {
        using var zip=ZipFile.OpenRead(input);if(zip.Entries.Count>10000||zip.Entries.Sum(e=>e.Length)>512_000_000||zip.Entries.GroupBy(e=>e.FullName,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))throw new InvalidDataException("The document package is too large or contains duplicate entries.");
        new UdfExport(zip).Write(output);
    }
    private sealed class UdfExport
    {
        private const string Styles="<styles><style name=\"default\" description=\"Geçerli\" family=\"Dialog\" size=\"12\" bold=\"false\" italic=\"false\" foreground=\"-13421773\" FONT_ATTRIBUTE_KEY=\"javax.swing.plaf.FontUIResource[family=Dialog,name=Dialog,style=plain,size=12]\" /><style name=\"hvl-default\" family=\"Times New Roman\" size=\"12\" description=\"Gövde\" /></styles>";
        // The page number settings of a footer UYAP wrote. UYAP draws "label page/count" itself at the bottom right, so a footer
        // line holding Word's PAGE and NUMPAGES fields becomes these settings, with the line's own label and separator.
        private string PageNumbers=>" pageNumber-spec=\"BSP32_2120\" pageNumber-color=\"-16777216\" pageNumber-fontFace=\"Arial\" pageNumber-fontSize=\"11\" pageNumber-fontBold=\"false\" pageNumber-fontItalic=\"false\" pageNumber-foreStr=\""+SecurityElement.Escape(XmlSafe(pageLabel.Length>0?pageLabel+" ":""))+"\" pageNumber-seperator=\""+SecurityElement.Escape(XmlSafe(pageSeparator))+"\"";
        private readonly record struct Segment(char Kind,string Text,Dictionary<string,string>? Format,byte[]? Image=null,double Width=0,double Height=0);
        private readonly ZipArchive zip;private readonly Dictionary<string,XElement> styles=new(StringComparer.Ordinal);private readonly XElement? defaults,numbering;private readonly string? normal;private readonly string majorFont,minorFont;
        private readonly Dictionary<string,(Dictionary<string,string> C,Dictionary<string,string> P)> resolved=new(StringComparer.Ordinal);
        private readonly Dictionary<string,int[]> counters=new(StringComparer.Ordinal);private readonly HashSet<string> restarted=new(StringComparer.Ordinal);
        private readonly Stack<(StringBuilder Instruction,bool Result,string? Page)> fields=new();private int instructions;private readonly StringBuilder text=new();
        private Dictionary<string,string> relations;private bool furniture,pageNumbers;private int cells;private string pageLabel="",pageSeparator="/";private double defaultTab=36,textWidth=453.5;
        public UdfExport(ZipArchive zip)
        {
            this.zip=zip;var styleXml=zip.GetEntry("word/styles.xml") is not null?Xml(zip,"word/styles.xml").Root:null;defaults=Child(styleXml,"docDefaults");
            foreach(var style in styleXml?.Elements().Where(e=>e.Name.LocalName=="style")??[])if(Attr(style,"styleId") is {} id)styles[id]=style;
            normal=Attr(styles.Values.LastOrDefault(e=>(Attr(e,"type")??"paragraph")=="paragraph"&&Attr(e,"default") is "1" or "true" or "on"),"styleId");
            numbering=zip.GetEntry("word/numbering.xml") is not null?Xml(zip,"word/numbering.xml").Root:null;
            var theme=zip.GetEntry("word/theme/theme1.xml") is not null?Xml(zip,"word/theme/theme1.xml").Root:null;
            string Typeface(string name)=>Attr(theme?.Descendants().FirstOrDefault(e=>e.Name.LocalName==name)?.Elements().FirstOrDefault(e=>e.Name.LocalName=="latin"),"typeface") is {Length:>0} font?font:"Times New Roman";
            majorFont=Typeface("majorFont");minorFont=Typeface("minorFont");relations=Relations("word/document.xml");
            if(zip.GetEntry("word/settings.xml") is not null&&Num(Xml(zip,"word/settings.xml").Root?.Elements().FirstOrDefault(e=>e.Name.LocalName=="defaultTabStop"),"val") is >0 and var interval)defaultTab=interval/20;
        }
        private Dictionary<string,string> Relations(string part)
        {
            var folder=Path.GetDirectoryName(part)!.Replace('\\','/');var file=folder+"/_rels/"+Path.GetFileName(part)+".rels";var result=new Dictionary<string,string>(StringComparer.Ordinal);if(zip.GetEntry(file) is null)return result;
            foreach(var relation in Xml(zip,file).Root!.Elements())if(Attr(relation,"Id") is {} id&&Attr(relation,"Target") is {} target&&Attr(relation,"TargetMode")!="External"&&Local(folder,target) is {} local)result[id]=local;
            return result;
        }
        private static string? Local(string folder,string target)
        {
            if(!target.StartsWith('/')&&Uri.TryCreate(target,UriKind.Absolute,out _))return null;var stack=target.StartsWith('/')?new List<string>():folder.Split('/').ToList();
            foreach(var p in target.Replace('\\','/').Split('/')){if(p is "" or ".")continue;if(p==".."){if(stack.Count==0)return null;stack.RemoveAt(stack.Count-1);}else stack.Add(p);}
            return string.Join('/',stack);
        }
        private static string Points(double value)=>Math.Round(value,3).ToString("0.0##",CultureInfo.InvariantCulture);
        private static string? Argb(string? hex)=>hex?.Length==6&&int.TryParse(hex,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var rgb)?(unchecked((int)0xFF000000)|rgb).ToString(CultureInfo.InvariantCulture):null;
        private static bool On(XElement e)=>Attr(e,"val") is not ("0" or "false" or "off");
        // Character and paragraph properties are kept under their UDF attribute names; "#" keys are Word details used along the way.
        private void CharacterInto(XElement? properties,Dictionary<string,string> d)
        {
            if(properties is null)return;foreach(var (name,key) in new[]{("b","bold"),("i","italic"),("strike","strikethrough"),("vanish","#hidden")})if(Child(properties,name) is {} p)d[key]=On(p)?"true":"false";
            if(Child(properties,"dstrike") is {} doubled&&On(doubled))d["strikethrough"]="true";if(Num(Child(properties,"sz"),"val") is {} size)d["size"]=Points(size/2);
            var fonts=Child(properties,"rFonts");var font=(Attr(fonts,"asciiTheme")??Attr(fonts,"hAnsiTheme")) is {} theme?theme.StartsWith("major",StringComparison.Ordinal)?majorFont:minorFont:Attr(fonts,"ascii")??Attr(fonts,"hAnsi");if(font is {Length:>0})d["family"]=font;
            if(Attr(Child(properties,"color"),"val") is {} color){if(Argb(color) is {} argb)d["foreground"]=argb;else d.Remove("foreground");}
            if(Child(properties,"u") is {} u)d["underline"]=Attr(u,"val") is "none" or "0"?"false":"true";
            if(Attr(Child(properties,"vertAlign"),"val") is {} align){d["superscript"]=align=="superscript"?"true":"false";d["subscript"]=align=="subscript"?"true":"false";}
            var highlight=Attr(Child(properties,"highlight"),"val") switch{"black"=>"000000","blue"=>"0000FF","cyan"=>"00FFFF","green"=>"00FF00","magenta"=>"FF00FF","red"=>"FF0000","yellow"=>"FFFF00","white"=>"FFFFFF","darkBlue"=>"000080","darkCyan"=>"008080","darkGreen"=>"008000","darkMagenta"=>"800080","darkRed"=>"800000","darkYellow"=>"808000","darkGray"=>"808080","lightGray"=>"C0C0C0","none"=>"",_=>null};
            if(highlight=="")d.Remove("background");else if(Argb(highlight??Attr(Child(properties,"shd"),"fill")) is {} background)d["background"]=background;
        }
        private static void ParagraphInto(XElement? properties,Dictionary<string,string> d)
        {
            if(properties is null)return;if(Attr(Child(properties,"jc"),"val") is {} jc)d["Alignment"]=jc switch{"center"=>"1","right" or "end"=>"2","both" or "distribute"=>"3",_=>"0"};
            var spacing=Child(properties,"spacing");if(Num(spacing,"before") is {} before)d["SpaceAbove"]=Points(before/20);if(Num(spacing,"after") is {} after)d["SpaceBelow"]=Points(after/20);
            // Word's single line is 240; Swing's LineSpacing is the space added between lines as a share of the line height.
            if(Num(spacing,"line") is {} line&&Attr(spacing,"lineRule") is null or "auto")d["LineSpacing"]=Points(Math.Clamp(line/240-1,0,10));
            var indent=Child(properties,"ind");if((Num(indent,"left")??Num(indent,"start")) is {} left)d["LeftIndent"]=Points(left/20);if((Num(indent,"right")??Num(indent,"end")) is {} right)d["RightIndent"]=Points(right/20);
            if(Num(indent,"firstLine") is {} first)d["FirstLineIndent"]=Points(first/20);if(Num(indent,"hanging") is {} hanging)d["FirstLineIndent"]=Points(-hanging/20);
            if(Child(properties,"numPr") is {} list){if(Attr(Child(list,"numId"),"val") is {} id)d["#numId"]=id;if(Attr(Child(list,"ilvl"),"val") is {} level)d["#ilvl"]=level;}
            if(Child(properties,"pageBreakBefore") is {} pageBreak)d["#pageBreakBefore"]=On(pageBreak)?"true":"false";
            foreach(var tab in Child(properties,"tabs")?.Elements().Where(e=>e.Name.LocalName=="tab")??[])if(Num(tab,"pos") is {} position)
                d["#tab:"+((int)position).ToString(CultureInfo.InvariantCulture)]=Attr(tab,"val") is null or "clear"?"clear":(Attr(tab,"val") switch{"right" or "end"=>"1","center"=>"2",_=>"0"})+":"+(Attr(tab,"leader") switch{"dot" or "middleDot"=>"1","hyphen"=>"2","underscore"=>"3","heavy"=>"4",_=>"0"});
        }
        private (Dictionary<string,string> C,Dictionary<string,string> P) Style(string? id,int depth=0)
        {
            if(depth>=16||id is null||!styles.TryGetValue(id,out var style)){var c=new Dictionary<string,string>(StringComparer.Ordinal);CharacterInto(Child(Child(defaults,"rPrDefault"),"rPr"),c);var p=new Dictionary<string,string>(StringComparer.Ordinal);ParagraphInto(Child(Child(defaults,"pPrDefault"),"pPr"),p);return(c,p);}
            if(depth==0&&resolved.TryGetValue(id,out var cached))return cached;
            var parent=Style(Attr(Child(style,"basedOn"),"val"),depth+1);var character=new Dictionary<string,string>(parent.C,StringComparer.Ordinal);CharacterInto(Child(style,"rPr"),character);var paragraph=new Dictionary<string,string>(parent.P,StringComparer.Ordinal);ParagraphInto(Child(style,"pPr"),paragraph);
            if(depth==0)resolved[id]=(character,paragraph);return(character,paragraph);
        }
        private void RunStyle(string? id,Dictionary<string,string> into,int depth=0){if(depth>=16||id is null||!styles.TryGetValue(id,out var style))return;RunStyle(Attr(Child(style,"basedOn"),"val"),into,depth+1);CharacterInto(Child(style,"rPr"),into);}
        public void Write(string output)
        {
            var body=Child(Xml(zip,"word/document.xml").Root,"body")??throw new InvalidDataException("The document has no editable body.");var section=Child(body,"sectPr");var elements=new StringBuilder();
            var size=Child(section,"pgSz");var margins=Child(section,"pgMar");textWidth=Math.Max(72,((Num(size,"w")??11906)-(Num(margins,"left")??1418)-(Num(margins,"right")??1418))/20);
            // UYAP keeps the header's text first and the footer's last.
            // Word's "different first page" with an empty first header is UYAP's header starting on page 2, as on a court's tensip zaptı.
            bool LaterPages()
            {
                if(Child(section,"titlePg") is not {} title||!On(title))return false;if(Furniture(section,"headerReference","first") is not {} first)return true;
                return PlainText(first.Root).Trim().Length==0&&!first.Root.Descendants().Any(e=>e.Name.LocalName is "drawing" or "pict");
            }
            if(Furniture(section,"headerReference") is {} header){elements.Append(LaterPages()?"<header startPage=\"2\">":"<header>");Part(header,elements);elements.Append("</header>\n");}
            var bodyStart=text.Length;Content(body,elements,0);if(text.Length==bodyStart)Emit(elements,new(StringComparer.Ordinal),[],new(StringComparer.Ordinal),"");
            if(Furniture(section,"footerReference") is {} footer){var footerElements=new StringBuilder();Part(footer,footerElements);elements.Append("<footer").Append(pageNumbers?PageNumbers:"").Append('>').Append(footerElements).Append("</footer>\n");}
            string Margin(string name,double fallback)=>Points(Math.Abs(Num(margins,name) is {} twips?twips/20:fallback));
            var xml=new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" ?>\n<template format_id=\"1.8\">\n<content><![CDATA[").Append(text.ToString().Replace("]]>","]]]]><![CDATA[>")).Append("]]></content>");
            // UYAP pages are A4; Java's orientation 2 (reverse landscape) is landscape however the value is read.
            xml.Append("<properties><pageFormat mediaSizeName=\"1\" leftMargin=\"").Append(Margin("left",70.866)).Append("\" rightMargin=\"").Append(Margin("right",70.866)).Append("\" topMargin=\"").Append(Margin("top",70.866)).Append("\" bottomMargin=\"").Append(Margin("bottom",70.866))
                .Append("\" paperOrientation=\"").Append(Attr(size,"orient")=="landscape"||Num(size,"w")>Num(size,"h")?"2":"1").Append("\" headerFOffset=\"").Append(Margin("header",35.4)).Append("\" footerFOffset=\"").Append(Margin("footer",35.4)).Append("\" /></properties>");
            xml.Append("\n<elements resolver=\"hvl-default\">\n").Append(elements).Append("</elements>\n").Append(Styles).Append("\n</template>\n");
            using var archive=ZipFile.Open(output,ZipArchiveMode.Create);using var writer=new StreamWriter(archive.CreateEntry("content.xml",CompressionLevel.Optimal).Open(),new UTF8Encoding(false));writer.Write(xml);
        }
        private (XElement Root,Dictionary<string,string> Relations)? Furniture(XElement? section,string reference,string? type=null)
        {
            var references=section?.Elements().Where(e=>e.Name.LocalName==reference).ToList()??[];
            var chosen=type is not null?references.FirstOrDefault(e=>Attr(e,"type")==type):references.FirstOrDefault(e=>Attr(e,"type") is null or "default")??references.FirstOrDefault();
            if(Attr(chosen,"id") is not {} id||!relations.TryGetValue(id,out var part)||zip.GetEntry(part) is null)return null;return(Xml(zip,part).Root!,Relations(part));
        }
        private void Part((XElement Root,Dictionary<string,string> Relations) part,StringBuilder xml)
        {
            var body=relations;relations=part.Relations;furniture=true;fields.Clear();instructions=0;try{Content(part.Root,xml,1);}finally{relations=body;furniture=false;fields.Clear();instructions=0;}
        }
        private void Content(XElement container,StringBuilder xml,int depth)
        {
            if(depth>32)throw new InvalidDataException("This document nests its content too deeply to convert.");
            foreach(var element in Blocks(container))if(element.Name.LocalName=="p")Paragraph(element,xml);else if(element.Name.LocalName=="tbl")Table(element,xml,depth);
        }
        private void Paragraph(XElement element,StringBuilder xml)
        {
            var properties=Child(element,"pPr");var style=Style(Attr(Child(properties,"pStyle"),"val")??normal);var paragraph=new Dictionary<string,string>(style.P,StringComparer.Ordinal);
            var mark=new Dictionary<string,string>(style.C,StringComparer.Ordinal);CharacterInto(Child(properties,"rPr"),mark);mark.Remove("#hidden");
            var probe=new Dictionary<string,string>(style.P,StringComparer.Ordinal);ParagraphInto(properties,probe);var runs=new List<Segment>();
            if(probe.GetValueOrDefault("#numId") is {} numId&&numId!="0"&&Label(numId,int.TryParse(probe.GetValueOrDefault("#ilvl"),NumberStyles.None,CultureInfo.InvariantCulture,out var level)?Math.Clamp(level,0,8):0,mark) is {} label)
            {
                // The numbering level's indents apply beneath the paragraph's own, as in Word.
                ParagraphInto(label.Properties,paragraph);if(label.Text.Length>0)runs.Add(new('t',label.Text,label.Format));
                if(label.Suffix=='\t')runs.Add(new('\t',"\t",label.Format));else if(label.Suffix==' ')runs.Add(new('t'," ",label.Format));
            }
            ParagraphInto(properties,paragraph);foreach(var child in element.Elements())Visit(child,runs,style.C);
            // An access code vouches only for the document UYAP served; a copy edited in Word and written as UDF again must not carry it on.
            if(furniture&&string.Concat(runs.Where(r=>r.Kind=='t').Select(r=>r.Text)).TrimStart().StartsWith(UyapAccessLead.TrimEnd(),StringComparison.Ordinal))return;
            if(runs.FindIndex(r=>r.Kind=='f') is >=0 and var page)
            {
                string Text(IEnumerable<Segment> part)=>string.Concat(part.Where(r=>r.Kind is 't' or '\t').Select(r=>r.Text)).Trim();var count=runs.FindIndex(page+1,r=>r.Kind=='f'&&r.Text!="PAGE");
                if(!pageNumbers){pageNumbers=true;pageLabel=Text(runs.Take(page));if(runs[page].Text=="PAGE"&&count>page&&Text(runs.Skip(page+1).Take(count-page-1)) is {Length:>0} separator)pageSeparator=separator;}
                runs.Clear();
            }
            var tabs=TabSet(paragraph,runs.Any(r=>r.Kind=='\t'));
            // A paragraph ends at "\n" in UYAP, so a line break starts another paragraph with the same layout, and a page break
            // is an empty paragraph wrapped in <page-break>, as udf-cli writes it. Headers, footers and cells keep no page breaks.
            var breaks=!furniture&&cells==0;if(breaks&&paragraph.GetValueOrDefault("#pageBreakBefore")=="true")PageBreak(xml);
            // Word keeps a paragraph mark that directly follows a page break on the page before, so it starts no line of its own.
            var part=new List<Segment>();var written=false;var broken=false;
            foreach(var run in runs)
            {
                if(run.Kind=='n'){Emit(xml,paragraph,part,mark,tabs);part.Clear();written=true;broken=false;}
                else if(run.Kind=='p'){if(!breaks)continue;if(part.Count>0||written){Emit(xml,paragraph,part,mark,tabs);part.Clear();}PageBreak(xml);written=false;broken=true;}
                else{part.Add(run);broken=false;}
            }
            if(!broken)Emit(xml,paragraph,part,mark,tabs);
        }
        private void PageBreak(StringBuilder xml){xml.Append("<page-break>");Emit(xml,new(StringComparer.Ordinal),[],new(StringComparer.Ordinal),"");xml.Append("</page-break>\n");}
        private void Visit(XElement item,List<Segment> runs,Dictionary<string,string> format)
        {
            switch(item.Name.LocalName)
            {
                case "r":Run(item,runs,format);break;
                case "fldSimple" when furniture&&PageField(Attr(item,"instr")) is {} page:runs.Add(new('f',page,null));break;
                // As in ReadDocx: links, content controls, insertions and simple fields hold ordinary runs; deletions and moved-away text do not.
                case "hyperlink" or "sdt" or "sdtContent" or "ins" or "smartTag" or "fldSimple" or "customXml" or "moveTo" or "dir" or "bdo":foreach(var child in item.Elements())Visit(child,runs,format);break;
            }
        }
        private static string? PageField(string? instruction)=>instruction?.Trim().Split(' ',2)[0].ToUpperInvariant() is ("PAGE" or "NUMPAGES" or "SECTIONPAGES") and var name?name:null;
        private string? Page(StringBuilder instruction)=>furniture&&instructions==0?PageField(instruction.ToString()):null;
        private void Run(XElement run,List<Segment> runs,Dictionary<string,string> paragraphFormat)
        {
            var properties=Child(run,"rPr");var format=new Dictionary<string,string>(paragraphFormat,StringComparer.Ordinal);RunStyle(Attr(Child(properties,"rStyle"),"val"),format);CharacterInto(properties,format);var hidden=format.GetValueOrDefault("#hidden")=="true";
            foreach(var child in run.Elements())
            {
                var name=child.Name.LocalName;
                // A field's instruction ends at its first separate (or at its end when it has no result). Later instruction text and separates
                // are ignored, so whether a header or footer field is a page number is decided once. A field inside another field's instruction,
                // as in IF { PAGE } = 1, only feeds that instruction: it is no page number and its result is no text. Hidden runs still open
                // and close fields, so the fields around them stay paired.
                if(name=="fldChar")
                {
                    switch(Attr(child,"fldCharType"))
                    {
                        case "begin":fields.Push((new StringBuilder(),false,null));instructions++;break;
                        case "separate":if(fields.TryPeek(out var open)&&!open.Result){fields.Pop();instructions--;var page=Page(open.Instruction);fields.Push((open.Instruction,true,page));if(page is not null)runs.Add(new('f',page,null));}break;
                        case "end":if(fields.TryPop(out var closed)&&!closed.Result){instructions--;if(Page(closed.Instruction) is {} bare)runs.Add(new('f',bare,null));}break;
                    }
                    continue;
                }
                if(name=="instrText"){if(fields.TryPeek(out var current)&&!current.Result)current.Instruction.Append(child.Value);continue;}
                // A field's instruction is not text, and a page number's cached result in a header or footer would repeat on every page.
                if(hidden||instructions>0||fields.TryPeek(out var field)&&field.Page is not null)continue;
                switch(name)
                {
                    case "t":runs.Add(new('t',child.Value,format));break;
                    case "tab" or "ptab":runs.Add(new('\t',"\t",format));break;
                    case "br":runs.Add(new(Attr(child,"type")=="page"?'p':'n',"",format));break;
                    case "cr":runs.Add(new('n',"",format));break;
                    case "noBreakHyphen":runs.Add(new('t',"\u2011",format));break;
                    case "drawing" or "pict" or "object":Picture(child,runs);break;
                }
            }
        }
        // UYAP's Java editor shows PNG, JPEG, GIF and BMP. TIFF, WebP, EMF and WMF pictures are stored as PNG, within the limits of image files
        // and scaled down where the PNG would outgrow PictureLimit; anything else, or a picture that cannot be read, is left out.
        private void Picture(XElement element,List<Segment> runs)
        {
            var id=element.Descendants().Select(e=>Attr(e,"embed")??(e.Name.LocalName=="imagedata"?Attr(e,"id"):null)).FirstOrDefault(v=>v is not null);
            if(id is null||!relations.TryGetValue(id,out var part)||zip.GetEntry(part) is not {} entry||entry.Length>PictureLimit)return;
            byte[] data;using(var source=entry.Open())using(var bytes=new MemoryStream()){var buffer=new byte[81920];int count;while((count=source.Read(buffer))>0){if(bytes.Length+count>PictureLimit)return;bytes.Write(buffer,0,count);}data=bytes.ToArray();}
            if(PictureFormat(data) is not {} format)return;
            // The size is read before a conversion, which may scale the pixels down.
            double width=0,height=0;try{var size=PhotoCodec.Size(data,format);width=size.Width;height=size.Height;if(ImageExtension(data) is null)data=PhotoCodec.Png(data,format,PictureLimit);}catch(Exception error)when(error is ImageMagick.MagickException or ArgumentException or InvalidDataException){return;}if(data.Length>PictureLimit)return;
            // Sizes are pixels at 96 DPI (EMU/9525), as udf-cli and the UDF toolkit write them.
            var extent=element.Descendants().FirstOrDefault(e=>e.Name.LocalName=="extent");if(Num(extent,"cx") is >0 and var cx&&Num(extent,"cy") is >0 and var cy){width=cx/9525;height=cy/9525;}
            if(width>0&&height>0)runs.Add(new('i',"",null,data,width,height));
        }
        // Word's hanging indent is UYAP's Hanging: the first line starts LeftIndent from the margin and the lines after it Hanging further in.
        private static (double Left,double First,double Hanging) Indents(Dictionary<string,string> paragraph)
        {
            double Get(string key)=>double.TryParse(paragraph.GetValueOrDefault(key),NumberStyles.Float,CultureInfo.InvariantCulture,out var value)?value:0;
            var left=Get("LeftIndent");var first=Get("FirstLineIndent");if(first>=0)return(left,first,0);var hanging=Math.Min(-first,Math.Max(left,0));return(left-hanging,0,hanging);
        }
        private string TabSet(Dictionary<string,string> paragraph,bool tabbed)
        {
            // Word measures stops from the margin and UYAP from where the first line starts (LeftIndent).
            var (left,_,hanging)=Indents(paragraph);
            var stops=paragraph.Where(p=>p.Key.StartsWith("#tab:",StringComparison.Ordinal)&&p.Value!="clear").Select(p=>(Position:int.Parse(p.Key[5..],NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture)/20d,p.Value)).OrderBy(s=>s.Position).ToList();
            // After its last stop Word tabs to every default interval, and to the hanging indent where the wrapped lines begin (a list
            // label's tab ends there); without stops UYAP would move on 72 points, so a paragraph with tabs lists them.
            if(tabbed)
            {
                for(var position=(Math.Floor((stops.Count>0?stops[^1].Position:0)/defaultTab)+1)*defaultTab;position<=textWidth&&stops.Count<64;position+=defaultTab)stops.Add((position,"0:0"));
                if(hanging>0&&!stops.Any(s=>Math.Abs(s.Position-left-hanging)<.5))stops.Add((left+hanging,"0:0"));
            }
            return string.Join(',',stops.Where(s=>s.Position-left>0).OrderBy(s=>s.Position).Select(s=>Points(s.Position-left)+":"+s.Value));
        }
        private void Emit(StringBuilder xml,Dictionary<string,string> paragraph,List<Segment> runs,Dictionary<string,string> mark,string tabs)
        {
            xml.Append("<paragraph").Append(ParagraphAttributes(paragraph,tabs)).Append('>');var start=text.Length;string? pendingAttributes=null;var pending=new StringBuilder();
            void Flush(){if(pending.Length==0)return;xml.Append("<content").Append(pendingAttributes).Append(" startOffset=\"").Append(text.Length).Append("\" length=\"").Append(pending.Length).Append("\" />");text.Append(pending);pending.Clear();}
            foreach(var run in runs)
            {
                if(run.Kind=='i'){Flush();xml.Append("<image imageData=\"").Append(Convert.ToBase64String(run.Image!)).Append("\" startOffset=\"").Append(text.Length).Append("\" length=\"1\" width=\"").Append(run.Width.ToString("0.0",CultureInfo.InvariantCulture)).Append("\" height=\"").Append(run.Height.ToString("0.0",CultureInfo.InvariantCulture)).Append("\" />");text.Append('\uFFFC');continue;}
                var attributes=RunAttributes(run.Format!);
                if(run.Kind=='\t'){Flush();xml.Append("<tab").Append(attributes).Append(" startOffset=\"").Append(text.Length).Append("\" length=\"1\" />");text.Append('\t');continue;}
                // Word splits runs freely; neighbours with the same formatting become one UYAP run. XML normalizes "\r", which would move every later offset.
                var value=XmlSafe(run.Text).Replace("\r","").Replace("\n","");if(value.Length==0)continue;if(attributes!=pendingAttributes)Flush();pendingAttributes=attributes;pending.Append(value);
            }
            Flush();
            // The paragraph's "\n" carries its mark's formatting; an empty paragraph keeps a zero-width space before it.
            var empty=text.Length==start;if(empty)text.Append('\u200B');
            xml.Append("<content").Append(RunAttributes(mark)).Append(" startOffset=\"").Append(empty?start:text.Length).Append("\" length=\"").Append(empty?2:1).Append("\" />");text.Append('\n');xml.Append("</paragraph>\n");
        }
        private static string ParagraphAttributes(Dictionary<string,string> paragraph,string tabs)
        {
            var (left,first,hanging)=Indents(paragraph);
            var xml=new StringBuilder(" Alignment=\"").Append(paragraph.GetValueOrDefault("Alignment")??"0").Append("\" LeftIndent=\"").Append(Points(left)).Append("\" RightIndent=\"").Append(paragraph.GetValueOrDefault("RightIndent")??"0.0").Append('"');
            if(first!=0)xml.Append(" FirstLineIndent=\"").Append(Points(first)).Append('"');if(hanging>0)xml.Append(" Hanging=\"").Append(Points(hanging)).Append('"');
            foreach(var key in new[]{"LineSpacing","SpaceAbove","SpaceBelow"})if(paragraph.GetValueOrDefault(key) is {} value&&double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&n!=0)xml.Append(' ').Append(key).Append("=\"").Append(value).Append('"');
            if(tabs.Length>0)xml.Append(" TabSet=\"").Append(tabs).Append('"');return xml.Append(" resolver=\"hvl-default\"").ToString();
        }
        // UYAP reads sizes as whole points. LibreOffice writes its metric-compatible fonts, and UYAP machines have the Microsoft originals.
        private static string RunAttributes(Dictionary<string,string> format)
        {
            var family=format.GetValueOrDefault("family") switch{null or ""=>"Times New Roman","Liberation Serif" or "Tinos" or "Thorndale"=>"Times New Roman","Liberation Sans" or "Arimo" or "Albany"=>"Arial","Liberation Mono" or "Cousine" or "Cumberland"=>"Courier New","Carlito"=>"Calibri","Caladea"=>"Cambria",var font=>font};
            var size=Math.Clamp((int)Math.Round(double.TryParse(format.GetValueOrDefault("size"),NumberStyles.Float,CultureInfo.InvariantCulture,out var points)?points:10,MidpointRounding.AwayFromZero),1,400);
            var xml=new StringBuilder(" family=\"").Append(SecurityElement.Escape(family)).Append("\" size=\"").Append(size).Append('"');
            foreach(var key in new[]{"bold","italic","underline","strikethrough","subscript","superscript"})if(format.GetValueOrDefault(key)=="true")xml.Append(' ').Append(key).Append("=\"true\"");
            if(format.GetValueOrDefault("foreground") is {} foreground&&foreground!="-16777216")xml.Append(" foreground=\"").Append(foreground).Append('"');if(format.GetValueOrDefault("background") is {} background&&background!="-1")xml.Append(" background=\"").Append(background).Append('"');
            return xml.ToString();
        }
        private void Table(XElement table,StringBuilder xml,int depth)
        {
            var rows=Blocks(table).Where(e=>e.Name.LocalName=="tr").ToList();if(rows.Count==0)return;
            var grid=Child(table,"tblGrid")?.Elements().Where(e=>e.Name.LocalName=="gridCol").Select(c=>(Num(c,"w")??0)/20).ToList()??[];
            static int Span(XElement cell)=>(int)Math.Clamp(Num(Child(Child(cell,"tcPr"),"gridSpan"),"val")??1,1,63);static int Before(XElement row)=>(int)Math.Clamp(Num(Child(Child(row,"trPr"),"gridBefore"),"val")??0,0,63);
            var columns=Math.Clamp(Math.Max(grid.Count,rows.Max(r=>Before(r)+Blocks(r).Where(c=>c.Name.LocalName=="tc").Sum(Span))),1,63);
            var fallback=grid.Where(w=>w>0).DefaultIfEmpty(453.5/columns).Average();var widths=Enumerable.Range(0,columns).Select(i=>i<grid.Count&&grid[i]>0?grid[i]:fallback).ToArray();
            xml.Append("<table tableName=\"Sabit\" columnCount=\"").Append(columns).Append("\" columnSpans=\"").Append(string.Join(',',widths.Select(w=>Math.Round(w).ToString(CultureInfo.InvariantCulture)))).Append("\" border=\"").Append(Bordered(table)?"borderCell":"borderNone").Append("\">");
            // UYAP rows fill every column: a merged-across cell keeps colspan, and the cells continuing a vertical merge stay empty.
            void Empty(){xml.Append("<cell>");Emit(xml,new(StringComparer.Ordinal),[],new(StringComparer.Ordinal),"");xml.Append("</cell>");}
            for(var r=0;r<rows.Count;r++)
            {
                xml.Append("<row rowName=\"row").Append(r+1).Append("\" rowType=\"dataRow\">");var column=0;for(var n=Before(rows[r]);n>0&&column<columns;n--,column++)Empty();
                foreach(var cell in Blocks(rows[r]).Where(c=>c.Name.LocalName=="tc"))
                {
                    if(column>=columns)break;var span=Math.Min(Span(cell),columns-column);var properties=Child(cell,"tcPr");xml.Append("<cell");if(span>1)xml.Append(" colspan=\"").Append(span).Append('"');
                    if(Attr(Child(properties,"vAlign"),"val") is ("center" or "bottom") and var align)xml.Append(" align=\"").Append(align=="center"?"vcenter":"bottom").Append('"');
                    if(Argb(Attr(Child(properties,"shd"),"fill")) is {} fill&&fill!="-1")xml.Append(" fillColor=\"").Append(fill).Append('"');xml.Append('>');
                    var continued=Child(properties,"vMerge") is {} merge&&Attr(merge,"val") is null or "continue";var before=xml.Length;
                    if(!continued){cells++;try{Content(cell,xml,depth+1);}finally{cells--;}}if(xml.Length==before)Emit(xml,new(StringComparer.Ordinal),[],new(StringComparer.Ordinal),"");
                    xml.Append("</cell>");column+=span;
                }
                for(;column<columns;column++)Empty();xml.Append("</row>");
            }
            xml.Append("</table>\n");
        }
        // Word draws no borders unless the table or its style sets them, as "Table Grid" does.
        private bool Bordered(XElement table)
        {
            static bool Visible(XElement borders)=>borders.Elements().Any(b=>Attr(b,"val") is not (null or "nil" or "none"));var properties=Child(table,"tblPr");if(Child(properties,"tblBorders") is {} direct)return Visible(direct);
            var id=Attr(Child(properties,"tblStyle"),"val");for(var depth=0;id is not null&&depth<16&&styles.TryGetValue(id,out var style);depth++){if(Child(Child(style,"tblPr"),"tblBorders") is {} borders)return Visible(borders);id=Attr(Child(style,"basedOn"),"val");}
            return false;
        }
        private (string Text,char Suffix,Dictionary<string,string> Format,XElement? Properties)? Label(string numId,int level,Dictionary<string,string> mark)
        {
            var num=numbering?.Elements().FirstOrDefault(e=>e.Name.LocalName=="num"&&Attr(e,"numId")==numId);var abstractId=Attr(Child(num,"abstractNumId"),"val");
            var definition=numbering?.Elements().FirstOrDefault(e=>e.Name.LocalName=="abstractNum"&&Attr(e,"abstractNumId")==abstractId);if(num is null||abstractId is null||definition is null)return null;
            var overrides=num.Elements().Where(e=>e.Name.LocalName=="lvlOverride").ToList();XElement? Override(int i)=>overrides.FirstOrDefault(o=>Attr(o,"ilvl")==i.ToString(CultureInfo.InvariantCulture));
            XElement? Level(int i)=>Child(Override(i),"lvl")??definition.Elements().FirstOrDefault(e=>e.Name.LocalName=="lvl"&&Attr(e,"ilvl")==i.ToString(CultureInfo.InvariantCulture));
            int Start(int i)=>(int)Math.Clamp(Num(Child(Override(i),"startOverride"),"val")??Num(Child(Level(i),"start"),"val")??1,0,100000);
            // Word continues one list across the instances of an abstract list; an instance with a start override restarts it when first used.
            if(!counters.TryGetValue(abstractId,out var counts))counters[abstractId]=counts=Enumerable.Repeat(int.MinValue,9).ToArray();
            if(restarted.Add(numId)&&overrides.Any(o=>Child(o,"startOverride") is not null))Array.Fill(counts,int.MinValue);
            for(var i=0;i<level;i++)if(counts[i]==int.MinValue)counts[i]=Start(i);counts[level]=counts[level]==int.MinValue?Start(level):counts[level]+1;for(var i=level+1;i<9;i++)counts[i]=int.MinValue;
            var definitionLevel=Level(level);var format=Attr(Child(definitionLevel,"numFmt"),"val")??"decimal";var legal=Child(definitionLevel,"isLgl") is not null;var pattern=Attr(Child(definitionLevel,"lvlText"),"val")??"";
            var label=format=="bullet"?Bullet(pattern):Regex.Replace(pattern,"%([1-9])",m=>{var i=m.Groups[1].Value[0]-'1';var f=i==level?format:Attr(Child(Level(i),"numFmt"),"val")??"decimal";return Count(counts[i]==int.MinValue?Start(i):counts[i],legal&&f!="decimalZero"?"decimal":f);});
            var labelFormat=new Dictionary<string,string>(mark,StringComparer.Ordinal);CharacterInto(Child(definitionLevel,"rPr"),labelFormat);labelFormat.Remove("#hidden");
            // Bullets drawn from Symbol or Wingdings are Unicode characters now, shown in the paragraph's own font.
            if(format=="bullet"){if(mark.TryGetValue("family",out var font))labelFormat["family"]=font;else labelFormat.Remove("family");}
            return(label,Attr(Child(definitionLevel,"suff"),"val") switch{"space"=>' ',"nothing"=>'\0',_=>'\t'},labelFormat,Child(definitionLevel,"pPr"));
        }
        private static string Count(int n,string format)=>format switch
        {
            "lowerLetter"=>Letters(n,'a'),"upperLetter"=>Letters(n,'A'),"lowerRoman"=>Roman(n).ToLowerInvariant(),"upperRoman"=>Roman(n),"decimalZero"=>n.ToString("00",CultureInfo.InvariantCulture),"none"=>"",_=>n.ToString(CultureInfo.InvariantCulture)
        };
        private static string Letters(int n,char first)=>n<1?n.ToString(CultureInfo.InvariantCulture):new string((char)(first+(n-1)%26),(n-1)/26+1);
        private static string Roman(int n)
        {
            if(n is <1 or >3999)return n.ToString(CultureInfo.InvariantCulture);var result=new StringBuilder();
            foreach(var (value,symbol) in new[]{(1000,"M"),(900,"CM"),(500,"D"),(400,"CD"),(100,"C"),(90,"XC"),(50,"L"),(40,"XL"),(10,"X"),(9,"IX"),(5,"V"),(4,"IV"),(1,"I")})while(n>=value){result.Append(symbol);n-=value;}
            return result.ToString();
        }
        // Word draws most bullets from Symbol and Wingdings' private-use characters, which a UYAP font does not have.
        private static string Bullet(string text)=>text switch{"" or "\uF0B7"=>"\u2022","\uF0A7"=>"\u25AA","\uF0A8"=>"\u25A1","\uF0D8" or "\uF0E8"=>"\u25BA","\uF0FC"=>"\u221A","o"=>"\u25E6",_=>text.Any(c=>c is >='\uF000' and <='\uF0FF')?"\u2022":text};
    }
}
