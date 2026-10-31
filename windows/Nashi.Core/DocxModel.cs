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
                void Visit(XElement item,string? link=null)
                {
                    if(item.Name.LocalName=="r")
                    {
                        var rp=Child(item,"rPr");var runBase=Attr(Child(rp,"rStyle"),"val") is {} sid?Resolve(sid,includeDefaults:false).C:new();var formatting=Merge(baseStyle.C,runBase,Character(rp));
                        foreach(var child in item.Elements())
                        {
                            if(child.Name.LocalName is "t" or "tab" or "br" or "cr")runs.Add(new{text=child.Name.LocalName=="t"?child.Value:child.Name.LocalName=="tab"?"\t":"\n",character=formatting,link});
                            if(child.Name.LocalName is "drawing" or "pict")
                            {
                                var id=child.Descendants().Select(e=>Attr(e,"embed")??(e.Name.LocalName=="imagedata"?Attr(e,"id"):null)).FirstOrDefault(v=>v is not null);if(id is null||!rels.TryGetValue(id,out var target)||Local(target) is not {} local)continue;var entry=zip.GetEntry(local);if(entry is null||entry.Length>20_000_000)throw new InvalidDataException("An embedded image is missing or too large.");using var source=entry.Open();using var bytes=new MemoryStream();source.CopyTo(bytes);var extent=child.Descendants().FirstOrDefault(e=>e.Name.LocalName=="extent");runs.Add(new{type="image",data=Convert.ToBase64String(bytes.ToArray()),fileName=Path.GetFileName(local),width=Num(extent,"cx")/12700,height=Num(extent,"cy")/12700,alt=Attr(child.Descendants().FirstOrDefault(e=>e.Name.LocalName=="docPr"),"descr")??""});
                            }
                        }
                    }
                    else if(item.Name.LocalName=="hyperlink"){var destination=rels.GetValueOrDefault(Attr(item,"id")??"");foreach(var child in item.Elements())Visit(child,destination);}
                    else if(item.Name.LocalName is "sdt" or "sdtContent" or "ins" or "smartTag")foreach(var child in item.Elements())Visit(child,link);
                }
                foreach(var child in element.Elements())Visit(child);return new{type="paragraph",runs,character=runs.Count==0?Merge(baseStyle.C,Character(Child(props,"rPr"))):baseStyle.C.Where(p=>p.Key!="backgroundColor").ToDictionary(p=>p.Key,p=>p.Value),paragraph=Merge(baseStyle.P,ParagraphStyle(props))};
            }
            if(element.Name.LocalName=="tbl")
            {
                var rows=new List<List<object>>();var merges=new List<Dictionary<string,int>>();var vertical=new Dictionary<int,(int Row,int Span)>();var rowIndex=0;
                foreach(var row in element.Elements().Where(e=>e.Name.LocalName=="tr"))
                {
                    var cells=new List<object>();var column=0;foreach(var cell in row.Elements().Where(e=>e.Name.LocalName=="tc"))
                    {
                        var props=Child(cell,"tcPr");var raw=Num(Child(props,"gridSpan"),"val");var span=raw is null?1:checked((int)raw.Value);if(span is <1 or >1024||column>1024-span)throw new InvalidDataException("A table cell span is invalid or exceeds 1,024 columns.");var text=string.Join("\n",Blocks(cell).Where(p=>p.Name.LocalName=="p").Select(p=>string.Concat(p.Descendants().Where(e=>e.Name.LocalName=="t").Select(t=>t.Value))));cells.Add(new{text});for(var n=1;n<span;n++)cells.Add(new{text=""});
                        var vm=Child(props,"vMerge");if(vm is not null){if(Attr(vm,"val")=="restart")vertical[column]=(rowIndex,span);else if(vertical.TryGetValue(column,out var origin)){var merged=merges.FirstOrDefault(m=>m["row"]==origin.Row&&m["column"]==column);if(merged is null){merged=new(){{"row",origin.Row},{"column",column},{"rowCount",1},{"columnCount",origin.Span}};merges.Add(merged);}merged["rowCount"]=rowIndex-origin.Row+1;}}else{vertical.Remove(column);if(span>1)merges.Add(new(){{"row",rowIndex},{"column",column},{"rowCount",1},{"columnCount",span}});}column+=span;
                    }rows.Add(cells);rowIndex++;
                }
                return new{type="table",rows,merges,columnWidths=Child(element,"tblGrid")?.Elements().Select(c=>(Num(c,"w")??2400)/20).ToArray()??[]};
            }return null;
        }
        var blocks=Blocks(body).Select(Block).Where(b=>b is not null).ToArray();var model=new Dictionary<string,object?>{{"blocks",blocks}};var section=Child(body,"sectPr");if(section is not null)
        {
            var setup=new Dictionary<string,object?>();foreach(var (name,key) in new[]{("w","pageWidth"),("h","pageHeight")})if(Num(Child(section,"pgSz"),name) is {} size)setup[key]=size/20;foreach(var (name,key) in new[]{("left","leftMargin"),("right","rightMargin"),("top","topMargin"),("bottom","bottomMargin")})if(Num(Child(section,"pgMar"),name) is {} size)setup[key]=size/20;model["pageSetup"]=setup;
            foreach(var (name,key) in new[]{("headerReference","header"),("footerReference","footer")})if(Attr(Child(section,name),"id") is {} id&&rels.TryGetValue(id,out var target)&&Local(target) is {} local&&zip.GetEntry(local) is not null)model[key]=string.Concat(Xml(zip,local).Descendants().Where(e=>e.Name.LocalName=="t").Select(e=>e.Value));
        }return model;
    }
}
