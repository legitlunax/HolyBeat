using System;
using System.Globalization;
using System.Windows.Data;

namespace HolyBeat.LiveGuard.App.Converters;

/// <summary>
/// Formats a numeric parameter with its unit (Hz, dB, ms, LUFS, ratio...).
/// Used by the configuration panel so every slider reads like the research notes.
/// </summary>
public sealed class UnitFormatConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string unit = parameter as string ?? string.Empty;

        double number = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            _ => 0d
        };

        int decimals = unit switch
        {
            "Hz" => 0,
            "dB" => 1,
            "dBTP" => 1,
            "LUFS" => 1,
            "ms" => 1,
            "ratio" => 1,
            "width" => 2,
            "mix" => 3,
            "stageWidth" => 2,
            "percent" => 0,
            _ => 1
        };

        string formatted = unit == "percent"
            ? (number * 100.0).ToString("F0", culture)
            : number.ToString("F" + decimals, culture);

        return unit switch
        {
            "" => formatted,
            "ratio" => formatted + ":1",
            "percent" => formatted + " %",
            _ => formatted + " " + unit
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}
