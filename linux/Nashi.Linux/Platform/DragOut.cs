using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
namespace Nashi.Linux;

/// Lets a control start a drag of real files into other apps (file managers, mail, chat, editors):
/// press and move a few pixels. Used for fresh exports in the toast and in Library.
public static class DragOut
{
    public static void Attach(Control control,Func<IReadOnlyList<string>> paths)
    {
        Avalonia.Point? start=null;PointerPressedEventArgs? pressed=null;
        control.Cursor=new Cursor(StandardCursorType.DragMove);
        control.PointerPressed+=(_,e)=>{if(e.GetCurrentPoint(control).Properties.IsLeftButtonPressed){start=e.GetPosition(control);pressed=e;}};
        control.PointerReleased+=(_,_)=>start=null;
        control.PointerMoved+=async(_,e)=>
        {
            if(start is not {} origin||pressed is null||!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)return;
            var delta=e.GetPosition(control)-origin;if(Math.Abs(delta.X)+Math.Abs(delta.Y)<6)return;start=null;
            if(TopLevel.GetTopLevel(control) is not {} top)return;
            var data=new DataTransfer();
            foreach(var path in paths().Where(p=>File.Exists(p)||Directory.Exists(p)))
            {
                IStorageItem? item=Directory.Exists(path)?await top.StorageProvider.TryGetFolderFromPathAsync(path):await top.StorageProvider.TryGetFileFromPathAsync(path);
                if(item is null)continue;var entry=DataTransferItem.CreateFile(item);entry.Set(ClipboardFiles.UriList,System.Text.Encoding.UTF8.GetBytes(new Uri(path).AbsoluteUri+"\r\n"));data.Add(entry);
            }
            // The drag belongs to the press that started it.
            if(data.Items.Count>0)await DragDrop.DoDragDropAsync(pressed,data,DragDropEffects.Copy);
        };
    }
}
