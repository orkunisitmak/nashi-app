using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Nashi.Core;
namespace Nashi.Windows;

internal sealed class LinkDownloadWindow:Window
{
    private readonly TextBox address=new(){MinWidth=0};private readonly WrapPanel choices=new(){Margin=new(0,18,0,0)};
    private readonly TextBlock status=MainWindow.Text("Copy a public media link, then Paste link.",13,"Muted");
    private readonly Action<WebMediaPayload,string> choose;private readonly Func<string> readClipboard;private readonly Button paste,more;private bool busy;
    internal TextBox Address=>address;internal IReadOnlyList<Button> DownloadButtons=>choices.Children.OfType<Button>().ToArray();
    internal LinkDownloadWindow(Window owner,Action<WebMediaPayload,string> choose,Func<string>? readClipboard=null)
    {
        Owner=owner;this.choose=choose;this.readClipboard=readClipboard??Clipboard.GetText;Title="Send a link · Nashi";Width=620;Height=470;MinWidth=520;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;Theme.Attach(this);
        var root=new DockPanel{Margin=new(24)};Content=root;var footer=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,18,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var close=MainWindow.Button("Close",Close);close.IsCancel=true;footer.Children.Add(close);
        more=MainWindow.Button("More download options…",()=>Select("options"));footer.Children.Add(more);
        var content=new StackPanel();root.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});content.Children.Add(MainWindow.Text("Send a link",26));content.Children.Add(MainWindow.Text("Copy on Instagram or YouTube. Choose video or audio here.",14,"Muted"));
        var label=new Label{Target=address,Padding=new(4,18,4,6)};label.SetResourceReference(Control.ForegroundProperty,"Ink");L10n.Bind(label,ContentControl.ContentProperty,"Media link");content.Children.Add(label);
        var row=new DockPanel();paste=MainWindow.Button("Paste link",Paste);DockPanel.SetDock(paste,Dock.Right);row.Children.Add(paste);row.Children.Add(address);content.Children.Add(row);content.Children.Add(choices);status.Margin=new(4,14,4,0);content.Children.Add(status);
        AutomationProperties.SetName(address,L10n.Text("Media link"));AutomationProperties.SetAutomationId(address,"CopiedMediaLink");AutomationProperties.SetAutomationId(status,"LinkStatus");AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);
        var descriptor=DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty,typeof(TextBlock));EventHandler announce=(_,_)=>{if(IsVisible)(UIElementAutomationPeer.FromElement(status)??UIElementAutomationPeer.CreatePeerForElement(status))?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);};descriptor.AddValueChanged(status,announce);Closed+=(_,_)=>descriptor.RemoveValueChanged(status,announce);
        address.TextChanged+=(_,_)=>Refresh();Loaded+=(_,_)=>{address.Focus();address.SelectAll();};PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){Close();e.Handled=true;}};
    }
    internal void SetLink(string text,string? error=null){address.Text=text;Refresh();if(error is not null)L10n.SetText(status,error);}
    internal void Paste(){if(busy)return;try{SetLink(readClipboard());}catch(Exception error)when(ClipboardImport.IsClipboardError(error)){SetLink("","Clipboard is busy. Try again.");}}
    internal void SetBusy(bool value){busy=value;address.IsEnabled=paste.IsEnabled=more.IsEnabled=!value;Refresh();}
    private void Refresh()
    {
        choices.Children.Clear();var payload=WebMediaPayload.Parse(address.Text);
        if(payload is null){L10n.SetText(status,string.IsNullOrWhiteSpace(address.Text)?"Copy a public media link, then Paste link.":"Paste one complete public media link.");return;}
        foreach(var choice in DragPayload.DownloadChoices(payload))
        {
            var button=choices.Children.Count==0?MainWindow.Primary(choice.Title,()=>Select(choice.Id)):MainWindow.Button(choice.Title,()=>Select(choice.Id));button.IsDefault=choices.Children.Count==0;button.IsEnabled=!busy;AutomationProperties.SetAutomationId(button,choice.Id);choices.Children.Add(button);
        }
        L10n.SetText(status,busy?"Finish or cancel the current operation first.":"Choose what to download. Your name and folder preference applies.");
    }
    private void Select(string action)
    {
        if(busy){L10n.SetText(status,"Finish or cancel the current operation first.");return;}var payload=WebMediaPayload.Parse(address.Text);if(payload is null){L10n.SetText(status,"Paste one complete public media link.");address.Focus();return;}
        if(action!="options"&&!DragPayload.DownloadChoices(payload).Any(choice=>choice.Id==action))return;
        Close();choose(payload,action);
    }
}
