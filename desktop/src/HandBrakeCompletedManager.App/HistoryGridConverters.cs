using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using HandBrakeCompletedManager.Core;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;

namespace HandBrakeCompletedManager.App;

public sealed class OneBasedIndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int index ? index + 1 : 1;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}

public sealed class OutputPercentageBackgroundConverter : IValueConverter
{
    private static readonly MediaBrush PaleOrange = CreateBrush("#FFFFE8D5");
    private static readonly MediaBrush PaleRed = CreateBrush("#FFFFDCDC");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return OutputPercentageHighlightRules.Classify(value as double?) switch
        {
            OutputPercentageHighlight.PaleRed => PaleRed,
            OutputPercentageHighlight.PaleOrange => PaleOrange,
            _ => MediaBrushes.Transparent
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;

    private static MediaBrush CreateBrush(string color)
    {
        var brush = new SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
