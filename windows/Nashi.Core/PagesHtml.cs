using System.Globalization;
using System.Text;
using System.Text.Json;
namespace Nashi.Core;
public sealed partial class OfficeDocuments
{
    private static double JNumber(JsonElement element,string name,double fallback=0)=>element.ValueKind==JsonValueKind.Object&&element.TryGetProperty(name,out var n)&&n.ValueKind==JsonValueKind.Number&&n.TryGetDouble(out var value)&&double.IsFinite(value)?value:fallback;
    private static string JString(JsonElement element,string name,string fallback="")=>element.ValueKind==JsonValueKind.Object&&element.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??fallback:fallback;
    private static bool JTrue(JsonElement element,string name)=>element.ValueKind==JsonValueKind.Object&&element.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.True;
    private static string CssNumber(double value)=>value.ToString("0.###",CultureInfo.InvariantCulture);
    private static string CharacterCss(JsonElement c,bool includeDecorations=true)
    {
        if(c.ValueKind!=JsonValueKind.Object)return "";var css=new StringBuilder();var font=JString(c,"fontName");if(font.Length>0)css.Append("font-family:'").Append(font.Replace("'","").Replace(";","").Replace("{","").Replace("}","")).Append("';");if(JNumber(c,"fontSize") is >0 and <1000)css.Append("font-size:").Append(CssNumber(JNumber(c,"fontSize"))).Append("pt;");
        if(c.TryGetProperty("bold",out var bold))css.Append(bold.ValueKind==JsonValueKind.True?"font-weight:bold;":"font-weight:normal;");if(c.TryGetProperty("italic",out var italic))css.Append(italic.ValueKind==JsonValueKind.True?"font-style:italic;":"font-style:normal;");
        var decorations=new List<string>();if(JNumber(c,"underline")>0)decorations.Add("underline");if(JNumber(c,"strikethru")>0)decorations.Add("line-through");if(includeDecorations&&decorations.Count>0)css.Append("text-decoration:").Append(string.Join(' ',decorations)).Append(';');
        foreach(var (name,property) in new[]{("fontColor","color"),("backgroundColor","background-color")})if(c.TryGetProperty(name,out var color)&&color.ValueKind==JsonValueKind.Object){css.Append(property).Append(":rgb(").Append(string.Join(',',new[]{"r","g","b"}.Select(n=>(int)Math.Round(Math.Clamp(JNumber(color,n),0,1)*255)))).Append(");");}if(JNumber(c,"superscript")!=0)css.Append(JNumber(c,"superscript")>0?"vertical-align:super;":"vertical-align:sub;");return css.ToString();
    }
    public static string ModelHtml(JsonElement model)
    {
        model.TryGetProperty("pageSetup",out var setup);var html=new StringBuilder("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><style>@page{size:");html.Append(CssNumber(JNumber(setup,"pageWidth",595.28))).Append("pt ").Append(CssNumber(JNumber(setup,"pageHeight",841.89))).Append("pt;margin:").Append(string.Join(' ',new[]{"topMargin","rightMargin","bottomMargin","leftMargin"}.Select(n=>CssNumber(JNumber(setup,n,56.69))+"pt"))).Append("}body{font:12pt Arial}table{border-collapse:collapse}td{border:1px solid #888;padding:5pt}p{white-space:pre-wrap}img{max-width:100%;height:auto}</style></head><body>");
        if(JString(model,"header") is {Length:>0} header)html.Append("<header>").Append(Escape(header)).Append("</header>");
        foreach(var block in model.GetProperty("blocks").EnumerateArray())
        {
            if(JString(block,"type")=="table")
            {
                html.Append("<table><colgroup>");if(block.TryGetProperty("columnWidths",out var widths))foreach(var width in widths.EnumerateArray())if(width.TryGetDouble(out var w)&&double.IsFinite(w)&&w>0)html.Append("<col style=\"width:").Append(CssNumber(w)).Append("pt\"/>");html.Append("</colgroup>");var merges=block.TryGetProperty("merges",out var m)?m.EnumerateArray().ToArray():[];var rowIndex=0;
                foreach(var row in block.GetProperty("rows").EnumerateArray()){html.Append("<tr>");var column=0;foreach(var cell in row.EnumerateArray())
                    {var merge=merges.FirstOrDefault(v=>JNumber(v,"row")==rowIndex&&JNumber(v,"column")==column);var hidden=merges.Any(v=>rowIndex>=JNumber(v,"row")&&rowIndex<JNumber(v,"row")+JNumber(v,"rowCount",1)&&column>=JNumber(v,"column")&&column<JNumber(v,"column")+JNumber(v,"columnCount",1)&&(rowIndex!=JNumber(v,"row")||column!=JNumber(v,"column")));if(!hidden)html.Append("<td rowspan=\"").Append((int)Math.Clamp(JNumber(merge,"rowCount",1),1,10000)).Append("\" colspan=\"").Append((int)Math.Clamp(JNumber(merge,"columnCount",1),1,1024)).Append("\">").Append(Escape(JString(cell,"text")).Replace("\n","<br/>")).Append("</td>");column++;}html.Append("</tr>");rowIndex++;}html.Append("</table>");continue;
            }
            block.TryGetProperty("paragraph",out var p);block.TryGetProperty("character",out var c);var css=CharacterCss(c,includeDecorations:false)+"text-align:"+(JNumber(p,"alignment") switch{1=>"right",2=>"center",3=>"justify",_=>"left"})+";";foreach(var (name,property) in new[]{("spaceBefore","margin-top"),("spaceAfter","margin-bottom"),("leftIndent","margin-left"),("rightIndent","margin-right"),("firstLineIndent","text-indent")})if(p.ValueKind==JsonValueKind.Object&&p.TryGetProperty(name,out _))css+=property+":"+CssNumber(JNumber(p,name))+"pt;";if(JTrue(p,"pageBreakBefore"))css+="page-break-before:always;";if(JNumber(p,"lineSpacing")>0)css+="line-height:"+CssNumber(JNumber(p,"lineSpacing"))+";";
            html.Append("<p style=\"").Append(Escape(css)).Append("\">");if(block.TryGetProperty("runs",out var runs))foreach(var run in runs.EnumerateArray())
            {
                if(JString(run,"type")=="image")
                {
                    var name=JString(run,"fileName","image.png");var mime=FeatureCatalog.Extension(name) switch{"jpg"=>"image/jpeg","webp"=>"image/webp","gif"=>"image/gif","tiff"=>"image/tiff","svg"=>"image/svg+xml",_=>"image/png"};var bytes=Convert.FromBase64String(JString(run,"data"));if(bytes.Length>20_000_000)throw new InvalidDataException("Embedded image exceeds 20 MB.");html.Append("<img alt=\"").Append(Escape(JString(run,"alt"))).Append("\" src=\"data:").Append(mime).Append(";base64,").Append(Convert.ToBase64String(bytes)).Append("\" style=\"");foreach(var dimension in new[]{"width","height"})if(JNumber(run,dimension)>0)html.Append(dimension).Append(':').Append(CssNumber(JNumber(run,dimension))).Append("pt;");html.Append("\"/>");
                }
                else
                {
                    run.TryGetProperty("character",out var character);var text="<span style=\""+Escape(CharacterCss(character))+"\">"+Escape(JString(run,"text")).Replace("\n","<br/>").Replace("\t","&#9;")+"</span>";if(Uri.TryCreate(JString(run,"link"),UriKind.Absolute,out var link)&&link.Scheme is "http" or "https" or "mailto")text="<a href=\""+Escape(link.AbsoluteUri)+"\">"+text+"</a>";html.Append(text);
                }
            }html.Append("</p>");
        }
        if(JString(model,"footer") is {Length:>0} footer)html.Append("<footer>").Append(Escape(footer)).Append("</footer>");return html.Append("</body></html>").ToString();
    }
}
