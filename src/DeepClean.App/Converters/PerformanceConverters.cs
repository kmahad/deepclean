using System.Globalization;
using System.Windows.Data;
using DeepClean.Core.Performance;
using Wpf.Ui.Controls;

namespace DeepClean.App.Converters;

public sealed class ModeToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PerformanceMode mode)
        {
            return SymbolRegular.Question24;
        }

        return mode switch
        {
            PerformanceMode.Normal => SymbolRegular.Home24,
            PerformanceMode.Gaming => SymbolRegular.Flash24,
            PerformanceMode.PowerSaving => SymbolRegular.BatterySaver24,
            _ => SymbolRegular.Question24
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}