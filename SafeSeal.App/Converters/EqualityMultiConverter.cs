using System.Globalization;
using System.Windows.Data;

namespace SafeSeal.App.Converters;

public sealed class EqualityMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
        {
            return false;
        }

        object? left = values[0];
        object? right = values[1];
        return Equals(left, right);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        return [Binding.DoNothing, Binding.DoNothing];
    }
}
