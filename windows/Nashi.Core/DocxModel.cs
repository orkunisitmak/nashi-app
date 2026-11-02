using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
namespace Nashi.Core;
public sealed partial class OfficeDocuments
{
    private static XElement? Child(XElement? e,string n)=>e?.Elements().FirstOrDefault(c=>c.Name.LocalName==n);
    private static IEnumerable<XElement> Blocks(XElement e)=>e.Elements().SelectMany(c=>c.Name.LocalName is "sdt" or "sdtContent" or "customXml"?Blocks(c):[c]);
    private static string? Attr(XElement? e,string n)=>e?.Attributes().FirstOrDefault(a=>a.Name.LocalName==n)?.Value;
    private static double? Num(XElement? e,string n)=>double.TryParse(Attr(e,n),NumberStyles.Float,CultureInfo.InvariantCulture,out var x)&&double.IsFinite(x)?x:null;
    // Word keeps a URL's #section in w:anchor, not in the relationship target. Like the Mac (Foundation's urlFragmentAllowed),
    // the anchor's UTF-8 bytes are percent-encoded except letters, digits and !$&'()*+,-./:;=?@_~.
    private static string Fragment(string anchor)=>string.Concat(System.Text.Encoding.UTF8.GetBytes(anchor).Select(b=>b<128&&(char.IsAsciiLetterOrDigit((char)b)||"!$&'()*+,-./:;=?@_~".Contains((char)b))?((char)b).ToString():"%"+b.ToString("X2",CultureInfo.InvariantCulture)));
    private static Dictionary<string,object?> Merge(params Dictionary<string,object?>[] dictionaries){var result=new Dictionary<string,object?>();foreach(var d in dictionaries)foreach(var p in d)result[p.Key]=p.Value;return result;}
    private static object? FontColor(string? value)=>value?.Length==6&&int.TryParse(value,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var n)?new{r=((n>>16)&255)/255d,g=((n>>8)&255)/255d,b=(n&255)/255d,space="srgb"}:null;
    private static object? HighlightColor(string? value)=>FontColor(value switch
    {
        "black"=>"000000","blue"=>"0000FF","cyan"=>"00FFFF","green"=>"00FF00","magenta"=>"FF00FF","red"=>"FF0000","yellow"=>"FFFF00","white"=>"FFFFFF",
        "darkBlue"=>"000080","darkCyan"=>"008080","darkGreen"=>"008000","darkMagenta"=>"800080","darkRed"=>"800000","darkYellow"=>"808000","darkGray"=>"808080","lightGray"=>"C0C0C0",_=>value
    });
    private static Dictionary<string,object?> Character(XElement? e)
    {
        var d=new Dictionary<string,object?>();if(e is null)return d;foreach(var (name,key) in new[]{("b","bold"),("i","italic")})if(Child(e,name) is {} p)d[key]=Attr(p,"val") is not ("0" or "false" or "off");
        if(Num(Child(e,"sz"),"val") is {} size)d["fontSize"]=size/2;if((Attr(Child(e,"rFonts"),"ascii")??Attr(Child(e,"rFonts"),"hAnsi")) is {} font)d["fontName"]=font;
        if(FontColor(Attr(Child(e,"color"),"val")) is {} color)d["fontColor"]=color;if(Child(e,"highlight") is {} highlight)d["backgroundColor"]=HighlightColor(Attr(highlight,"val"));
        if(Child(e,"u") is {} u)d["underline"]=Attr(u,"val") switch{"none"=>0,"double"=>2,_=>1};if(Child(e,"strike") is {} strike)d["strikethru"]=Attr(strike,"val")=="0"?0:1;
        if(Attr(Child(e,"vertAlign"),"val") is {} align)d["superscript"]=align=="superscript"?1:align=="subscript"?-1:0;if(Attr(Child(e,"lang"),"val") is {} lang)d["language"]=lang;return d;
    }
    private static Dictionary<string,object?> ParagraphStyle(XElement? e)
    {
        var d=new Dictionary<string,object?>();if(e is null)return d;if(Attr(Child(e,"jc"),"val") is {} align)d["alignment"]=align switch{"right" or "end"=>1,"center"=>2,"both" or "distribute"=>3,_=>0};
        var space=Child(e,"spacing");if(Num(space,"before") is {} before)d["spaceBefore"]=before/20;if(Num(space,"after") is {} after)d["spaceAfter"]=after/20;if(Num(space,"line") is {} line&&Attr(space,"lineRule") is null or "auto")d["lineSpacing"]=line/240;
        var indent=Child(e,"ind");foreach(var (name,key) in new[]{("left","leftIndent"),("right","rightIndent"),("firstLine","firstLineIndent")})if(Num(indent,name) is {} value)d[key]=value/20;if(Num(indent,"hanging") is {} hanging)d["firstLineIndent"]=-hanging/20;
        foreach(var (name,key) in new[]{("keepNext","keepWithNext"),("keepLines","keepLinesTogether"),("pageBreakBefore","pageBreakBefore"),("widowControl","widowControl")})if(Child(e,name) is {} p)d[key]=Attr(p,"val") is not ("0" or "false" or "off");return d;
    }
    public static object ReadDocx(string path)
    {
        using var zip=ZipFile.OpenRead(path);if(zip.Entries.Count>10000||zip.Entries.Sum(e=>e.Length)>512_000_000||zip.Entries.GroupBy(e=>e.FullName,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))throw new InvalidDataException("The document package is too large or contains duplicate entries.");
        var doc=Xml(zip,"word/document.xml");var body=Child(doc.Root,"body")??throw new InvalidDataException("The document has no editable body.");var styles=zip.GetEntry("word/styles.xml") is not null?Xml(zip,"word/styles.xml"):null;var defaults=Child(styles?.Root,"docDefaults");var dc=Character(Child(Child(defaults,"rPrDefault"),"rPr"));var dp=ParagraphStyle(Child(Child(defaults,"pPrDefault"),"pPr"));
        var styleMap=styles?.Root?.Elements().Where(e=>e.Name.LocalName=="style").ToDictionary(e=>Attr(e,"styleId")??throw new InvalidDataException("Invalid style identifier."))??[];
        var defaultStyle=styleMap.Values.LastOrDefault(e=>(Attr(e,"type")??"paragraph")=="paragraph"&&Attr(e,"default") is "1" or "true" or "on");
        var rels=zip.GetEntry("word/_rels/document.xml.rels") is not null?Xml(zip,"word/_rels/document.xml.rels").Root!.Elements().ToDictionary(e=>Attr(e,"Id")??"",e=>Attr(e,"Target")??""):[];
        string? Local(string relative){if(Uri.TryCreate(relative,UriKind.Absolute,out _))return null;var stack=new List<string>{"word"};foreach(var p in relative.Replace('\\','/').Split('/')){if(p is "" or ".")continue;if(p==".."){if(stack.Count==0)return null;stack.RemoveAt(stack.Count-1);}else stack.Add(p);}return string.Join('/',stack);}
        (Dictionary<string,object?> C,Dictionary<string,object?> P) Resolve(string? id,int depth=0,bool includeDefaults=true){if(depth>=16||id is null||!styleMap.TryGetValue(id,out var style))return includeDefaults?(dc,dp):(new(),new());var parent=Resolve(Attr(Child(style,"basedOn"),"val"),depth+1,includeDefaults);return(Merge(parent.C,Character(Child(style,"rPr"))),Merge(parent.P,ParagraphStyle(Child(style,"pPr"))));}
        object? Block(XElement element)
        {
            if(element.Name.LocalName=="p")
            {
                var props=Child(element,"pPr");var baseStyle=Resolve(Attr(Child(props,"pStyle"),"val")??Attr(defaultStyle,"styleId"));var runs=new List<object>();
                // Open complex fields (fldChar begin … end), as on the Mac: a HYPERLINK field links its result once, at its first separate,
                // and each entry keeps the innermost link in effect, so a run reads it in constant time however deeply fields nest.
                var fields=new List<(System.Text.StringBuilder Instruction,string? Link,bool Result)>();
                void Visit(XElement item,string? link=null)
                {
                    if(item.Name.LocalName=="r")
                    {
                        var rp=Child(item,"rPr");var runBase=Attr(Child(rp,"rStyle"),"val") is {} sid?Resolve(sid,includeDefaults:false).C:new();var formatting=Merge(baseStyle.C,runBase,Character(rp));
                        foreach(var child in item.Elements())
                        {
                            if(child.Name.LocalName=="fldChar")switch(Attr(child,"fldCharType")){case "begin":fields.Add((new(),fields.Count>0?fields[^1].Link:null,false));break;case "separate" when fields.Count>0&&!fields[^1].Result:fields[^1]=fields[^1] with{Link=HyperlinkField(fields[^1].Instruction.ToString())??fields[^1].Link,Result=true};break;case "end" when fields.Count>0:fields.RemoveAt(fields.Count-1);break;}
                            else if(child.Name.LocalName=="instrText"&&fields.Count>0&&!fields[^1].Result)fields[^1].Instruction.Append(child.Value);
                            if(child.Name.LocalName=="br"&&Attr(child,"type")=="page")runs.Add(PageBreak);else if(child.Name.LocalName is "t" or "tab" or "br" or "cr")runs.Add(new{text=child.Name.LocalName=="t"?child.Value:child.Name.LocalName=="tab"?"\t":"\n",character=formatting,link=link??(fields.Count>0?fields[^1].Link:null)});
                            if(child.Name.LocalName is "drawing" or "pict")
                            {
                                var id=child.Descendants().Select(e=>Attr(e,"embed")??(e.Name.LocalName=="imagedata"?Attr(e,"id"):null)).FirstOrDefault(v=>v is not null);if(id is null||!rels.TryGetValue(id,out var target)||Local(target) is not {} local)continue;var entry=zip.GetEntry(local);if(entry is null||entry.Length>20_000_000)throw new InvalidDataException("An embedded image is missing or too large.");using var source=entry.Open();using var bytes=new MemoryStream();source.CopyTo(bytes);var extent=child.Descendants().FirstOrDefault(e=>e.Name.LocalName=="extent");runs.Add(new{type="image",data=Convert.ToBase64String(bytes.ToArray()),fileName=Path.GetFileName(local),width=Num(extent,"cx")/12700,height=Num(extent,"cy")/12700,alt=Attr(child.Descendants().FirstOrDefault(e=>e.Name.LocalName=="docPr"),"descr")??""});
                            }
                            if(child.Name.LocalName=="noBreakHyphen")runs.Add(new{text="\u2011",character=formatting,link});
                        }
                    }
                    else if(item.Name.LocalName=="hyperlink"){var destination=rels.GetValueOrDefault(Attr(item,"id")??"");if(destination is not null&&!destination.Contains('#')&&Attr(item,"anchor") is {Length:>0} anchor)destination+="#"+Fragment(anchor);foreach(var child in item.Elements())Visit(child,destination);}
                    else if(item.Name.LocalName is "sdt" or "sdtContent" or "ins" or "smartTag")foreach(var child in item.Elements())Visit(child,link);
                    // As on the Mac, simple fields (page numbers, dates), custom XML, tracked moves and direction wrappers hold ordinary runs too.
                    else if(item.Name.LocalName is "fldSimple" or "customXml" or "moveTo" or "dir" or "bdo")foreach(var child in item.Elements())Visit(child,link);
                }
                foreach(var child in element.Elements())Visit(child);var character=runs.Count==0?Merge(baseStyle.C,Character(Child(props,"rPr"))):baseStyle.C.Where(p=>p.Key!="backgroundColor").ToDictionary(p=>p.Key,p=>p.Value);var paragraph=Merge(baseStyle.P,ParagraphStyle(props));
                if(!runs.Contains(PageBreak))return new{type="paragraph",runs,character,paragraph};
                // Pages has no inline page break: as on the Mac, the paragraph ends at a manual page break and each later part starts a new page.
                var parts=new List<List<object>>{new()};foreach(var run in runs)if(run==PageBreak)parts.Add([]);else parts[^1].Add(run);
                return parts.Select((part,index)=>(object)new{type="paragraph",runs=part,character,paragraph=index==0?paragraph:Merge(paragraph,new(){{"pageBreakBefore",true}})}).ToArray();
            }
            if(element.Name.LocalName=="tbl")
            {
                var rows=new List<List<object>>();var merges=new List<Dictionary<string,int>>();var vertical=new Dictionary<int,(int Row,int Span)>();var rowIndex=0;
                // As on the Mac, a vertical merge only survives rows that continue it in the same grid column with the same width,
                // w:gridBefore leaves leading grid columns empty, and a legacy w:hMerge continuation joins the restart cell to its left.
                foreach(var row in element.Elements().Where(e=>e.Name.LocalName=="tr"))
                {
                    var cells=new List<object>();var column=0;var continued=new Dictionary<int,(int Row,int Span)>();int? anchor=null;
                    if(int.TryParse(Attr(Child(Child(row,"trPr"),"gridBefore"),"val"),NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var skipped)&&skipped>0){if(skipped>1024)throw new InvalidDataException("A table cell span is invalid or exceeds 1,024 columns.");column=skipped;for(var n=0;n<skipped;n++)cells.Add(new{text=""});}
                    foreach(var cell in row.Elements().Where(e=>e.Name.LocalName=="tc"))
                    {
                        var props=Child(cell,"tcPr");var raw=Num(Child(props,"gridSpan"),"val");var span=raw is null?1:checked((int)raw.Value);if(span is <1 or >1024||column>1024-span)throw new InvalidDataException("A table cell span is invalid or exceeds 1,024 columns.");var text=string.Join("\n",Blocks(cell).Where(p=>p.Name.LocalName=="p").Select(p=>string.Concat(p.Descendants().Where(e=>e.Name.LocalName=="t").Select(t=>t.Value))));cells.Add(new{text});for(var n=1;n<span;n++)cells.Add(new{text=""});
                        var vm=Child(props,"vMerge");var hm=vm is null?Child(props,"hMerge"):null;var extends=false;var joins=false;
                        if(vm is not null){if(Attr(vm,"val")=="restart")continued[column]=(rowIndex,span);else if(vertical.TryGetValue(column,out var origin)&&origin.Span==span){continued[column]=origin;extends=true;var merged=merges.FirstOrDefault(m=>m["row"]==origin.Row&&m["column"]==column);if(merged is null){merged=new(){{"row",origin.Row},{"column",column},{"rowCount",1},{"columnCount",origin.Span}};merges.Add(merged);}merged["rowCount"]=rowIndex-origin.Row+1;}}
                        if(hm is null)anchor=null;else if(Attr(hm,"val")=="restart")anchor=column;else if(anchor is {} start){joins=true;var merged=merges.FirstOrDefault(m=>m["row"]==rowIndex&&m["column"]==start);if(merged is null){merged=new(){{"row",rowIndex},{"column",start},{"rowCount",1},{"columnCount",1}};merges.Add(merged);}merged["columnCount"]=column+span-start;}
                        // A spanning cell keeps its width whether or not a continuation follows; a continuation only grows that entry.
                        if(span>1&&!extends&&!joins)merges.Add(new(){{"row",rowIndex},{"column",column},{"rowCount",1},{"columnCount",span}});column+=span;
                    }vertical=continued;rows.Add(cells);rowIndex++;
                }
                return new{type="table",rows,merges,columnWidths=Child(element,"tblGrid")?.Elements().Select(c=>(Num(c,"w")??2400)/20).ToArray()??[]};
            }return null;
        }
        var blocks=Blocks(body).Select(Block).OfType<object>().SelectMany(b=>b as object[]??[b]).ToArray();var model=new Dictionary<string,object?>{{"blocks",blocks}};var section=Child(body,"sectPr");if(section is not null)
        {
            var setup=new Dictionary<string,object?>();foreach(var (name,key) in new[]{("w","pageWidth"),("h","pageHeight")})if(Num(Child(section,"pgSz"),name) is {} size)setup[key]=size/20;foreach(var (name,key) in new[]{("left","leftMargin"),("right","rightMargin"),("top","topMargin"),("bottom","bottomMargin")})if(Num(Child(section,"pgMar"),name) is {} size)setup[key]=size/20;model["pageSetup"]=setup;
            foreach(var (name,key) in new[]{("headerReference","header"),("footerReference","footer")})if(Attr(Child(section,name),"id") is {} id&&rels.TryGetValue(id,out var target)&&Local(target) is {} local&&zip.GetEntry(local) is not null)model[key]=string.Concat(Xml(zip,local).Descendants().Where(e=>e.Name.LocalName=="t").Select(e=>e.Value));
        }return model;
    }
    // Stands for a w:br w:type="page" in a paragraph's runs until the paragraph is split there.
    private static readonly object PageBreak=new();
    /// The external target of a `HYPERLINK "url" [\o "tip"] [\l "bookmark"]` field instruction, as the Mac reads it;
    /// bookmark-only links have no URL in the Pages model.
    private static string? HyperlinkField(string instruction)
    {
        var tokens=new List<(string Value,bool Quoted)>();
        for(var i=0;i<instruction.Length;)
        {
            if(char.IsWhiteSpace(instruction[i])){i++;continue;}
            if(instruction[i]=='"'){var close=instruction.IndexOf('"',i+1);if(close<0)close=instruction.Length;tokens.Add((instruction[(i+1)..close],true));i=close+1;}
            else{var end=i;while(end<instruction.Length&&!char.IsWhiteSpace(instruction[end]))end++;tokens.Add((instruction[i..end],false));i=end;}
        }
        if(tokens.Count==0||tokens[0].Quoted||!tokens[0].Value.Equals("HYPERLINK",StringComparison.OrdinalIgnoreCase))return null;
        for(var n=1;n<tokens.Count;)
        {
            if(!tokens[n].Quoted&&tokens[n].Value.StartsWith('\\')){n+=tokens[n].Value.ToLowerInvariant() is "\\l" or "\\o" or "\\t"?2:1;continue;}
            return tokens[n].Value.Length==0?null:tokens[n].Value;
        }
        return null;
    }
}
