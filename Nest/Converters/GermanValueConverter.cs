using System;
using System.Globalization;
using System.Windows.Data;

namespace Nest.Converters
{
    public sealed class GermanValueConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value switch
            {
                "Verfuegbar" => "Verfügbar",
                "Nicht_verfuegbar" => "Nicht verfügbar",
                "Fuehrerschein" => "Führerschein",
                "Ueberweisung" => "Überweisung",
                _ => value
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value switch
            {
                "Verfügbar" => "Verfuegbar",
                "Nicht verfügbar" => "Nicht_verfuegbar",
                "Führerschein" => "Fuehrerschein",
                "Überweisung" => "Ueberweisung",
                _ => value
            };
        }
    }
}
