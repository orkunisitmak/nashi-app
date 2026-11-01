using Avalonia.Controls;
namespace Nashi.Linux;

/// Editors for `--capture <png> --dialog <tool> <file>`: each opens with a small sample edit so
/// the render check shows the editor's working state, not only its empty layout.
internal static class CaptureDialog
{
    public static async Task<Window?> OpenAsync(string tool,IReadOnlyList<string> files)
    {
        switch(tool)
        {
            case "organizePDF":
                var organizer=await PdfOrganizeDialog.CreateAsync("Organize PDF",files[0]);organizer.Select(0);organizer.Rotate();organizer.Duplicate();return organizer;
            case "annotateImage":
                {
                    var editor=await AnnotateDialog.CreateAsync("Annotate Photo",files[0]);var info=new ImageMagick.MagickImageInfo(files[0]);double w=info.Width,h=info.Height;
                    editor.Add(new("marker",0,0,0,0,"#F5C400",w/200,Points:[(w*.08,h*.15),(w*.2,h*.14),(w*.35,h*.16)]));
                    editor.Add(new("rectangle",w*.1,h*.3,w*.4,h*.6,"#E5322D",w/200));
                    editor.Add(new("arrow",w*.55,h*.2,w*.42,h*.38,"#FF5200",w/200));
                    editor.Add(new("ellipse",w*.6,h*.55,w*.9,h*.85,"#2F6FEB",w/200));
                    editor.Add(new("number",w*.62,h*.25,0,0,"#2E9E4F",4,"1",w/25));
                    editor.Add(new("callout",w*.1,h*.7,w*.42,h*.86,"#111111",4,"Çok güzel",w/30));
                    editor.Add(new("magnifier",w*.75,h*.1,w*.85,h*.22,"#FFFFFF",w/250));
                    editor.Add(new("text",w*.5,h*.88,0,0,"#FFFFFF",4,"Nashi ✓ şğüöçı",w/30));
                    return editor;
                }
            case "bubble":
                {
                    var bubble=new Bubble(MainWindow.BubbleChoices,(_,_)=>Task.FromResult(""));bubble.Opened+=(_,_)=>bubble.Expand(files.ToArray());return bubble;
                }
            default:Console.Error.WriteLine("Unknown dialog: "+tool);return null;
        }
    }
}
