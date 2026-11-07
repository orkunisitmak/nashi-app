using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Nashi.Core;
namespace Nashi.Windows;

internal static class BrowserLinkHint
{
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    private static bool IsBrowser(string process)=>process.ToLowerInvariant() is "chrome" or "brave" or "msedge" or "firefox" or "opera" or "vivaldi";
    internal static bool IsBrowserWindow(IntPtr window)
    {
        try{GetWindowThreadProcessId(window,out var id);using var process=Process.GetProcessById((int)id);return IsBrowser(process.ProcessName);}
        catch(Exception error)when(error is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception){return false;}
    }
    internal static bool AllowsPointerReel(string process,ControlType kind,string url,bool password=false)=>!password&&IsBrowser(process)&&WebMediaPayload.Parse(url)?.Kind=="instagramReel"&&(kind==ControlType.Image||kind==ControlType.Pane||kind==ControlType.Custom||kind==ControlType.Group||kind==ControlType.Document||kind==ControlType.Button||kind==ControlType.Hyperlink);
    internal static bool PointerReel(AutomationElement origin,string url)
    {
        try{using var process=Process.GetProcessById(origin.Current.ProcessId);return AllowsPointerReel(process.ProcessName,origin.Current.ControlType,url,origin.Current.IsPassword);}
        catch(Exception error)when(error is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException or System.ComponentModel.Win32Exception){return false;}
    }
    internal static string? InstagramAddress(string value)
    {
        var payload=WebMediaPayload.Parse(value)??WebMediaPayload.Parse("https://"+value);
        return payload?.Kind is "instagramReel" or "instagramPhotos"?payload.Url:null;
    }
    internal static bool IsLocationMetadata(string name,string id,string shortcut)=>id is "urlbar-input" or "addressEditBox"||shortcut.Contains("Ctrl+L",StringComparison.OrdinalIgnoreCase)||name.StartsWith("Address and search bar",StringComparison.OrdinalIgnoreCase)||name.StartsWith("Adres ve arama çubuğu",StringComparison.OrdinalIgnoreCase)||name.Equals("Adres çubuğu",StringComparison.OrdinalIgnoreCase)||name.Equals("Search or enter address",StringComparison.OrdinalIgnoreCase)||name.Equals("Search with Google or enter address",StringComparison.OrdinalIgnoreCase);
    private static bool OutsideWebPage(AutomationElement field)
    {
        var node=field;for(var n=0;n<30;n++){var parent=TreeWalker.ControlViewWalker.GetParent(node);if(parent is null)return false;var kind=parent.Current.ControlType;if(kind==ControlType.Document)return false;if(kind==ControlType.Window)return true;node=parent;}return false;
    }
    internal static string? Read(AutomationElement origin)
    {
        try
        {
            // A browser video often drags only its poster/blob. Read only its address bar, only for this drag.
            if(origin.Current.IsPassword||origin.Current.ControlType==ControlType.Edit||origin.Current.ControlType==ControlType.Text)return null;
            var process=Process.GetProcessById(origin.Current.ProcessId);using(process){if(!IsBrowser(process.ProcessName))return null;}
            var root=origin;for(var n=0;n<30&&root.Current.ControlType!=ControlType.Window;n++){var parent=TreeWalker.ControlViewWalker.GetParent(root);if(parent is null)return null;root=parent;}
            if(root.Current.ControlType!=ControlType.Window)return null;using(var browser=Process.GetProcessById(root.Current.ProcessId)){if(!IsBrowser(browser.ProcessName))return null;}
            var fields=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit));
            foreach(AutomationElement field in fields)
            {
                var details=field.Current;if(details.IsPassword)continue;var name=details.Name??"";var id=details.AutomationId??"";var shortcut=details.AcceleratorKey??"";
                if(!IsLocationMetadata(name,id,shortcut)||!OutsideWebPage(field))continue;
                if(field.TryGetCurrentPattern(ValuePattern.Pattern,out var pattern)&&InstagramAddress(((ValuePattern)pattern).Current.Value) is {} link)return link;
            }
        }
        catch(Exception error)when(error is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException or System.ComponentModel.Win32Exception){}
        return null;
    }
}
