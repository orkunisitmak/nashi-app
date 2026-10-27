using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
namespace Nashi.Linux;

public sealed class WheelWindow:Window
{
    private readonly Button[] buttons;
    private int selected;
    public WheelWindow((string Id,string Label)[] choices,Action<string> choose)
    {
        Title="Nashi · Tools";Width=560;Height=560;CanResize=false;FontFamily=new Avalonia.Media.FontFamily("Noto Sans, DejaVu Sans");Background=NashiTheme.Paper;Foreground=NashiTheme.Ink;
        var canvas=new Canvas();Content=canvas;var center=NashiTheme.Text("Tools",22);Canvas.SetLeft(center,245);Canvas.SetTop(center,246);canvas.Children.Add(center);
        buttons=choices.Select((choice,index)=>
        {
            var angle=-Math.PI/2+index*2*Math.PI/choices.Length;var button=NashiTheme.Button(choice.Label,()=>{Close();choose(choice.Id);});button.Width=150;
            Canvas.SetLeft(button,205+175*Math.Cos(angle));Canvas.SetTop(button,240+175*Math.Sin(angle));canvas.Children.Add(button);return button;
        }).ToArray();
        Opened+=(_,_)=>buttons[0].Focus();KeyDown+=(_,e)=>
        {
            if(e.Key==Key.Escape){Close();e.Handled=true;return;}
            if(e.Key is Key.Right or Key.Down or Key.Tab){selected=(selected+1)%buttons.Length;buttons[selected].Focus();e.Handled=true;}
            if(e.Key is Key.Left or Key.Up){selected=(selected+buttons.Length-1)%buttons.Length;buttons[selected].Focus();e.Handled=true;}
        };
    }
}
