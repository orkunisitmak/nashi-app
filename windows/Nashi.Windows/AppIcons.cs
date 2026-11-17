using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Globalization;
using System.Windows.Media;

namespace Nashi.Windows;

/// Central control and sizing policy for app-owned actions. File artwork stays native.
internal static class AppIcons
{
    internal static Viewbox Icon(string id,double size=18)=>
        WheelIcon.Create(id,size,false)??throw new ArgumentException("Unknown application icon: "+id,nameof(id));

    internal static Button ActionButton(string id,string title,Action action)
    {
        var button=new Button
        {
            Content=Icon(id),Width=NashiDesignTokens.ActionTarget,Height=NashiDesignTokens.ActionTarget,Padding=new Thickness(0),
            Margin=new Thickness(3),ToolTip=title
        };
        button.Style=(Style)Application.Current.FindResource("NashiIconButton");
        AutomationProperties.SetName(button,title);
        button.Click+=(_,_)=>action();
        return button;
    }

    internal static IValueConverter FixedIcon(string id,double size=18)=>new FixedIconConverter(id,size);

    private sealed class FixedIconConverter(string id,double size):IValueConverter
    {
        public object Convert(object value,Type type,object parameter,CultureInfo culture)=>Icon(id,size);
        public object ConvertBack(object value,Type type,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }

    internal static Border SearchField(TextBox field)
    {
        var width=field.Width;
        var minWidth=field.MinWidth;
        field.Width=double.NaN;
        field.MinWidth=0;
        field.Margin=new Thickness(29,0,0,0);
        field.Padding=new Thickness(2,7,6,7);
        field.Background=Brushes.Transparent;
        field.BorderThickness=new Thickness(0);
        var host=new FrameworkElementFactory(typeof(ScrollViewer));
        host.Name="PART_ContentHost";
        field.Template=new ControlTemplate(typeof(TextBox)){VisualTree=host};
        var icon=Icon("search",16);
        icon.IsHitTestVisible=false;
        icon.HorizontalAlignment=HorizontalAlignment.Left;
        icon.VerticalAlignment=VerticalAlignment.Center;
        icon.Margin=new Thickness(11,0,0,0);
        var body=new Grid();
        body.Children.Add(icon);
        body.Children.Add(field);
        var border=new Border{Tag="NashiSearchField",Child=body,MinWidth=minWidth,
            Width=width,MinHeight=NashiDesignTokens.SearchHeight,Margin=new Thickness(4),CornerRadius=new CornerRadius(11),
            Padding=new Thickness(0,0,6,0),BorderThickness=new Thickness(1)};
        border.SetResourceReference(Border.BackgroundProperty,"Inset");
        border.SetResourceReference(Border.BorderBrushProperty,"Line");
        field.GotKeyboardFocus+=(_,_)=>border.SetResourceReference(Border.BorderBrushProperty,"Gold");
        field.LostKeyboardFocus+=(_,_)=>border.SetResourceReference(Border.BorderBrushProperty,"Line");
        return border;
    }

    internal static Button Decorate(Button button,string id,double size=16)
    {
        var row=new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);
        var glyph=new FrameworkElementFactory(typeof(ContentControl));
        glyph.SetBinding(ContentControl.ContentProperty,new Binding{Converter=FixedIcon(id,size)});
        glyph.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,7,0));
        glyph.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        row.AppendChild(glyph);
        var text=new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty,new Binding());
        text.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        row.AppendChild(text);
        button.ContentTemplate=new DataTemplate{VisualTree=row};
        return button;
    }

    internal static StackPanel Labeled(string id,string title,double size=18)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        var icon=Icon(id,size);
        icon.Margin=new Thickness(0,0,10,0);
        row.Children.Add(icon);
        var caption=new TextBlock{Text=title,VerticalAlignment=VerticalAlignment.Center,
            TextWrapping=TextWrapping.Wrap};
        row.Children.Add(L10n.Label(caption));
        return row;
    }
}
