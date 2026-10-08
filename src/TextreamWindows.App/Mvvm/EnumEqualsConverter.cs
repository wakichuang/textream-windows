using System.Globalization;
using System.Windows.Data;

namespace TextreamWindows.App.Mvvm;

/// <summary>讓一組 RadioButton 綁同一個列舉屬性：ConverterParameter 寫哪個值，等於它就勾選。</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Enum.Parse(targetType, parameter.ToString()!) : Binding.DoNothing;
}
