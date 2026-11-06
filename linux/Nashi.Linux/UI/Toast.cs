using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Nashi.Core;
namespace Nashi.Linux.UI;

/// Live progress with Cancel; when the work finishes it offers Open and Show in Folder.
public sealed class Toast:Border
{
    private readonly TextBlock label;private readonly ProgressBar bar=new(){Minimum=0,Maximum=1,IsIndeterminate=true,Height=6,MinWidth=300};
    private readonly StackPanel body;private readonly Button cancel;
    public Toast(string title,Action onCancel)
    {
        Classes.Add("card");Padding=new(18,14);Width=380;HorizontalAlignment=HorizontalAlignment.Right;
        label=Ui.Text("Preparing…",12,"muted");cancel=Ui.Button("Cancel",()=>{onCancel();label.Text=L10n.T("Cancelling…");},"chip");
        var head=new DockPanel();DockPanel.SetDock(cancel,Dock.Right);head.Children.Add(cancel);head.Children.Add(Ui.Text(title,14,"title",false));
        body=new StackPanel{Spacing=8,Children={head,bar,label}};Child=body;
    }
    public void Update(OperationProgress progress)
    {
        label.Text=L10n.T(progress.Label);
        if(progress.Fraction is {} fraction){bar.IsIndeterminate=false;bar.Value=Math.Clamp(fraction,0,1);}else bar.IsIndeterminate=true;
    }
    public void Complete(IReadOnlyList<string> outputs,Action close)
    {
        bar.IsIndeterminate=false;bar.Value=1;cancel.IsVisible=false;
        label.Text=outputs.Count==0?L10n.T("Ready."):L10n.T($"Saved {outputs.Count} new {(outputs.Count==1?"file":"files")}. Originals kept.");
        var buttons=new WrapPanel();
        if(outputs.Count>0){buttons.Children.Add(Ui.Button("Open",()=>LinuxShell.Open(outputs[0]),"chip"));buttons.Children.Add(Ui.Button("Show in Folder",()=>LinuxShell.Reveal(outputs[0]),"chip"));}
        buttons.Children.Add(Ui.Button("Done",close,"chip"));body.Children.Add(buttons);
        DispatcherTimer.RunOnce(close,TimeSpan.FromSeconds(outputs.Count==0?3:12));
    }
}
