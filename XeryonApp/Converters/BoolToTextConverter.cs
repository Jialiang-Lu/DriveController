using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace XeryonApp.Converters;

public class BoolToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && parameter is string s)
        {
            if (s.Contains('|'))
            {
                var labels = s.Split('|');
                if (labels.Length == 2)
                {
                    return b ? labels[0] : labels[1];
                }
            }

            return s;
        }

        return new BindingNotification(new Exception("Invalid parameters"), BindingErrorType.Error);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}