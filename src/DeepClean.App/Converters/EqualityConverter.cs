using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DeepClean.App.Converters;

public sealed class EqualityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Equals(value, parameter);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? parameter! : Binding.DoNothing;
    }
}