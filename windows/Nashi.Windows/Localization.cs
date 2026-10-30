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
    internal static string Text(string source){var shared=AppLocalization.Text(source,Language);return shared==source&&Language=="tr"?WindowsLocalization.Turkish.GetValueOrDefault(source,source):shared;}
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
        public object Convert(object value,Type type,object parameter,CultureInfo culture)=>L10n.Text((string)parameter);
        public object ConvertBack(object value,Type type,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
    private sealed class ChoiceConverter:IMultiValueConverter
    {
        internal static readonly ChoiceConverter Shared=new();
        public object Convert(object[] values,Type type,object parameter,CultureInfo culture)=>values.Length==2&&values[0] is string source&&values[1] is string language?WindowsLocalization.Text(source,language):values[0];
        public object[] ConvertBack(object value,Type[] types,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
}
internal static class WindowsLocalization
{
    private static readonly IReadOnlyDictionary<string,string> translations=new Dictionary<string,string>(StringComparer.Ordinal)
    {
        ["Appearance"]="Görünüm",["Shortcuts"]="Kısayollar",["Behavior"]="Davranış",["Accent color"]="Vurgu rengi",
        ["Gold"]="Altın",["Blue"]="Mavi",["Violet"]="Mor",["Green"]="Yeşil",["Pill size"]="Hap boyutu",
        ["When Nashi opens"]="Nashi açıldığında",["Start hidden · use shortcut"]="Gizli başlat · kısayolla aç",
        ["Open the pill expanded"]="Hap genişletilmiş açılsın",["Pill visibility"]="Hap görünürlüğü",
        ["Shortcut toggle"]="Kısayolla aç / kapat",["Always visible"]="Her zaman görünür",["Show / hide pill"]="Hap görünürlüğü kısayolu",
        ["Your files, your way."]="Dosyaların senin düzeninde.",
        ["Original files stay untouched. Set where new copies are saved."]="Orijinal dosyalar değişmez. Yeni kopyaların nereye kaydedileceğini seç.",
        ["How Nashi behaves."]="Nashi'nin davranışı.",
        ["Tune startup, visibility and small quality-of-life details."]="Başlangıç, görünürlük ve kullanım tercihlerini düzenle.",
        ["Make it yours."]="Kendine göre düzenle.",
        ["Choose a look and see changes on the pill right away."]="Bir görünüm seç; değişiklikleri hapta anında gör.",
        ["One PDF per page by default. Enter a larger number to group pages into each new PDF. Leave page ranges blank to split every page; use ranges such as 1-3, 5 to include selected pages only."]="Varsayılan olarak her sayfa ayrı PDF olur. Sayfaları gruplamak için daha büyük bir sayı gir. Tüm sayfaları bölmek için aralık alanını boş bırak; yalnızca seçili sayfalar için 1-3, 5 gibi aralıklar yaz.",
        ["Your original stays safe. Save creates new files."]="Orijinal dosyan korunur. Kaydet yeni dosyalar oluşturur.",
        ["Choose PDF files to edit"]="Düzenlenecek PDF dosyalarını seç",["{0} PDF selected"]="{0} PDF seçildi",
        ["Start a track in a media app to see it here."]="Burada görmek için bir medya uygulamasında parça başlat."
    };
    internal static string Text(string source,string language)=>language=="tr"&&translations.TryGetValue(source,out var result)?result:AppLocalization.Text(source,language);
    internal static IReadOnlyDictionary<string,string> Turkish=>translations;
}
