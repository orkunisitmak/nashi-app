#if DEBUG
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using Nashi.Core;

namespace Nashi.Windows;

internal static class PillModeSmokeChecks
{
    internal static int Run(string output)
    {
        output=Path.GetFullPath(output);
        if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new IOException("Choose an empty pill smoke-test output folder.");
        Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,".nashi-pill-smoke"),"Nashi pill interaction smoke\n");
        var checks=new List<object>();var failures=new List<string>();void Check(string name,bool pass,string? detail=null){checks.Add(new{name,passed=pass,detail});if(!pass)failures.Add(name+(detail is null?"":": "+detail));}
        var data=Path.Combine(output,"isolated-data");Directory.CreateDirectory(data);var library=new LibraryStore(data);var preferences=new PreferencesStore(data);
        preferences.Save(new Preferences{Language="en",PillModeEnabled=true,PillVisibilityMode="shortcut",PillHotkey="Ctrl+Alt+N",PillOnboardingCompleted=false,AutomaticUpdateChecks=false,KeepInTray=false,Feedback=false,Animations=false});
        var window=new MainWindow(library:library,checkForUpdates:false,offerToolSetup:false);
        try
        {
            DesktopInput.MovePointer(new Point(SystemParameters.PrimaryScreenWidth/2,2));
            window.Show();DesktopInput.Pump(450);
            var onboarding=window.IsVisible&&window.UIAuditIslandExpanded&&window.UIAuditOnboardingVisible&&VisualDescendants(window).OfType<Button>().Any(button=>button.Content as string=="Got it");
            Check("first-launch-onboarding-opens-expanded",onboarding,$"visible={window.IsVisible}, expanded={window.UIAuditIslandExpanded}, banner={window.UIAuditOnboardingVisible}");
            if(onboarding){Capture(window,Path.Combine(output,"pill-first-launch.png"));DesktopInput.Click(VisualDescendants(window).OfType<Button>().Single(button=>button.Content as string=="Got it"));}
            Check("onboarding-dismisses-and-persists",!window.UIAuditOnboardingVisible&&new PreferencesStore(data).Value.PillOnboardingCompleted,"Got it did not dismiss and persist first-run tip");
            var initialHideBefore=window.UIAuditHotkeyMessages;SendShortcut(window);var initialHide=WaitFor(()=>!window.IsVisible,TimeSpan.FromMilliseconds(900));Check("onboarding-shortcut-hides-panel",initialHide,$"shortcut did not dismiss panel (messages {window.UIAuditHotkeyMessages-initialHideBefore})");
            Check("single-shaped-root",window.UIAuditIslandTab=="now"&&window.Content is System.Windows.Controls.Border,"the pill did not use its single border root and Now view");
            Check("global-hotkey-registered",window.UIAuditHotkeyRegistered,"RegisterHotKey failed or the shortcut is already in use");
            DesktopInput.Pump(500);Check("top-edge-does-not-open",!window.IsVisible,"moving the pointer to the screen edge revealed the pill");
            var pickerFolder=Path.Combine(output,"pdf-picker-fixture");Directory.CreateDirectory(pickerFolder);Directory.CreateDirectory(Path.Combine(pickerFolder,"Synthetic folder"));var pixels=new byte[16*16*4];for(var y=0;y<16;y++)for(var x=0;x<16;x++){var offset=(y*16+x)*4;pixels[offset]=(byte)(x*16);pixels[offset+1]=(byte)(y*16);pixels[offset+2]=(byte)(255-x*16);pixels[offset+3]=255;}var fixtureImage=BitmapSource.Create(16,16,96,96,PixelFormats.Bgra32,null,pixels,16*4);fixtureImage.Freeze();var fixturePdf=Path.Combine(pickerFolder,"Synthetic edit.pdf");ImageEngine.Save(fixtureImage,fixturePdf,"pdf");File.WriteAllBytes(Path.Combine(pickerFolder,"Synthetic image.png"),[137,80,78,71]);File.WriteAllText(Path.Combine(pickerFolder,"Synthetic archive.zip"),"fixture");
            var twoPagePdf=DocumentEngine.Pdf("mergePDFs",[fixturePdf,fixturePdf],pickerFolder).Single();var splitEditor=new ToolEditor(window,"splitPDF","Split PDF",twoPagePdf,inputs:[twoPagePdf]);var splitDefault=splitEditor.SplitSummaryForTest.Contains("2 pages · 2 PDF",StringComparison.Ordinal);splitEditor.FieldForTest("Pages per file").Text="2";var groupedSummary=splitEditor.SplitSummaryForTest.Contains("2 pages · 1 PDF",StringComparison.Ordinal);splitEditor.FieldForTest("Pages per file").Text="1";splitEditor.FieldForTest("Page ranges").Text="2";var rangeSummary=splitEditor.SplitSummaryForTest.Contains("1 pages · 1 PDF",StringComparison.Ordinal);var accepted=splitEditor.AcceptForTest();var splitOutputs=DocumentEngine.Pdf("splitPDF",[twoPagePdf],pickerFolder,pagesPerFile:1,pageRanges:"2");using var splitOutputDoc=PdfSharp.Pdf.IO.PdfReader.Open(splitOutputs.Single(),PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);using var originalPdfDoc=PdfSharp.Pdf.IO.PdfReader.Open(twoPagePdf,PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);Check("split-pdf-preview-and-export",splitDefault&&groupedSummary&&rangeSummary&&accepted&&splitOutputs.Count==1&&splitOutputDoc.PageCount==1&&originalPdfDoc.PageCount==2,$"default={splitEditor.SplitSummaryForTest}, grouped={groupedSummary}, ranges={rangeSummary}, outputPages={splitOutputDoc.PageCount}, originalPages={originalPdfDoc.PageCount}");splitEditor.Close();
            var pdfPicker=new NashiFilePickerWindow(window,pickerFolder,["pdf"],allowMultiple:false,purpose:"pdf");pdfPicker.Show();DesktopInput.Pump(250);var pdfPickerScoped=pdfPicker.UIAuditHasVisibleFile("Synthetic edit.pdf")&&!pdfPicker.UIAuditHasVisibleFile("Synthetic image.png")&&!pdfPicker.UIAuditHasVisibleFile("Synthetic archive.zip")&&pdfPicker.UIAuditSingleSelection;Check("pdf-tool-picker-is-scoped",pdfPickerScoped,"the PDF edit picker must show PDFs only and use single selection");if(pdfPickerScoped)Capture(pdfPicker,Path.Combine(output,"pdf-tool-picker.png"));pdfPicker.Close();
            var splitPicker=new NashiFilePickerWindow(window,pickerFolder,["pdf"],allowMultiple:true,purpose:"pdf");splitPicker.Show();DesktopInput.Pump(180);var splitPickerScoped=splitPicker.UIAuditHasVisibleFile("Synthetic edit.pdf")&&!splitPicker.UIAuditHasVisibleFile("Synthetic image.png")&&!splitPicker.UIAuditHasVisibleFile("Synthetic archive.zip")&&!splitPicker.UIAuditSingleSelection;Check("split-pdf-picker-allows-only-pdf-files",splitPickerScoped,"split PDF must accept multiple PDFs and filter other file types");splitPicker.Close();
            var generalPicker=new NashiFilePickerWindow(window,pickerFolder);generalPicker.Show();DesktopInput.Pump(250);var filesFirst=generalPicker.UIAuditFirstItemIsFile&&generalPicker.UIAuditHasVisibleFile("Synthetic image.png")&&generalPicker.UIAuditHasVisibleFile("Synthetic archive.zip");Check("general-picker-shows-files-before-folders",filesFirst,"the first visible entries should include files even when the folder contains directories");if(filesFirst)Capture(generalPicker,Path.Combine(output,"general-file-picker.png"));generalPicker.Close();
            var downloadsOrder=NashiFilePickerWindow.UIAuditSortPaths([Path.Combine(pickerFolder,"Synthetic archive.zip"),Path.Combine(pickerFolder,"Synthetic image.png"),Path.Combine(pickerFolder,"Synthetic clip.mp4")],[Path.Combine(pickerFolder,"Synthetic folder")],downloads:true);var mediaFirst=downloadsOrder.SequenceEqual(["Synthetic clip.mp4","Synthetic image.png","Synthetic archive.zip","Synthetic folder"],StringComparer.CurrentCultureIgnoreCase);Check("downloads-picker-prioritizes-images-and-videos",mediaFirst,"Downloads should list image and video files first, then other files, then folders: "+string.Join(", ",downloadsOrder));
            var organizerTask=PdfOrganizer.CreateAsync(window,fixturePdf);var organizerUntil=DateTime.UtcNow.AddSeconds(10);while(!organizerTask.IsCompleted&&DateTime.UtcNow<organizerUntil)DesktopInput.Pump(35);if(!organizerTask.IsCompleted)throw new TimeoutException("Synthetic PDF organizer did not render in time.");var organizer=organizerTask.GetAwaiter().GetResult();organizer.Show();DesktopInput.Pump(180);var pageThumbnail=VisualDescendants(organizer).OfType<System.Windows.Controls.Image>().FirstOrDefault(image=>image.Source is BitmapSource);var thumbnails=pageThumbnail is null?0:1;var colorful=false;if(pageThumbnail?.Source is BitmapSource thumbnail){var thumbnailPixels=new byte[thumbnail.PixelWidth*thumbnail.PixelHeight*4];new FormatConvertedBitmap(thumbnail,PixelFormats.Bgra32,null,0).CopyPixels(thumbnailPixels,thumbnail.PixelWidth*4,0);colorful=Enumerable.Range(0,thumbnailPixels.Length/4).Any(index=>thumbnailPixels[index*4]!=thumbnailPixels[index*4+1]||thumbnailPixels[index*4+1]!=thumbnailPixels[index*4+2]);}var organizerVisual=organizer.Pages.Length==1&&thumbnails==1&&colorful;Check("pdf-organizer-renders-page-thumbnail",organizerVisual,$"pages={organizer.Pages.Length}, thumbnails={thumbnails}, colorful={colorful}");if(organizerVisual)Capture(organizer,Path.Combine(output,"pdf-organizer.png"));organizer.Close();
            var before=window.UIAuditHotkeyMessages;SendShortcut(window);
            var shortcutShows=WaitFor(()=>window.IsVisible&&window.UIAuditIslandExpanded,TimeSpan.FromMilliseconds(900));
            Check("registered-shortcut-opens",shortcutShows,$"Ctrl+Alt+N did not open the panel (WM_HOTKEY messages: {window.UIAuditHotkeyMessages-before}, visible={window.IsVisible}, expanded={window.UIAuditIslandExpanded})");
            Capture(window,Path.Combine(output,"pill-expanded-now.png"));
            window.UIAuditPresentIsland(false);DesktopInput.Pump(120);Capture(window,Path.Combine(output,"pill-collapsed.png"));
            DesktopInput.MovePointer(window.PointToScreen(new Point(window.ActualWidth/2,window.ActualHeight/2)));DesktopInput.Pump(500);
            Check("hover-does-not-expand",window.IsVisible&&!window.UIAuditIslandExpanded,"pointer hover expanded the shortcut-controlled pill");
            window.UIAuditPresentIsland(true);DesktopInput.Pump(100);
            DesktopInput.MovePointer(new Point(SystemParameters.PrimaryScreenWidth-40,SystemParameters.PrimaryScreenHeight-90));DesktopInput.Pump(700);
            Check("pointer-leave-does-not-close",window.IsVisible&&window.UIAuditIslandExpanded,"the shortcut-controlled panel closed after pointer leave");
            before=window.UIAuditHotkeyMessages;SendShortcut(window);
            var shortcutHides=WaitFor(()=>!window.IsVisible,TimeSpan.FromMilliseconds(900));
            Check("registered-shortcut-closes",shortcutHides,$"Ctrl+Alt+N did not close the panel (WM_HOTKEY messages: {window.UIAuditHotkeyMessages-before}, visible={window.IsVisible})");
            before=window.UIAuditHotkeyMessages;SendShortcut(window);
            var shortcutReopens=WaitFor(()=>window.IsVisible&&window.UIAuditIslandExpanded,TimeSpan.FromMilliseconds(900));
            Check("registered-shortcut-reopens",shortcutReopens,$"Ctrl+Alt+N did not reopen the panel (WM_HOTKEY messages: {window.UIAuditHotkeyMessages-before}, visible={window.IsVisible}, expanded={window.UIAuditIslandExpanded})");
            Capture(window,Path.Combine(output,"pill-hotkey-expanded.png"));

            Check("settings-gear-is-visible",window.UIAuditSettingsDockVisible,"expanded pill does not expose Settings directly");
            Check("media-empty-state-is-explanatory",window.UIAuditClickDock("media")&&WaitFor(()=>window.UIAuditMediaEmptyMessage?.Contains("Start a track",StringComparison.Ordinal)==true,TimeSpan.FromMilliseconds(300)),"media empty state does not tell users to start a track in another app");
            foreach(var tab in new[]{"now","tray","media","notes","todo","tools"})
            {
                var clicked=window.UIAuditClickDock(tab);
                var targetTab=tab=="tools"?"actions":tab;
                var opened=clicked&&WaitFor(()=>window.UIAuditIslandTab==targetTab,TimeSpan.FromMilliseconds(900));
                Check("dock-opens-"+tab,opened,$"mouse click did not select the {tab} module");
                if(opened)Capture(window,Path.Combine(output,"module-"+tab+".png"));
            }
            foreach(var (action,tab) in new[]{("Open Pocket","pocket"),("Open calendar","calendar"),("Open teleprompter","prompter"),("Open mirror","mirror")})
            {
                var clicked=window.UIAuditClickAction(action);
                var opened=clicked&&WaitFor(()=>window.UIAuditIslandTab==tab,TimeSpan.FromMilliseconds(900));
                Check("search-opens-"+tab,opened,$"mouse selection of {action} did not open {tab}");
                if(opened)Capture(window,Path.Combine(output,"module-"+tab+".png"));
            }
            window.UIAuditClickDock("notes");
            if(window.UIAuditNoteEditor is {} note)
            {
                DesktopInput.Click(note);DesktopInput.TypeText("Synthetic smoke note");DesktopInput.Pump(120);
                var saved=File.Exists(window.UIAuditWidgetStatePath)&&File.ReadAllText(window.UIAuditWidgetStatePath).Contains("Synthetic smoke note",StringComparison.Ordinal);
                Check("notes-save-locally",saved,"typed note was not saved in the isolated Nashi data directory");
                if(saved)Capture(window,Path.Combine(output,"module-notes-filled.png"));
            }
            else Check("notes-save-locally",false,"notes editor did not appear");
            window.UIAuditClickDock("todo");
            if(window.UIAuditTodoEditor is {} todo)
            {
                DesktopInput.Click(todo);DesktopInput.TypeText("Synthetic smoke task");DesktopInput.Key(window,Key.Enter);DesktopInput.Pump(120);
                var saved=File.Exists(window.UIAuditWidgetStatePath)&&File.ReadAllText(window.UIAuditWidgetStatePath).Contains("Synthetic smoke task",StringComparison.Ordinal);
                Check("todo-saves-locally",saved,"typed task was not saved in the isolated Nashi data directory");
                if(saved)Capture(window,Path.Combine(output,"module-todo-filled.png"));
            }
            else Check("todo-saves-locally",false,"to-do entry did not appear");
            Check("search-indexes-note",window.UIAuditActionSearchContains("Synthetic smoke note"),"the local action search did not find the saved note");
            Check("search-indexes-todo",window.UIAuditActionSearchContains("Synthetic smoke task"),"the local action search did not find the saved task");
            Check("search-enter-opens-note",window.UIAuditSubmitActionSearch("Synthetic smoke note","notes"),"Enter did not activate the first matching local search result");
            var timerStarted=window.UIAuditClickAction("Start 25-minute timer")&&WaitFor(()=>window.UIAuditTimerRunning&&window.UIAuditIslandTab=="timer"&&window.IsVisible&&window.UIAuditIslandExpanded,TimeSpan.FromMilliseconds(900));
            Check("timer-starts-from-search",timerStarted,$"running={window.UIAuditTimerRunning}, tab={window.UIAuditIslandTab}, visible={window.IsVisible}, expanded={window.UIAuditIslandExpanded}");
            if(timerStarted)Capture(window,Path.Combine(output,"module-timer.png"));
            SettingsWindow? modeSettings=null;var closeSettings=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(120)};closeSettings.Tick+=(_,_)=>{modeSettings=Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();if(modeSettings is null)return;closeSettings.Stop();var section=VisualDescendants(modeSettings).OfType<ListBox>().FirstOrDefault();if(section is not null)section.SelectedItem="Behavior";modeSettings.UpdateLayout();var enabled=VisualDescendants(modeSettings).OfType<CheckBox>().FirstOrDefault(box=>box.Content as string=="Enable compact pill mode");if(enabled is not null)enabled.IsChecked=false;modeSettings.Close();};closeSettings.Start();var settingsOpened=window.UIAuditClickDock("settings");var restored=Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(candidate=>!ReferenceEquals(candidate,window));var modeSwitched=settingsOpened&&modeSettings is not null&&restored is {UIAuditPillModeEnabled:false,IsVisible:true};Check("settings-can-switch-pill-mode-after-startup",modeSwitched,"changing pill mode in Settings failed to switch the live window safely");closeSettings.Stop();restored?.Close();
        }
        finally{if(window.IsVisible)window.Close();}
        File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new{checks,failures},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(new{checks=checks.Count,failed=failures.Count,output}));
        return failures.Count==0?0:1;
    }

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++){var child=VisualTreeHelper.GetChild(root,index);yield return child;foreach(var descendant in VisualDescendants(child))yield return descendant;}
    }
    private static bool WaitFor(Func<bool> condition,TimeSpan timeout)
    {
        var until=DateTime.UtcNow+timeout;while(DateTime.UtcNow<until){DesktopInput.Pump(35);if(condition())return true;}return condition();
    }
    private static void SendShortcut(MainWindow window)
    {
        DesktopInput.Key(window,Key.N,control:true,alt:true);
    }
    private static void Capture(FrameworkElement element,string path)
    {
        element.UpdateLayout();var width=(int)Math.Ceiling(element.ActualWidth);var height=(int)Math.Ceiling(element.ActualHeight);
        if(width<1||height<1)throw new InvalidOperationException("A pill screenshot had an empty layout.");
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
}
#endif
