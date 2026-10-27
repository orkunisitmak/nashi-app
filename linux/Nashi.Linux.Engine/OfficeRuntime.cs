using Nashi.Core;
namespace Nashi.Windows;
// Kept in the source namespace so DocumentConversion.cs is compiled unchanged on both platforms.
public sealed class OfficeRuntime(string root)
{
    public string? Executable => SystemTools.Find("libreoffice") ?? SystemTools.Find("soffice");
    public string DirectoryPath => root;
}
