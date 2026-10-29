using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nashi.Core;
namespace Nashi.Windows;

internal static class LocalizationChecks
{
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        yield return root;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Children(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    internal static void Run(string directory,MainWindow owner,string image,Action<bool,string> check)
    {
        var before=L10n.Language;var prefs=new PreferencesStore(Path.Combine(directory,"Language profile"));
        var settings=new SettingsWindow(owner,prefs,()=>L10n.Language=prefs.Value.Language,()=>Task.CompletedTask);
        var editor=new ToolEditor(owner,"crop","Crop Image",image,preview:ImageEngine.Load(image));
        try
        {
            foreach(var error in new Exception[]{new UnauthorizedAccessException("fixture"),new IOException("fixture"),new System.Security.SecurityException("fixture")})
            {
                L10n.Language="tr";App.UseSavedLanguage(()=>throw error);
                check(L10n.Language=="en","An unreadable language preference hid the original startup error: "+error.GetType().Name);
            }
            App.UseSavedLanguage(()=>"tr");check(L10n.Language=="tr","Readable saved language was not used for an error dialog");
            check(NumericInput.ParseFinite("1,25",System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))==1.25&&NumericInput.ParseFinite("1.25",System.Globalization.CultureInfo.InvariantCulture)==1.25,"Localized numeric input changed valid values");
            check(NumericInput.ParseFinite("12.5",System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))==12.5,"A Turkish thousands separator turned 12.5 seconds into 125");
            foreach(var invalid in new[]{"not a number","NaN","Infinity"})
            {
                try{NumericInput.ParseFinite(invalid,System.Globalization.CultureInfo.InvariantCulture);check(false,"Invalid numeric input was accepted: "+invalid);}
                catch(ArgumentException error){check(Nashi.Core.AppLocalization.Text(error.Message,"tr")=="Geçerli bir sayı gir.","Invalid-number framework text was not converted to localized validation copy");}
            }
            L10n.Language="en";settings.Show();editor.Show();DesktopInput.Pump();
            editor.FieldForTest("Width").Text="123";
            var language=Children(settings).OfType<ComboBox>().Single(c=>AutomationProperties.GetAutomationId(c)=="AppLanguage");
            language.Focus();DesktopInput.Key(settings,Key.End);DesktopInput.Pump();
            check(prefs.Value.Language=="tr"&&new PreferencesStore(Path.Combine(directory,"Language profile")).Value.Language=="tr","Turkish setting did not persist through a real picker selection");
            check(Children(settings).OfType<TextBlock>().Any(t=>t.Text=="Dil"),"Settings language label did not update live");
            check(Children(owner).OfType<Button>().Any(b=>b.Content as string=="Ana Sayfa"),"Open Home buttons did not switch to Turkish");
            // The window opens with one file selected, so Refresh() has already set the selection copy.
            check(Children(owner).OfType<TextBlock>().Any(t=>t.Text=="✦  SIRA SENDE")&&Children(owner).OfType<TextBlock>().Any(t=>t.Text=="Hadi yeni bir şey yapalım.")&&Children(owner).OfType<Button>().Any(b=>b.Content as string=="Dosya ekle veya değiştir"),"Home selection copy did not switch to Turkish");
            check(Children(owner).OfType<TextBlock>().Any(t=>t.Text==Path.GetFileName(image)),"Home translated or lost the selected file name");
            check(Children(editor).OfType<TextBlock>().Any(t=>t.Text=="Görseli Kırp"),"Open crop editor did not switch language");
            check(editor.FieldForTest("Width").Text=="123","Language switch reset an unsaved editor value");
            check(language.SelectedItem as string=="Türkçe","Language picker did not retain the Turkish autonym");
            var raw=new TextBox{Text="Home\r\nLibrary · Türkçe ıİşğü 🍐"};L10n.Attach(raw);
            check(raw.Text=="Home\r\nLibrary · Türkçe ıİşğü 🍐","Localization modified user text");
            Capture(settings,Path.Combine(directory,"windows-settings-turkish.png"));
            var wheel=new WheelWindow(null,Enum.GetValues<WindowPlacement>().Select(p=>new ToolChoice(p.ToString(),p.Title(),"",p)).ToArray(),_=>{},externalDrag:true);
            wheel.Show();DesktopInput.Pump();Capture(wheel,Path.Combine(directory,"windows-window-wheel-turkish.png"));wheel.Close();
            settings.Activate();language.Focus();DesktopInput.Key(settings,Key.Home);DesktopInput.Pump();
            check(prefs.Value.Language=="en"&&Children(owner).OfType<Button>().Any(b=>b.Content as string=="Home"),"Switching back to English left stale Turkish controls");
            check(Children(owner).OfType<TextBlock>().Any(t=>t.Text=="✦  READY WHEN YOU ARE"),"Home selection copy stayed Turkish after switching back to English");
            check(editor.FieldForTest("Width").Text=="123","Switching back reset the crop draft");
        }
        finally{editor.Close();settings.Close();L10n.Language=before;}
    }
    private static void Capture(Window window,string path)
    {
        window.UpdateLayout();var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(window);ImageEngine.Save(bitmap,path,"png");
    }
}
