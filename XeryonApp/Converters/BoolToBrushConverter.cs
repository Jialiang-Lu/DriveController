using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace XeryonApp.Converters;

public class BoolToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && parameter is string s && targetType.IsAssignableFrom(typeof(IBrush)))
        {
            if (s.Contains('|'))
            {
                var colors = s.Split('|');
                if (colors.Length == 2)
                {
                    try
                    {
                        var color1 = Brush.Parse(colors[0]);
                        var color2 = Brush.Parse(colors[1]);
                        return b ? color1 : color2;
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                }
            }
        }

        return new BindingNotification(new Exception("Invalid parameters"), BindingErrorType.Error);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}