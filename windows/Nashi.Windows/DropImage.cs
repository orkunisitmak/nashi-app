using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ComDataObject=System.Runtime.InteropServices.ComTypes.IDataObject;
namespace Nashi.Windows;

/// WPF drop targets do not talk to the shell's drag-image helper, so the
/// Explorer thumbnail under the pointer vanishes as soon as it enters a Nashi
/// window. Forwarding the drag events keeps the shell preview visible.
internal sealed class DropImage
{
    [StructLayout(LayoutKind.Sequential)]private struct NativePoint{public int X,Y;}
    [ComImport,Guid("4657278B-411B-11d2-839A-00C04FD918D0"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropTargetHelper
    {
        void DragEnter(IntPtr window,ComDataObject data,ref NativePoint point,int effect);
        void DragLeave();
        void DragOver(ref NativePoint point,int effect);
        void Drop(ComDataObject data,ref NativePoint point,int effect);
        void Show(bool show);
    }
    [ComImport,Guid("4657278A-411B-11d2-839A-00C04FD918D0")]private class DragDropHelper{}
    private readonly Window window;private IDropTargetHelper? helper;private bool inside,leaving;
    private DropImage(Window window){this.window=window;try{helper=(IDropTargetHelper)new DragDropHelper();}catch(COMException){}}
    /// Handlers run after the window's own so they see the final effect.
    internal static void Attach(Window window)
    {
        var image=new DropImage(window);
        // A drop can close its window (the wheel does) before these run; the shell image then just ends.
        void Safe(Action<DragEventArgs> step,DragEventArgs e){try{step(e);}catch(InvalidOperationException){image.inside=false;}}
        window.AddHandler(UIElement.DragEnterEvent,new DragEventHandler((_,e)=>Safe(image.Enter,e)),true);
        window.AddHandler(UIElement.DragOverEvent,new DragEventHandler((_,e)=>Safe(image.Over,e)),true);
        window.AddHandler(UIElement.DragLeaveEvent,new DragEventHandler((_,e)=>Safe(image.Leave,e)),true);
        window.AddHandler(UIElement.DropEvent,new DragEventHandler((_,e)=>Safe(image.Drop,e)),true);
        window.Closed+=(_,_)=>{if(image.inside){image.inside=false;image.Run(h=>h.DragLeave());}};
    }
    private NativePoint Point(DragEventArgs e){var p=window.PointToScreen(e.GetPosition(window));return new(){X=(int)p.X,Y=(int)p.Y};}
    private void Run(Action<IDropTargetHelper> call){if(helper is null)return;try{call(helper);}catch(Exception error)when(error is COMException or InvalidCastException or ArgumentException){}}
    private void Enter(DragEventArgs e)
    {
        // DragEnter also bubbles up from every child element; only the first one starts the shell image.
        leaving=false;if(inside||e.Data is not ComDataObject data)return;inside=true;var point=Point(e);var handle=new WindowInteropHelper(window).Handle;Run(h=>h.DragEnter(handle,data,ref point,(int)e.Effects));
    }
    private void Over(DragEventArgs e){leaving=false;if(!inside){Enter(e);return;}var point=Point(e);Run(h=>h.DragOver(ref point,(int)e.Effects));}
    /// WPF raises DragLeave both when moving between child elements and when the
    /// drag really leaves the window, and the reported position cannot tell them
    /// apart. A child change is followed at once by DragEnter/DragOver, so the
    /// shell image is only released if nothing follows.
    private void Leave(DragEventArgs e)
    {
        if(!inside)return;leaving=true;
        window.Dispatcher.BeginInvoke(()=>{if(leaving&&inside){leaving=false;inside=false;Run(h=>h.DragLeave());}},System.Windows.Threading.DispatcherPriority.Input);
    }
    private void Drop(DragEventArgs e){leaving=false;if(!inside||e.Data is not ComDataObject data)return;inside=false;var point=Point(e);Run(h=>h.Drop(data,ref point,(int)e.Effects));}
}
