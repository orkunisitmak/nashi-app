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
            default:Console.Error.WriteLine("Unknown dialog: "+tool);return null;
        }
    }
}
