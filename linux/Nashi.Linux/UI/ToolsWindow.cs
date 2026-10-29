using Avalonia.Controls;
using Avalonia.Layout;
using Nashi.Linux.Engine;
namespace Nashi.Linux.UI;

/// Linux replacement for the Windows "Finish setup" downloader: it shows which
/// distribution packages Nashi found and the one command that installs the rest.
internal sealed class ToolsWindow:Window
{
    private readonly Operations operations;private readonly Action changed;private readonly StackPanel stack=new(){Spacing=10};
    public ToolsWindow(Operations operations,Action changed)
    {
        this.operations=operations;this.changed=changed;Title=L10n.T("System tools")+" · Nashi";Width=640;SizeToContent=SizeToContent.Height;MaxHeight=780;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        this.Res(BackgroundProperty,"Paper");Content=new ScrollViewer{Content=new Border{Padding=new(26),Child=stack}};Render();
    }
    private void Render()
    {
        stack.Children.Clear();stack.Children.Add(Brand.Text("System tools",26));
        stack.Children.Add(Brand.Text("Nashi uses the tools your distribution maintains, so they get security updates with the rest of your system.",13,"Muted"));
        var rows=new StackPanel{Spacing=2};
        foreach(var tool in operations.Status())
        {
            var row=new DockPanel{Margin=new(0,4)};var mark=Brand.Text(tool.Path is null?"○":"●",14,tool.Path is null?"Muted":"Gold",false);mark.Margin=new(0,0,12,0);DockPanel.SetDock(mark,Dock.Left);row.Children.Add(mark);
            var state=Brand.Text(tool.Path is null?"Missing":"Found",12,"Muted");DockPanel.SetDock(state,Dock.Right);row.Children.Add(state);
            var text=new StackPanel();text.Children.Add(Brand.Text(tool.Name,13,null,false));text.Children.Add(Brand.Text(tool.Path??L10n.T(tool.Purpose),11,"Muted",false));if(tool.Path is not null)ToolTip.SetTip(text,L10n.T(tool.Purpose));row.Children.Add(text);rows.Children.Add(row);
        }
        stack.Children.Add(Brand.Card(rows,16));
        var command=operations.InstallCommand();
        if(command.Length==0)stack.Children.Add(Brand.Text("All tools are ready.",14));
        else{stack.Children.Add(Brand.Text("Install the missing tools in a terminal:",13));stack.Children.Add(Command(command));}
        if(LinuxPdf.Tesseract is null){stack.Children.Add(Brand.Text("Optional OCR for scanned PDFs:",13));stack.Children.Add(Command(LinuxPdf.InstallTesseract));}
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};buttons.Children.Add(Brand.Button("Check again",()=>{Render();changed();}));buttons.Children.Add(Brand.Primary("Close",Close));stack.Children.Add(buttons);
    }
    private Control Command(string command)
    {
        var row=new DockPanel();var copy=Brand.Button("Copy command",async()=>{if(Clipboard is not null)await Clipboard.SetTextAsync(command);});copy.Margin=new(8,0,0,0);DockPanel.SetDock(copy,Dock.Right);row.Children.Add(copy);
        row.Children.Add(new TextBox{Text=command,IsReadOnly=true,FontFamily=new("monospace"),TextWrapping=Avalonia.Media.TextWrapping.Wrap});return row;
    }
}
