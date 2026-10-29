using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FileNest.Ui;

/// <summary>Visible when the bound value equals ConverterParameter (string compare). Used for page switching.</summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>True when the bound value equals ConverterParameter; setting true writes the parameter back. For RadioButton groups.</summary>
public sealed class EqualsToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is null)
            return Binding.DoNothing;
        return targetType.IsEnum ? Enum.Parse(targetType, parameter.ToString()!) : parameter;
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Collapsed for null, empty strings and zero counts.</summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var empty = value switch
        {
            null => true,
            string s => s.Length == 0,
            int i => i == 0,
            System.Collections.ICollection c => c.Count == 0,
            _ => false,
        };
        return empty ^ Invert ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public static class Format
{
    public static string Size(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }

    public static string Ago(DateTime time)
    {
        var span = DateTime.Now - time;
        if (span.TotalSeconds < 45)
            return "just now";
        if (span.TotalMinutes < 60)
            return $"{Math.Max(1, (int)Math.Round(span.TotalMinutes))} min ago";
        if (span.TotalHours < 24 && time.Date == DateTime.Today)
            return $"{(int)span.TotalHours} hr ago";
        if (time.Date == DateTime.Today.AddDays(-1))
            return $"Yesterday, {time:h:mm tt}";
        return time.Year == DateTime.Now.Year ? time.ToString("MMM d, h:mm tt") : time.ToString("MMM d yyyy, h:mm tt");
    }

    public static string Count(long n) => n.ToString("N0", CultureInfo.CurrentCulture);
}
