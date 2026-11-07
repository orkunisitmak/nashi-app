using System.Windows;
using System.ComponentModel;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Microsoft.Win32;
using Nashi.Core;
namespace Nashi.Windows;

internal sealed class DownloadSaveWindow:Window
{
    private readonly TextBox name=new(){MaxLength=160};
    private readonly TextBox folder=new(){IsReadOnly=true};
    private readonly TextBlock message=new(){TextWrapping=TextWrapping.Wrap,Margin=new(4,12,4,0),MinHeight=38};
    private readonly Button download;
    private readonly CancellationTokenSource loading=new(TimeSpan.FromSeconds(30));
    private bool nameEdited,readingTitle;
    internal string FileName=>name.Text.Trim();
    internal string DirectoryPath=>folder.Text;
    internal TextBox NameField=>name;
    internal TextBox FolderField=>folder;
    internal Button DownloadButton=>download;
    internal bool ReadingTitle=>readingTitle;
    internal DownloadSaveWindow(Window owner,string destination,string suggestedName,Func<CancellationToken,Task<string>>? loadTitle=null)
    {
        Owner=owner;Title="Save your download · Nashi";Width=620;MinWidth=520;MinHeight=370;SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;Theme.Attach(this);
        var root=new DockPanel{Margin=new(24)};Content=root;
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,20,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var cancel=MainWindow.Button("Cancel",Close);cancel.IsCancel=true;footer.Children.Add(cancel);
        download=MainWindow.Primary("Download",Confirm);download.IsDefault=true;footer.Children.Add(download);
        var page=new StackPanel();root.Children.Add(new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        page.Children.Add(MainWindow.Text("Save your download",26));
        page.Children.Add(MainWindow.Text("Choose a name and folder for this download.",13,"Muted"));
        name.Text=WebDownload.SuggestedFileName(suggestedName);folder.Text=destination;
        Field(page,"File name",name);page.Children.Add(name);
        page.Children.Add(MainWindow.Text("The file extension is added automatically.",12,"Muted"));
        Field(page,"Save to folder",folder);var row=new DockPanel();var browse=MainWindow.Button("Choose folder…",()=>
        {
            var picker=new OpenFolderDialog{Title=L10n.Text("Save downloaded media to…"),InitialDirectory=DirectoryPath};
            if(picker.ShowDialog(this)==true)folder.Text=picker.FolderName;
        });DockPanel.SetDock(browse,Dock.Right);row.Children.Add(browse);row.Children.Add(folder);page.Children.Add(row);
        page.Children.Add(MainWindow.Text("Your default folder stays the same. Existing files are kept.",12,"Muted"));page.Children.Add(message);
        AutomationProperties.SetAutomationId(name,"DownloadFileName");AutomationProperties.SetAutomationId(folder,"DownloadFolder");AutomationProperties.SetAutomationId(download,"ConfirmDownload");
        AutomationProperties.SetAutomationId(message,"DownloadStatus");AutomationProperties.SetLiveSetting(message,AutomationLiveSetting.Polite);
        var statusText=DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty,typeof(TextBlock));
        EventHandler announce=(_,_)=>
        {
            if(!IsVisible||string.IsNullOrWhiteSpace(message.Text))return;
            var peer=UIElementAutomationPeer.FromElement(message)??UIElementAutomationPeer.CreatePeerForElement(message);peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        };
        statusText.AddValueChanged(message,announce);
        readingTitle=loadTitle is not null;
        name.TextChanged+=(_,_)=>{nameEdited=true;ValidateName();};
        folder.TextChanged+=(_,_)=>ValidateName();
        Closed+=(_,_)=>{statusText.RemoveValueChanged(message,announce);loading.Cancel();loading.Dispose();};
        Loaded+=async(_,_)=>
        {
            name.Focus();name.SelectAll();ValidateName();
            if(loadTitle is null)return;
            var token=loading.Token;var failed=false;
            try{var title=await loadTitle(token);if(!IsVisible)return;token.ThrowIfCancellationRequested();if(!nameEdited){name.Text=WebDownload.SuggestedFileName(title);name.SelectAll();}}
            catch(Exception error)when(error is not OutOfMemoryException){failed=true;if(!IsVisible)return;L10n.SetText(message,"Could not read the media title. Enter a name to continue.");}
            finally{readingTitle=false;if(IsVisible)ValidateName(clearMessage:!failed);}
        };
    }
    private static void Field(Panel page,string text,Control target)
    {
        var label=new Label{Target=target,Margin=new(0,18,0,0),Padding=new(4,0,4,0)};label.SetResourceReference(Control.ForegroundProperty,"Ink");L10n.Bind(label,ContentControl.ContentProperty,text);page.Children.Add(label);AutomationProperties.SetName(target,L10n.Text(text));
    }
    private void ValidateName(bool clearMessage=true)
    {
        download.IsEnabled=false;
        if(readingTitle){L10n.SetText(message,"Reading the media title…");return;}
        if(!Path.IsPathFullyQualified(DirectoryPath)){L10n.SetText(message,"Choose a download folder to continue.");return;}
        try{WebDownload.ValidateFileName(FileName);download.IsEnabled=true;if(clearMessage)message.Text="";}
        catch(ArgumentException error){L10n.SetText(message,error.Message);}
    }
    private void Confirm(){ValidateName();if(download.IsEnabled)DialogResult=true;}
}
