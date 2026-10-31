using System.Windows;
using Nashi.Core;
namespace Nashi.Windows;

internal static class ShellShortcutChecks
{
    internal static void Run(string root,Action<bool,string> check)
    {
        var folder=Path.Combine(root,"Shortcuts");Directory.CreateDirectory(folder);
        var target=Path.Combine(folder,"Shortcut target.txt");File.WriteAllText(target,"Nashi shortcut fixture");
        var shortcut=Path.Combine(folder,"Shortcut to target.lnk");ShellShortcut.Create(shortcut,target);
        check(File.Exists(shortcut),"Shell did not save the shortcut fixture");
        check(ShellShortcut.Resolve(shortcut)==Path.GetFullPath(target),"A dropped shortcut did not resolve to its target file");
        var folderShortcut=Path.Combine(folder,"Shortcut to folder.LNK");ShellShortcut.Create(folderShortcut,folder);
        check(ShellShortcut.Resolve(folderShortcut)==Path.GetFullPath(folder),"A folder shortcut did not resolve to its folder");
        var missing=Path.Combine(folder,"Missing.txt");File.WriteAllText(missing,"");var broken=Path.Combine(folder,"Broken shortcut.lnk");ShellShortcut.Create(broken,missing);File.Delete(missing);
        check(ShellShortcut.Resolve(broken)==broken,"A shortcut to a missing file was not kept as it is");
        var corrupt=Path.Combine(folder,"Not really a shortcut.lnk");File.WriteAllText(corrupt,"plain text");
        check(ShellShortcut.Resolve(corrupt)==corrupt,"An unreadable shortcut was not kept as it is");
        check(ShellShortcut.Resolve(target)==target,"A normal file path changed during shortcut resolution");
        var pasted=ClipboardImport.Read(new DataObject(DataFormats.FileDrop,new[]{shortcut,target}),new LibraryStore(Path.Combine(root,"Shortcut Library")));
        check(pasted.Paths.SequenceEqual(new[]{Path.GetFullPath(target)}),"Pasted or dropped shortcuts did not import their target once");
    }
}
