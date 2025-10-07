using System;
using System.Globalization;
using System.Windows.Data;

namespace MVVM.VariableBindings.Converters;

public sealed class BoolToSuggestedHeaderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
        {
            return "Suggested";
        }

        return "All variables";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
