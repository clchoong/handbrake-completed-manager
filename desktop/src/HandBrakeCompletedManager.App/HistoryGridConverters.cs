using System.Globalization;
using System.Windows.Data;
using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.App;

public sealed class OneBasedIndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int index ? index + 1 : 1;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}

public sealed class OutputPercentageHighlightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        OutputPercentageHighlightRules.Classify(value as double?);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}
