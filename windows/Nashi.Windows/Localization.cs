using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using Nashi.Core;
namespace Nashi.Windows;

public sealed class LanguageState:INotifyPropertyChanged
{
    public static LanguageState Shared{get;}=new();
    private string language="en";
    public string Language{get=>language;set{var next=AppLocalization.Normalize(value);if(next==language)return;language=next;PropertyChanged?.Invoke(this,new(nameof(Language)));}}
    public event PropertyChangedEventHandler? PropertyChanged;
}
internal static class L10n
{
    internal static string Language{get=>LanguageState.Shared.Language;set=>LanguageState.Shared.Language=value;}
    internal static string Text(string source)=>AppLocalization.Text(source,Language);
    internal static string Uppercase(string source)=>AppLocalization.Uppercase(Text(source),Language);
    internal static void Bind(DependencyObject target,DependencyProperty property,string source)=>BindingOperations.SetBinding(target,property,new Binding(nameof(LanguageState.Language)){Source=LanguageState.Shared,Converter=TextConverter.Shared,ConverterParameter=source,Mode=BindingMode.OneWay});
    internal static TextBlock Label(TextBlock text){Bind(text,TextBlock.TextProperty,text.Text);return text;}
    internal static T Content<T>(T control)where T:ContentControl{if(control.Content is string source)Bind(control,ContentControl.ContentProperty,source);return control;}
    internal static void SetText(TextBlock text,string source)=>Bind(text,TextBlock.TextProperty,source);
    internal static void Attach(DependencyObject root)
    {
        if(root is ButtonBase button&&button.Content is string content&&!BindingOperations.IsDataBound(button,ContentControl.ContentProperty))Bind(button,ContentControl.ContentProperty,content);
        if(root is HeaderedContentControl header&&header.Header is string heading&&!BindingOperations.IsDataBound(header,HeaderedContentControl.HeaderProperty))Bind(header,HeaderedContentControl.HeaderProperty,heading);
        if(root is FrameworkElement element&&element.ToolTip is string tip&&!BindingOperations.IsDataBound(element,FrameworkElement.ToolTipProperty))Bind(element,FrameworkElement.ToolTipProperty,tip);
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)Attach(VisualTreeHelper.GetChild(root,i));
    }
    internal static void Choices(ItemsControl control)
    {
        var factory=new FrameworkElementFactory(typeof(TextBlock));
        var binding=new MultiBinding{Converter=ChoiceConverter.Shared};binding.Bindings.Add(new Binding());binding.Bindings.Add(new Binding(nameof(LanguageState.Language)){Source=LanguageState.Shared});
        factory.SetBinding(TextBlock.TextProperty,binding);control.ItemTemplate=new DataTemplate{VisualTree=factory};
    }
    private sealed class TextConverter:IValueConverter
    {
        internal static readonly TextConverter Shared=new();
        public object Convert(object value,Type type,object parameter,CultureInfo culture)=>AppLocalization.Text((string)parameter,(string)value);
        public object ConvertBack(object value,Type type,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
    private sealed class ChoiceConverter:IMultiValueConverter
    {
        internal static readonly ChoiceConverter Shared=new();
        public object Convert(object[] values,Type type,object parameter,CultureInfo culture)=>values.Length==2&&values[0] is string source&&values[1] is string language?AppLocalization.Text(source,language):values[0];
        public object[] ConvertBack(object value,Type[] types,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
}
