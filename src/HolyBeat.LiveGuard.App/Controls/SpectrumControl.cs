using System;
using System.Windows;
using System.Windows.Media;

namespace HolyBeat.LiveGuard.App.Controls;

/// <summary>
/// Draws the live spectrum of the processed output with a gradient body and peak-hold caps.
/// </summary>
public sealed class SpectrumControl : FrameworkElement
{
    private float[] _magnitudes = Array.Empty<float>();
    private float[] _peaks = Array.Empty<float>();
    private double _peakDecay = 0.86;

    public SpectrumControl()
    {
        ClipToBounds = true;
        IsVisibleChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>Magnitude values in dB, ordered from low to high frequency.</summary>
    public void Update(float[] magnitudes)
    {
        if (magnitudes == null || magnitudes.Length == 0)
            return;

        if (_magnitudes.Length != magnitudes.Length)
        {
            _magnitudes = new float[magnitudes.Length];
            _peaks = new float[magnitudes.Length];
        }

        Array.Copy(magnitudes, _magnitudes, magnitudes.Length);

        for (int i = 0; i < _peaks.Length; i++)
        {
            _peaks[i] = (float)Math.Max(_magnitudes[i], _peaks[i] * _peakDecay);
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0A, 0x0D, 0x12)), null, bounds);

        if (_magnitudes.Length == 0 || ActualWidth <= 1 || ActualHeight <= 1)
            return;

        // Grid lines
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0x1E, 0x25, 0x2F)), 1);
        for (int i = 1; i < 8; i++)
        {
            double x = ActualWidth * i / 8.0;
            dc.DrawLine(gridPen, new Point(x, 0), new Point(x, ActualHeight));
        }

        double barWidth = ActualWidth / _magnitudes.Length;
        for (int i = 0; i < _magnitudes.Length; i++)
        {
            double db = _magnitudes[i];
            double normalized = Math.Clamp((db + 90.0) / 90.0, 0.0, 1.0);
            double height = normalized * (ActualHeight - 6);

            var brush = new LinearGradientBrush(
                Color.FromRgb(0x37, 0xD6, 0xB0),
                Color.FromRgb(0x1B, 0x3A, 0x5C),
                new Point(0, 1), new Point(0, 0));

            dc.DrawRectangle(brush, null, new Rect(i * barWidth + 0.5, ActualHeight - height, Math.Max(1, barWidth - 1), height));

            double peakNormalized = Math.Clamp((_peaks[i] + 90.0) / 90.0, 0.0, 1.0);
            double peakY = ActualHeight - peakNormalized * (ActualHeight - 6);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x20)), null,
                new Rect(i * barWidth + 0.5, peakY - 1.5, Math.Max(1, barWidth - 1), 1.5));
        }
    }
}
