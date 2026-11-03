using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;

internal sealed record ClipboardResult(string[] Paths,string? Link,string Label);
internal static class ClipboardImport
{
    // Walk ancestors: TextBox's OriginalSource can be its internal text view.
    internal static bool IsEditing(DependencyObject? element)
    {
        while(element is not null)
        {
            if(element is TextBoxBase or PasswordBox||element is ComboBox)return true;
            element=element is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(element):LogicalTreeHelper.GetParent(element);
        }
        return false;
    }
    internal static ClipboardResult Read(LibraryStore library)=>Read(Clipboard.GetDataObject()??throw new InvalidOperationException("Your clipboard is empty."),library);
    internal static ClipboardResult Read(IDataObject data,LibraryStore library)
    {
        if(data.GetDataPresent(DataFormats.FileDrop)&&data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            var files=ShellShortcut.Resolve(paths).Where(p=>File.Exists(p)||Directory.Exists(p)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();if(files.Length==0)throw new InvalidOperationException("Those files are no longer available.");
            return new(files,null,"Pasted files");
        }
        if(data.GetDataPresent(DataFormats.Bitmap)&&data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
        {
            if(bitmap.PixelWidth<1||bitmap.PixelHeight<1||(long)bitmap.PixelWidth*bitmap.PixelHeight>100_000_000)throw new InvalidOperationException("This image is too large to paste. Choose the image file instead.");
            var file=Destination(library,"png");ImageEngine.Save(bitmap,file,"png");return new([file],null,"Pasted image");
        }
        var text=data.GetData(DataFormats.UnicodeText) as string??data.GetData(DataFormats.Text) as string;
        if(string.IsNullOrWhiteSpace(text))throw new InvalidOperationException("Copy a file, some text, an image or a public media link first.");
        var trimmed=text.Trim();
        if(Uri.TryCreate(trimmed,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http")return new([],uri.AbsoluteUri,"Copied link");
        if(Encoding.UTF8.GetByteCount(text)>16*1024*1024)throw new InvalidOperationException("This text is too large to paste. Choose a text file instead.");
        var path=Destination(library,"txt");File.WriteAllText(path,text,new UTF8Encoding(false));return new([path],null,"Pasted text");
    }
    private static string Destination(LibraryStore library,string extension)
    {
        var folder=Path.Combine(library.DirectoryPath,"Imports");Directory.CreateDirectory(folder);
        return Path.Combine(folder,$"Clipboard {DateTime.Now:yyyy-MM-dd HH-mm-ss}-{Guid.NewGuid().ToString("N")[..6]}.{extension}");
    }
    internal static bool IsClipboardError(Exception e)=>e is ExternalException or IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException;
}
