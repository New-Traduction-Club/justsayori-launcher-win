// SubmodStatusToVisibilityConverter.cs
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace justsayo_win;

public class SubmodStatusToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not SubmodStatus status || parameter is not string targetStatusStr)
        {
            return Visibility.Collapsed;
        }

        var targetStates = targetStatusStr.Split('|');

        foreach (var stateStr in targetStates)
        {
            if (Enum.TryParse<SubmodStatus>(stateStr, true, out var targetStatus))
            {
                if (status == targetStatus)
                {
                    return Visibility.Visible;
                }
            }
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}