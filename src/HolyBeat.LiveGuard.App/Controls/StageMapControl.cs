using System;
using System.Windows;
using System.Windows.Media;

namespace HolyBeat.LiveGuard.App.Controls;

/// <summary>
/// Draws the virtual headphone stage: the listener in the middle with the front,
/// height, surround and rumble buses around it, each one pulsing with its live level.
/// </summary>
public sealed class StageMapControl : FrameworkElement
{
    private double[] _levels = new double[8];

    public StageMapControl()
    {
        ClipToBounds = true;
    }

    /// <summary>Level per bus in dB, ordered like the virtual stage bus enum.</summary>
    public void Update(double[]? levels)
    {
        if (levels is null || levels.Length == 0)
            return;

        if (_levels.Length != levels.Length)
            _levels = new double[levels.Length];

        Array.Copy(levels, _levels, levels.Length);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0A, 0x0D, 0x12)), null, bounds);

        double width = ActualWidth;
        double height = ActualHeight;
        if (width < 10 || height < 10)
            return;

        double scale = Math.Min(width, height);

        // The listener.
        double headX = width * 0.5;
        double headY = height * 0.52;
        double headRadius = scale * 0.085;

        dc.DrawEllipse(
            new SolidColorBrush(Color.FromRgb(0x1B, 0x22, 0x2C)),
            new Pen(new SolidColorBrush(Color.FromRgb(0x32, 0x3D, 0x4B)), 1),
            new Point(headX, headY), headRadius, headRadius);

        // Ears.
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x24, 0x2D, 0x39)), null,
            new Point(headX - headRadius * 1.25, headY), headRadius * 0.32, headRadius * 0.42);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x24, 0x2D, 0x39)), null,
            new Point(headX + headRadius * 1.25, headY), headRadius * 0.32, headRadius * 0.42);

        // Front to back axis.
        var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(0x1E, 0x25, 0x2F)), 1);
        dc.DrawLine(axisPen, new Point(headX, height * 0.12), new Point(headX, height * 0.88));

        DrawBus(dc, width, height, scale, 0, width * 0.22, height * 0.62, Color.FromRgb(0x5B, 0x8C, 0xFF));
        DrawBus(dc, width, height, scale, 1, width * 0.78, height * 0.62, Color.FromRgb(0x5B, 0x8C, 0xFF));
        DrawBus(dc, width, height, scale, 2, width * 0.50, height * 0.30, Color.FromRgb(0x37, 0xD6, 0xB0));
        DrawBus(dc, width, height, scale, 3, width * 0.29, height * 0.18, Color.FromRgb(0xFF, 0x5F, 0xA2));
        DrawBus(dc, width, height, scale, 4, width * 0.71, height * 0.18, Color.FromRgb(0xFF, 0x5F, 0xA2));
        DrawBus(dc, width, height, scale, 5, width * 0.09, height * 0.44, Color.FromRgb(0xB9, 0x8C, 0xFF));
        DrawBus(dc, width, height, scale, 6, width * 0.91, height * 0.44, Color.FromRgb(0xB9, 0x8C, 0xFF));
        DrawBus(dc, width, height, scale, 7, width * 0.50, height * 0.90, Color.FromRgb(0xFF, 0xB0, 0x20));
    }

    private void DrawBus(DrawingContext dc, double width, double height, double scale, int bus,
        double x, double y, Color colour)
    {
        double level = bus < _levels.Length ? _levels[bus] : -80.0;
        double normalised = Math.Clamp((level + 60.0) / 60.0, 0.0, 1.0);
        double radius = scale * (0.022 + 0.020 * normalised);

        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)(40 + 215 * normalised), colour.R, colour.G, colour.B));

        dc.DrawEllipse(brush, new Pen(new SolidColorBrush(colour), 1), new Point(x, y), radius, radius);

        if (normalised > 0.05)
        {
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(
                (byte)(90 * normalised), colour.R, colour.G, colour.B)), 2),
                new Point(x, y), radius * 1.9, radius * 1.9);
        }
    }
}
