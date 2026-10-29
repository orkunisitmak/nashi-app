using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nashi.Core;
namespace Nashi.Windows;

// Route OLE modifier updates without moving, copying or opening the fixture files.
internal static class GlobalDragModeChecks
{
    private static DragEventArgs Event(IDataObject data,DragDropKeyStates keys,Canvas canvas,RoutedEvent routedEvent)
    {
        // WPF exposes no public constructor for drag events. Exercise its actual
        // routed handlers with the same payload, key flags and local coordinates.
        var e=(DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,[data,keys|DragDropKeyStates.LeftMouseButton,DragDropEffects.Copy,canvas,new Point(270,90)],null)!;
        e.RoutedEvent=routedEvent;return e;
    }
    internal static void Run(Action<bool,string> check)
    {
        var language=L10n.Language;var feedback=Feedback.Enabled;
        L10n.Language="en";Feedback.Enabled=false;
        try
        {
            LocalFilePrecedence(check);
            var data=new DataObject(DataFormats.FileDrop,new[]{"Synthetic archive.zip"});
            string? chosen=null;
            var wheel=new GlobalDropWheel("convert",1,(_,id)=>chosen=id,()=>null);
            try
            {
                wheel.Show();wheel.UpdateLayout();
                var canvas=(Canvas)((Viewbox)wheel.Content).Child;
                void Over(DragDropKeyStates modifiers){wheel.RaiseEvent(Event(data,modifiers,canvas,DragDrop.DragOverEvent));wheel.UpdateLayout();}
                Over(DragDropKeyStates.ShiftKey);
                check(wheel.Revealed&&canvas.Children.OfType<System.Windows.Shapes.Path>().Count()==FeatureCatalog.Formats(["Synthetic archive.zip"]).Length,"ZIP conversion did not offer its supported archive formats");
                Over(DragDropKeyStates.ControlKey);
                check(wheel.Title=="Nashi · tools"&&wheel.Revealed,"Changing Shift to Ctrl during a ZIP drag did not reveal tools");
                check(canvas.Children.OfType<System.Windows.Shapes.Path>().Count()==FeatureCatalog.Tools(["Synthetic archive.zip"]).Length,"ZIP tool choices were not rebuilt after changing modifiers");
                Over(DragDropKeyStates.AltKey);
                check(wheel.Title=="Nashi · actions"&&canvas.Children.OfType<System.Windows.Shapes.Path>().Count()==6,"Changing Ctrl to Alt did not replace tools with file actions");
                var children=canvas.Children.Cast<UIElement>().ToArray();
                Over(DragDropKeyStates.AltKey);
                check(canvas.Children.Cast<UIElement>().SequenceEqual(children),"An unchanged OLE mode rebuilt the hit-test tree");
                Over(0);
                check(wheel.Title=="Nashi · actions","Releasing modifiers lost the active drag mode");
                Over(DragDropKeyStates.ShiftKey);
                check(wheel.Title=="Nashi · convert"&&canvas.Children.OfType<System.Windows.Shapes.Path>().Count()==FeatureCatalog.Formats(["Synthetic archive.zip"]).Length,"Returning to ZIP conversion retained stale action choices");
                data=new DataObject(DataFormats.FileDrop,new[]{"Synthetic.unknown"});
                Over(DragDropKeyStates.ShiftKey);
                check(canvas.Children.OfType<System.Windows.Shapes.Path>().Count()==0,"Unsupported conversion retained stale choices");
                Over(DragDropKeyStates.AltKey);
                check(wheel.Title=="Nashi · actions"&&canvas.Children.OfType<System.Windows.Shapes.Path>().Count()==6,"An empty conversion menu prevented switching to file actions");
                check(chosen is null,"Changing drag modes executed a file action before a drop");
                data=new DataObject(DataFormats.FileDrop,new[]{"Synthetic archive.zip"});Over(DragDropKeyStates.AltKey);
                wheel.RaiseEvent(Event(data,DragDropKeyStates.ShiftKey,canvas,DragDrop.DropEvent));
                check(chosen=="convert:"+FeatureCatalog.Formats(["Synthetic archive.zip"])[0],"Drop used a stale action instead of its final modifier state");
            }
            finally{wheel.Close();}
            var settings=new Preferences{ToolsModifier="Ctrl+Alt",ConvertModifier="Shift+Alt",ActionsModifier="Ctrl+Shift"};
            var custom=new GlobalDropWheel("convert",1,(_,_)=>{},()=>null,()=>settings);
            try
            {
                custom.Show();custom.UpdateLayout();var canvas=(Canvas)((Viewbox)custom.Content).Child;
                foreach(var (keys,expected) in new[]{(DragDropKeyStates.ControlKey|DragDropKeyStates.AltKey,"tools"),(DragDropKeyStates.ControlKey|DragDropKeyStates.ShiftKey,"actions"),(DragDropKeyStates.ShiftKey|DragDropKeyStates.AltKey,"convert")})
                {
                    custom.RaiseEvent(Event(data,keys,canvas,DragDrop.DragOverEvent));
                    check(custom.Title=="Nashi · "+expected,"Custom modifier chord failed to select "+expected);
                }
                custom.SetMode("actions");
                check(custom.Title=="Nashi · actions"&&custom.State.Contains("selection=,",StringComparison.Ordinal),"Polling a new mode outside the wheel retained a stale selection");
            }
            finally{custom.Close();}
            var empty=new GlobalDropWheel("convert",1,(_,_)=>{},()=>null);
            try
            {
                empty.Show();empty.UpdateLayout();var canvas=(Canvas)((Viewbox)empty.Content).Child;var unknown=new DataObject(DataFormats.FileDrop,new[]{"Synthetic.unknown"});
                empty.RaiseEvent(Event(unknown,DragDropKeyStates.ShiftKey,canvas,DragDrop.DragOverEvent));
                check(!empty.Revealed,"An initially unsupported conversion revealed an empty wheel");
                empty.RaiseEvent(Event(unknown,DragDropKeyStates.AltKey,canvas,DragDrop.DragOverEvent));
                check(empty.Revealed&&empty.Title=="Nashi · actions","Switching an initially hidden conversion to actions did not reveal the wheel");
            }
            finally{empty.Close();}
        }
        finally{L10n.Language=language;Feedback.Enabled=feedback;}
    }
    private static void LocalFilePrecedence(Action<bool,string> check)
    {
        const string url="https://www.youtube.com/watch?v=dQw4w9WgXcQ";
        var data=new DataObject(DataFormats.FileDrop,new[]{"Synthetic archive.zip"});
        data.SetData(DataFormats.UnicodeText,url);
        data.SetData(DataFormats.Html,$"<a href=\"{url}\">source</a>");
        check(DragPayload.Parse(data,url) is null,"A real file drop was classified as web media because it also carried a URL");
        string? selected=null;var hintReads=0;
        var wheel=new GlobalDropWheel("convert",1,(_,id)=>selected=id,()=>{hintReads++;return url;});
        try
        {
            wheel.Show();wheel.UpdateLayout();var canvas=(Canvas)((Viewbox)wheel.Content).Child;
            foreach(var (keys,expected,count) in new[]{(DragDropKeyStates.ShiftKey,"convert",FeatureCatalog.Formats(["Synthetic archive.zip"]).Length),(DragDropKeyStates.ControlKey,"tools",FeatureCatalog.Tools(["Synthetic archive.zip"]).Length),(DragDropKeyStates.AltKey,"actions",6)})
            {
                wheel.RaiseEvent(Event(data,keys,canvas,DragDrop.DragOverEvent));
                check(wheel.Title=="Nashi · "+expected&&canvas.Children.OfType<System.Windows.Shapes.Path>().Count()==count,"A file drop with a URL offered download choices in "+expected+" mode");
            }
            wheel.RaiseEvent(Event(data,DragDropKeyStates.AltKey,canvas,DragDrop.DropEvent));
            check(selected=="share","Dropping a local file with a URL invoked a download action");
            check(hintReads==0,"A local file drag consulted the unrelated source URL hint");
        }
        finally{wheel.Close();}
        var browser=new DataObject(DataFormats.UnicodeText,url);
        check(DragPayload.Parse(browser) is {Kind:"youtube"},"A genuine browser URL stopped offering web media actions");
        var empty=new DataObject(DataFormats.FileDrop,Array.Empty<string>());empty.SetData(DataFormats.UnicodeText,url);
        check(DragPayload.Parse(empty) is {Kind:"youtube"},"An empty file format hid a genuine browser URL");
    }
}
