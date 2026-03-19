using System.Globalization;
using System.Windows.Data;

namespace SafeSeal.App.Converters;

public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Equals(value, parameter);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked)
        {
            return parameter ?? Binding.DoNothing;
        }

        return Binding.DoNothing;
    }
}
