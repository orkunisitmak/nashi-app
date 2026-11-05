using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nashi.Windows;

/// Regressions for the shared Windows application icon system.
internal static class VisualSystemSmoke
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(var id in new[]{"preview","reuse","reveal","home","library","settings",
            "remove","download","fineTune","fileActions","chevron","search","convert",
            "previous","next","more","play","info","checkmark"})
        {
            var icon=WheelIcon.Create(id,18,false);
            check(icon is Viewbox { Width:18, Height:18, Child:Canvas{Width:24,Height:24} },
                "App icon uses same 24-unit geometry: "+id);
            check(((Canvas)icon!.Child).Children.OfType<System.Windows.Shapes.Path>().Any(),
                "App icon has an actual vector path: "+id);
        }
        check(NashiDesignTokens.IconGrid==24 && NashiDesignTokens.ActionTarget==36 &&
            NashiDesignTokens.SearchHeight==38 && NashiDesignTokens.SettingsCardRadius==16,
            "Reusable visual tokens keep Windows icon and control measurements consistent");
        var combo=new ComboBox{ItemsSource=new[]{"Light","Dark"},SelectedIndex=0};
        combo.Style=(Style)Application.Current.FindResource(typeof(ComboBox));
        combo.ApplyTemplate();
        var chevron=combo.Template?.FindName("NashiChevron",combo) as System.Windows.Shapes.Path;
        check(ReferenceEquals(chevron?.Data,FluentIcons.GeometryFor("chevron")),
            "Dropdown chevron uses pinned official Fluent geometry");
        var checkBox=new CheckBox{Content="Check",IsChecked=true};
        checkBox.Style=(Style)Application.Current.FindResource(typeof(CheckBox));
        checkBox.ApplyTemplate();
        var tick=checkBox.Template?.FindName("NashiCheckMark",checkBox) as System.Windows.Shapes.Path;
        check(ReferenceEquals(tick?.Data,FluentIcons.GeometryFor("checkmark")),
            "Settings checkbox tick uses pinned official Fluent geometry");
        var action=AppIcons.ActionButton("preview","Preview",()=>{});
        check(action.Width==36 && action.Height==36 && action.Content is Viewbox,
            "Application icon actions use a consistent 36px target");
        check(Equals(AutomationProperties.GetName(action),"Preview") &&
            Equals(action.ToolTip,"Preview"),"Icon-only actions are accessible and labeled");
        check(action.Padding==new Thickness(0) && action.IsEnabled,
            "Action glyphs are centered and remain interactive");
        var converter=AppIcons.FixedIcon("remove",12);
        var first=converter.Convert("file-a",typeof(object),null!,System.Globalization.CultureInfo.InvariantCulture);
        var second=converter.Convert("file-b",typeof(object),null!,System.Globalization.CultureInfo.InvariantCulture);
        check(first is Viewbox && second is Viewbox && !ReferenceEquals(first,second),
            "Repeated file chips receive independent removable icon controls");
        var shellPath=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"Nashi-icon-fixture.pdf");
        var shelfArt=FloatArtwork.Image(shellPath,48);
        check(shelfArt is Border{Child:Image native} &&
              native.GetValue(FileArtwork.PathProperty) is string artworkPath &&
              artworkPath==shellPath,
            "Float uses native Windows file artwork for non-image files");
        var checkbox=new CheckBox{Content="Checkbox fixture",IsChecked=true};
        checkbox.ApplyTemplate();
        check(checkbox.Template.FindName("NashiCheckMark",checkbox) is System.Windows.Shapes.Path,
            "Settings use the shared rounded, theme-aware checkbox visual");
        var sample=MainWindow.Button("Home",()=>{});
        AppIcons.Decorate(sample,"home",16);
        check(Equals(sample.Content,"Home") && sample.ContentTemplate is not null,
            "Header button retains text for shortcuts/localization and gains a shared icon");
        var search=new TextBox{Width=180,ToolTip="Search"};
        var searchChrome=AppIcons.SearchField(search);
        check(searchChrome is Border{Tag:"NashiSearchField",Child:Grid} &&
            ((Grid)searchChrome.Child).Children.OfType<Viewbox>().Count()==1 &&
            ((Grid)searchChrome.Child).Children.OfType<TextBox>().Single()==search &&
            Math.Abs(searchChrome.Width-180)<.1,
            "Search fields use one vector magnifier without replacing text input or width");
        var labeled=AppIcons.Labeled("download","Download a link",18);
        check(labeled.Children.OfType<Viewbox>().Count()==1 &&
            labeled.Children.OfType<TextBlock>().Any(),
            "Icon+text actions share vectors and keep readable labels");
    }
}
