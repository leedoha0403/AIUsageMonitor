using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AIUsage.Presentation.Converters;

public sealed class TokenFreeBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 138, 112))
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(197, 64, 73));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
