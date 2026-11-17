using System;
using System.Collections.Generic;

namespace HolyBeat.LiveGuard.Core.Dsp;

/// <summary>Filter families supported by <see cref="Biquad"/> (RBJ audio-EQ cookbook).</summary>
public enum BiquadKind
{
    LowPass,
    HighPass,
    BandPass,
    Notch,
    Peaking,
    LowShelf,
    HighShelf
}

/// <summary>Small conversion helpers shared by the DSP stages.</summary>
public static class AudioMath
{
    public const double Epsilon = 1e-12;

    public static double DbToLinear(double db) => Math.Pow(10.0, db / 20.0);
    public static double LinearToDb(double linear) => 20.0 * Math.Log10(Math.Max(linear, Epsilon));
    public static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;
    public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
}

/// <summary>
/// Direct-form II transposed biquad with independent state per channel.
/// Kept dependency free so the whole DSP core can be unit tested without an audio device.
/// </summary>
public sealed class Biquad
{
    private double _b0, _b1, _b2, _a1, _a2;
    private readonly double[] _z1 = new double[2];
    private readonly double[] _z2 = new double[2];

    public BiquadKind Kind { get; private set; }
    public double Frequency { get; private set; }
    public double Q { get; private set; }

    private Biquad()
    {
    }

    public static Biquad Create(BiquadKind kind, double sampleRate, double frequency, double q, double gainDb = 0.0)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (frequency <= 0 || frequency >= sampleRate * 0.5)
            throw new ArgumentOutOfRangeException(nameof(frequency), "Frequency must be inside the Nyquist range.");

        var b = new Biquad { Kind = kind, Frequency = frequency, Q = q };

        double w0 = 2.0 * Math.PI * frequency / sampleRate;
        double cosw = Math.Cos(w0);
        double sinw = Math.Sin(w0);
        double alpha = sinw / (2.0 * Math.Max(q, 0.0001));
        double a0;

        switch (kind)
        {
            case BiquadKind.LowPass:
                b._b0 = (1.0 - cosw) / 2.0;
                b._b1 = 1.0 - cosw;
                b._b2 = (1.0 - cosw) / 2.0;
                a0 = 1.0 + alpha;
                b._a1 = -2.0 * cosw;
                b._a2 = 1.0 - alpha;
                break;

            case BiquadKind.HighPass:
                b._b0 = (1.0 + cosw) / 2.0;
                b._b1 = -(1.0 + cosw);
                b._b2 = (1.0 + cosw) / 2.0;
                a0 = 1.0 + alpha;
                b._a1 = -2.0 * cosw;
                b._a2 = 1.0 - alpha;
                break;

            case BiquadKind.BandPass:
                b._b0 = alpha;
                b._b1 = 0.0;
                b._b2 = -alpha;
                a0 = 1.0 + alpha;
                b._a1 = -2.0 * cosw;
                b._a2 = 1.0 - alpha;
                break;

            case BiquadKind.Notch:
                b._b0 = 1.0;
                b._b1 = -2.0 * cosw;
                b._b2 = 1.0;
                a0 = 1.0 + alpha;
                b._a1 = -2.0 * cosw;
                b._a2 = 1.0 - alpha;
                break;

            case BiquadKind.Peaking:
            {
                double a = Math.Pow(10.0, gainDb / 40.0);
                b._b0 = 1.0 + alpha * a;
                b._b1 = -2.0 * cosw;
                b._b2 = 1.0 - alpha * a;
                a0 = 1.0 + alpha / a;
                b._a1 = -2.0 * cosw;
                b._a2 = 1.0 - alpha / a;
                break;
            }

            case BiquadKind.LowShelf:
            {
                double a = Math.Pow(10.0, gainDb / 40.0);
                double beta = 2.0 * Math.Sqrt(a) * alpha;
                b._b0 = a * ((a + 1.0) - (a - 1.0) * cosw + beta);
                b._b1 = 2.0 * a * ((a - 1.0) - (a + 1.0) * cosw);
                b._b2 = a * ((a + 1.0) - (a - 1.0) * cosw - beta);
                a0 = (a + 1.0) + (a - 1.0) * cosw + beta;
                b._a1 = -2.0 * ((a - 1.0) + (a + 1.0) * cosw);
                b._a2 = (a + 1.0) + (a - 1.0) * cosw - beta;
                break;
            }

            case BiquadKind.HighShelf:
            {
                double a = Math.Pow(10.0, gainDb / 40.0);
                double beta = 2.0 * Math.Sqrt(a) * alpha;
                b._b0 = a * ((a + 1.0) + (a - 1.0) * cosw + beta);
                b._b1 = -2.0 * a * ((a - 1.0) + (a + 1.0) * cosw);
                b._b2 = a * ((a + 1.0) + (a - 1.0) * cosw - beta);
                a0 = (a + 1.0) - (a - 1.0) * cosw + beta;
                b._a1 = 2.0 * ((a - 1.0) - (a + 1.0) * cosw);
                b._a2 = (a + 1.0) - (a - 1.0) * cosw - beta;
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        b._b0 /= a0;
        b._b1 /= a0;
        b._b2 /= a0;
        b._a1 /= a0;
        b._a2 /= a0;
        return b;
    }

    /// <summary>Processes an interleaved buffer in place.</summary>
    public void Process(float[] buffer, int channels, int offset, int frames)
    {
        for (int f = 0; f < frames; f++)
        {
            int baseIndex = offset + f * channels;
            for (int c = 0; c < channels; c++)
            {
                double x = buffer[baseIndex + c];
                double y = _b0 * x + _z1[c];
                _z1[c] = _b1 * x - _a1 * y + _z2[c];
                _z2[c] = _b2 * x - _a2 * y;
                buffer[baseIndex + c] = (float)y;
            }
        }
    }

    /// <summary>Processes one sample of a single channel.</summary>
    public double ProcessChannel(double x, int channel)
    {
        double y = _b0 * x + _z1[channel];
        _z1[channel] = _b1 * x - _a1 * y + _z2[channel];
        _z2[channel] = _b2 * x - _a2 * y;
        return y;
    }

    public void Reset()
    {
        Array.Clear(_z1, 0, _z1.Length);
        Array.Clear(_z2, 0, _z2.Length);
    }
}

/// <summary>
/// Band extraction filter: cascaded high-pass and low-pass sections.
/// A zero low edge means "unbounded low", a zero high edge means "unbounded high".
/// </summary>
public sealed class BandFilter
{
    private readonly List<Biquad> _stages = new List<Biquad>();
    private readonly double _sampleRate;
    private readonly double _lowHz;
    private readonly double _highHz;

    public BandFilter(double sampleRate, double lowHz, double highHz, int orderPerEdge = 2)
    {
        _sampleRate = sampleRate;
        _lowHz = lowHz;
        _highHz = highHz;

        int edges = Math.Max(1, orderPerEdge);
        if (lowHz > 0)
        {
            for (int i = 0; i < edges; i++)
                _stages.Add(Biquad.Create(BiquadKind.HighPass, sampleRate, lowHz, 0.70710678));
        }

        if (highHz > 0)
        {
            for (int i = 0; i < edges; i++)
                _stages.Add(Biquad.Create(BiquadKind.LowPass, sampleRate, highHz, 0.70710678));
        }
    }

    public void Process(float[] buffer, int channels, int offset, int frames)
    {
        foreach (var stage in _stages)
            stage.Process(buffer, channels, offset, frames);
    }

    public void Reset()
    {
        foreach (var stage in _stages)
            stage.Reset();
    }

    public double LowHz => _lowHz;
    public double HighHz => _highHz;
}
