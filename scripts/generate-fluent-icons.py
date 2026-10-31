from pathlib import Path
from concurrent.futures import ThreadPoolExecutor, as_completed
import urllib.request, urllib.parse, urllib.error, xml.etree.ElementTree as ET, hashlib, json

root=Path(__file__).resolve().parent.parent
names={
"convert":["arrow_swap"],"compress":["arrow_collapse_all","arrow_minimize","arrow_down"],
"removeMetadata":["document_dismiss","document_delete","document"],
"editImage":["image_edit","image"],
"annotate":["draw_image","edit","pen"],
"frameImage":["image_multiple","image"],
"crop":["crop"],
"normalizeAudio":["sound_wave_circle","speaker_2","speaker"],
"muteVideo":["speaker_mute"],
"trim":["scissors","cut"],
"split":["split_horizontal","split","document_split_hint"],
"organizePDF":["document_multiple","document"],
"extractArchive":["folder_arrow_down","folder_zip","folder_arrow_right"],
"createPDF":["document_add"],
"merge":["document_copy","document_multiple"],
"changeVideoSpeed":["timer","gauge"],
"joinVideos":["video_clip_multiple","video_clip"],
"videoSnapshots":["camera"],
"audioToVideo":["video","video_clip"],
"audioChannels":["sound_wave_circle","speaker_2","speaker"],
"createCollage":["grid","image_multiple"],
"redact":["shield_lock","document_lock"],
"readQRCode":["barcode_scanner"],
"share":["share"],
"float":["window_multiple","window"],
"copy":["copy"],
"move":["folder_arrow_right","folder_move"],
"copyPath":["link"],
"terminal":["code"],
"resize":["arrow_maximize","resize"],
"preview":["eye"],
"reuse":["arrow_counterclockwise","arrow_reset"],
"reveal":["folder_open"],
"home":["home"],
"library":["library"],
"settings":["settings"],
"remove":["dismiss"],
"download":["arrow_download"],
"fineTune":["options","settings"],
"fileActions":["apps"],
"chevron":["chevron_down"],
"search":["search"],
"previous":["chevron_left"],
"next":["chevron_right"],
"more":["more_horizontal"],
"play":["play"],
"info":["info"],"checkmark":["checkmark"],
}
assert len(names)==len(set(names))
asset_dir=root/"Resources/FluentIcons"
asset_dir.mkdir(exist_ok=True,parents=True)
# Reproducible, audited Fluent UI System Icons revision. Update intentionally.
sha="9eb184e96268d3293462f9d9d19225b4c7b07774"
print("Upstream revision:",sha,flush=True)
unique=sorted(set(n for seq in names.values() for n in seq))
def get_one(name):
  title=urllib.parse.quote(name.replace("_"," ").title(),safe="")
  url=f"https://raw.githubusercontent.com/microsoft/fluentui-system-icons/{sha}/assets/{title}/SVG/ic_fluent_{name}_24_regular.svg"
  try:
    req=urllib.request.Request(url,headers={"User-Agent":"Nashi-fluent-migration"})
    with urllib.request.urlopen(req,timeout=16) as r: body=r.read()
    parsed=ET.fromstring(body)
    ns="{http://www.w3.org/2000/svg}"
    paths=parsed.findall(".//"+ns+"path")
    if not paths or any(x.tag not in (ns+"svg",ns+"path") for x in parsed.iter()):
      raise ValueError("Unsupported nested SVG shapes")
    parts=[]
    for node in paths:
      p=node.attrib.get("d")
      if not p: raise ValueError("Missing path data")
      parts.append({"d":p,"rule":node.attrib.get("fill-rule","nonzero")})
    if parsed.attrib.get("viewBox")!="0 0 24 24":raise ValueError("Non 24px SVG")
    return name,body,parts,url,None
  except Exception as e:return name,None,None,url,str(e)[:110]
results={}
with ThreadPoolExecutor(max_workers=12) as pool:
  for fut in as_completed([pool.submit(get_one,name) for name in unique]):
    name,body,parts,url,error=fut.result()
    results[name]=(body,parts,url,error)
for icon,attempts in names.items():
  selected=next((n for n in attempts if results[n][1]),None)
  if selected is None:
    print("MISSING ICON",icon,[(n,results[n][-1]) for n in attempts],flush=True)
resolved={key:next((n for n in seq if results[n][1]),None) for key,seq in names.items()}
if any(v is None for v in resolved.values()):
  raise SystemExit("Some Fluent symbols missing; no C# files generated")
downloaded=sorted(set(resolved.values()))
for name in downloaded:
  (asset_dir/f"{name}_24_regular.svg").write_bytes(results[name][0])
lic_req=urllib.request.Request(f"https://raw.githubusercontent.com/microsoft/fluentui-system-icons/{sha}/LICENSE",headers={"User-Agent":"Nashi-fluent-migration"})
with urllib.request.urlopen(lic_req,timeout=20) as r: (asset_dir/"LICENSE").write_bytes(r.read())
manifest={"source":"microsoft/fluentui-system-icons","license":"MIT","upstream_revision":sha,"assets":[{"icon":name,"source":results[name][2],"sha256":hashlib.sha256(results[name][0]).hexdigest()} for name in downloaded],"semantic_mappings":resolved}
(asset_dir/"SOURCE.json").write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
def escape(s):return s.replace("\\","\\\\").replace('"','\\"')
file_lines=[]
for n in downloaded:
  parts=results[n][1]
  data=", ".join('('+'"'+escape(x["d"])+'"'+", "+('true' if x["rule"]=="evenodd" else 'false')+')' for x in parts)
  file_lines.append('        ["'+n+'"] = new [] { '+data+' },')
mappings=",\n".join('        ["'+k+'"] = "'+v+'"' for k,v in sorted(resolved.items()))
code="""using System.Windows.Media;

namespace Nashi.Windows;

/// <summary>Microsoft Fluent UI System Icons (Regular 24) pinned and embedded as WPF geometry.
/// Origin and MIT license: Resources/FluentIcons/SOURCE.json and LICENSE.
/// No runtime HTTP, SVG renderer, fonts or NuGet dependencies.</summary>
internal static class FluentIcons
{
    private static readonly IReadOnlyDictionary<string,string> Ids = new Dictionary<string,string>(StringComparer.Ordinal)
    {
"""+mappings+"""
    };
    private static readonly IReadOnlyDictionary<string,(string d,bool evenOdd)[]> SourcePaths =
        new Dictionary<string,(string,bool)[]>(StringComparer.Ordinal)
    {
"""+ "\n".join(file_lines)+"""
    };
    private static readonly IReadOnlyDictionary<string,Geometry> GeometryByName = Build();
    private static IReadOnlyDictionary<string,Geometry> Build()
    {
        var output=new Dictionary<string,Geometry>(StringComparer.Ordinal);
        foreach(var (name,paths) in SourcePaths)
        {
            var group=new GeometryGroup();
            foreach(var (d,evenOdd) in paths)
            {
                var path=Geometry.Parse((evenOdd?"F0 ":"F1 ")+d);
                path.Freeze();
                group.Children.Add(path);
            }
            group.FillRule=FillRule.Nonzero;
            group.Freeze();
            output.Add(name,group);
        }
        return output;
    }
    internal static bool HasIcon(string id)=>GeometryFor(id) is not null;
    internal static Geometry? GeometryFor(string id)
    {
        if(id.StartsWith("convert:",StringComparison.Ordinal))id="convert";
        id=id switch
        {
            "cropImage" or "cropVideo"=>"crop",
            "annotateImage"=>"annotate",
            "mergePDF" or "mergePDFs"=>"merge",
            "trimAudio" or "trimVideo"=>"trim",
            "splitPDF" or "splitVideo"=>"split",
            "redactImage" or "redactVideo" or "redactAudio"=>"redact",
            _=>id
        };
        return Ids.TryGetValue(id,out var name)&&GeometryByName.TryGetValue(name,out var geometry)?geometry:null;
    }
}
"""
out=root/"windows/Nashi.Windows/FluentIcons.cs"
out.write_text(code,encoding="utf-8",newline="")
print("Generated",out,"bytes",out.stat().st_size,"assets",len(downloaded),"mapped IDs",len(resolved),flush=True)
