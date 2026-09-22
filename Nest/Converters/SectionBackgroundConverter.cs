using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Nest.Converters
{
    public sealed class SectionBackgroundConverter : IValueConverter
    {
        private static readonly Brush SelectedBrush = new SolidColorBrush(Color.FromRgb(45, 99, 173));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal)
                ? SelectedBrush
                : Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
