using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nashi.Core;
namespace Nashi.Windows;

// Native regression checks for Home mouse input, selected-file removal and conversion.
internal static class InteractionChecks
{
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Children(child))yield return nested;}
    }
    private static Button? Caption(WheelWindow wheel,string title)=>Children(wheel).OfType<Button>()
        .FirstOrDefault(button=>Children(button).OfType<TextBlock>().Any(text=>text.Text==L10n.Uppercase(title)));
    private static void ControlKey(Window window,Key key)
    {
        window.Activate();DesktopInput.Pump();DesktopInput.Hold(Key.LeftCtrl,true);
        try{DesktopInput.Pump();DesktopInput.Key(window,key);}finally{DesktopInput.Hold(Key.LeftCtrl,false);DesktopInput.Pump();}
    }
    internal static void Run(string root,Action<bool,string> check)
    {
        var results=new List<(bool Passed,string Message)>();void Verify(bool passed,string message)=>results.Add((passed,message));
        var directory=Path.Combine(root,"interaction-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())drawing.DrawRectangle(Brushes.Gold,null,new Rect(0,0,40,30));
        var bitmap=new RenderTargetBitmap(40,30,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var paths=Enumerable.Range(1,3).Select(i=>Path.Combine(directory,$"Image {i}.jpg")).ToArray();
        foreach(var path in paths)ImageEngine.Save(bitmap,path,"jpg");
        var language=L10n.Language;var feedback=Feedback.Enabled;var library=new LibraryStore(Path.Combine(directory,"library"));
        var window=new MainWindow(paths,library,checkForUpdates:false,offerToolSetup:false);
        try
        {
            L10n.Language="en";Feedback.Enabled=false;window.Show();DesktopInput.Pump(500);
            var open=Children(window).OfType<Button>().Single(button=>button.Content as string=="Open wheel");
            DesktopInput.Click(open);
            var wheel=Application.Current.Windows.OfType<WheelWindow>().FirstOrDefault(candidate=>candidate.Owner==window);
            Verify(wheel is not null,"Clicking Home's Open wheel button did not open the wheel");
            if(wheel is not null)
            {
                var canvas=Children(wheel).OfType<Canvas>().First();var petals=canvas.Children.OfType<System.Windows.Shapes.Path>().ToArray();var labels=canvas.Children.OfType<Button>().Take(petals.Length).ToArray();
                Verify(labels.Length==petals.Length&&labels.Select((label,index)=>{var bounds=label.TransformToAncestor(canvas).TransformBounds(new Rect(0,0,label.ActualWidth,label.ActualHeight));return new[]{bounds.TopLeft,bounds.TopRight,bounds.BottomLeft,bounds.BottomRight}.All(petals[index].Data.FillContains);}).All(fits=>fits),"Adding Convert placed a wheel label outside its tool sector");
                wheel.Close();
            }
            Children(window).OfType<Button>().Single(button=>button.Content as string=="Home").Focus();
            DesktopInput.Key(window,Key.F,control:true);
            Verify(window.LibraryVisible&&window.SearchField.IsKeyboardFocused,$"Ctrl+F did not focus Library search (Library visible: {window.LibraryVisible}; search visible: {window.SearchField.IsVisible}; focus: {Keyboard.FocusedElement?.GetType().Name})");
            if(window.LibraryVisible)
            {
                window.SearchField.Text="existing";window.SearchField.Select(0,0);DesktopInput.Key(window,Key.F,control:true);
                Verify(window.SearchField.IsKeyboardFocused&&window.SearchField.SelectionLength==window.SearchField.Text.Length,"Ctrl+F in Library did not select the current search text");
            }
            if(window.LibraryVisible)DesktopInput.Click(Children(window).OfType<Button>().Single(button=>button.Content as string=="Home"));

            var list=Children(window).OfType<ListBox>().Single(item=>System.Windows.Automation.AutomationProperties.GetAutomationId(item)=="SelectedFiles");
            list.Focus();ControlKey(window,Key.A);Verify(list.SelectedItems.Count==paths.Length,"Ctrl+A did not select all Home files");
            list.SelectedItems.Clear();list.SelectedItems.Add(paths[0]);list.SelectedItems.Add(paths[2]);
            DesktopInput.Click(Children(window).OfType<Button>().Single(button=>button.Content as string=="Library"));
            Verify(window.LibraryVisible,"Clicking Library did not open the Library page");DesktopInput.Click(window.SearchField);
            Verify(window.SearchField.IsKeyboardFocused,"Library search did not receive mouse focus");
            window.SearchField.Text="abc";window.SearchField.Select(1,1);DesktopInput.Key(window,Key.Delete);
            Verify(window.SearchField.Text=="ac"&&window.SelectedFiles.Count==3,"Delete in Library search changed the selected files or failed to edit text");
            DesktopInput.Click(Children(window).OfType<Button>().Single(button=>button.Content as string=="Home"));
            Verify(!window.LibraryVisible,"Clicking Home did not return to the Home page");
            list.Focus();DesktopInput.Key(window,Key.Delete);
            Verify(window.SelectedFiles.SequenceEqual(new[]{paths[1]}),"Delete did not remove only the selected Home files");
            Verify(paths.All(File.Exists),"Removing Home selections deleted original files");

            ControlKey(window,Key.Space);
            wheel=Application.Current.Windows.OfType<WheelWindow>().FirstOrDefault(candidate=>candidate.Owner==window);
            Verify(wheel is not null,"Ctrl+Space did not open the selected files wheel");
            if(wheel is not null)
            {
                var convert=Caption(wheel,"Convert");Verify(convert is not null,"Ctrl+Space wheel has no Convert choice");
                if(convert is not null)
                {
                    DesktopInput.Key(wheel,Key.Enter);
                    var keyboardFormats=Application.Current.Windows.OfType<WheelWindow>().FirstOrDefault(candidate=>candidate.Owner==window);
                    Verify(keyboardFormats is not null&&Caption(keyboardFormats,"PDF") is not null,"Enter did not open conversion formats from the keyboard wheel");
                    keyboardFormats?.Close();ControlKey(window,Key.Space);
                    wheel=Application.Current.Windows.OfType<WheelWindow>().First(candidate=>candidate.Owner==window);
                    convert=Caption(wheel,"Convert")!;
                    DesktopInput.Click(convert);
                    var formats=Application.Current.Windows.OfType<WheelWindow>().FirstOrDefault(candidate=>candidate.Owner==window);
                    var pdf=formats is null?null:Caption(formats,"PDF");Verify(pdf is not null,"Conversion wheel has no PDF choice for JPG");
                    if(pdf is not null)
                    {
                        string? selectedFormat=null;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
                        timer.Tick+=(_,_)=>{var editor=Application.Current.Windows.OfType<ToolEditor>().FirstOrDefault();if(editor is null)return;selectedFormat=Children(editor).OfType<ComboBox>().FirstOrDefault(box=>box.Items.Contains("pdf"))?.SelectedItem as string;timer.Stop();DesktopInput.Click(Children(editor).OfType<Button>().Single(button=>button.Content is string title&&title.StartsWith("Save new copy",StringComparison.Ordinal)));};
                        timer.Start();DesktopInput.Click(pdf);timer.Stop();
                        Verify(selectedFormat=="pdf","Selecting PDF did not open the conversion editor with PDF selected");
                        var until=DateTime.UtcNow.AddSeconds(15);while(library.Entries.Count==0&&DateTime.UtcNow<until)DesktopInput.Pump(50);
                        var output=library.Entries.SingleOrDefault()?.AvailablePaths.SingleOrDefault();
                        var readable=false;if(output is not null&&Path.GetExtension(output)==".pdf"){using var document=PdfSharp.Pdf.IO.PdfReader.Open(output,PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);readable=document.PageCount==1;}
                        Verify(readable,"Saving JPG as PDF from the wheel did not produce a readable page");
                    }
                    formats?.Close();
                }
                wheel.Close();
            }
            L10n.Language="tr";ControlKey(window,Key.Space);
            var shortcuts=L10n.Text(MainWindow.ShortcutHelp);
            Verify(shortcuts.Contains("Ctrl+Space / Enter  Araçları ve dönüştürmeleri aç",StringComparison.Ordinal)&&shortcuts.Contains("Delete  Seçili dosyaları Nashi listesinden kaldır",StringComparison.Ordinal),"Turkish shortcut help lost the conversion or selected-file removal guidance");
            var turkishWheel=Application.Current.Windows.OfType<WheelWindow>().FirstOrDefault(candidate=>candidate.Owner==window);
            var turkishConvert=turkishWheel is null?null:Caption(turkishWheel,"Convert");
            Verify(turkishConvert is not null&&Children(turkishConvert).OfType<TextBlock>().Any(text=>text.Text=="DÖNÜŞTÜR"),"Turkish wheel did not show the localized conversion choice");
            if(turkishConvert is not null)
            {
                DesktopInput.Click(turkishConvert);var formats=Application.Current.Windows.OfType<WheelWindow>().FirstOrDefault(candidate=>candidate.Owner==window);
                Verify(formats is not null&&Caption(formats,"PDF") is not null,"Turkish conversion wheel did not offer PDF");formats?.Close();
            }
            turkishWheel?.Close();L10n.Language="en";
            DesktopInput.Click(Children(window).OfType<Button>().Single(button=>button.Content as string=="Clear"));
            Verify(window.SelectedFiles.Count==0&&paths.All(File.Exists),"Clicking Clear did not remove the remaining selection safely");
        }
        finally
        {
            foreach(var child in Application.Current.Windows.OfType<Window>().Where(candidate=>candidate.Owner==window).ToArray())child.Close();
            window.Close();L10n.Language=language;Feedback.Enabled=feedback;
        }
        foreach(var result in results)check(result.Passed,result.Message);
    }
}
