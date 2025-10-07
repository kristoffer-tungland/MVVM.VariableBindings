using System;
using System.Globalization;
using System.Windows.Data;

namespace MVVM.VariableBindings.Converters;

public sealed class ScopeHeaderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is VariableScope scope
            ? scope.ToString()
            : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
