using System;

namespace HolyBeat.LiveGuard.Tests;

/// <summary>Deterministic signal generators and measurements for the DSP tests.</summary>
internal static class SignalHelper
{
    public const int SampleRate = 48000;

    public static float[] Sine(double frequency, double amplitude, int samples, int sampleRate = SampleRate)
    {
        var buffer = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            buffer[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * frequency * i / sampleRate));
        }

        return buffer;
    }

    public static float[] Noise(int samples, double amplitude, int seed = 12345, int sampleRate = SampleRate)
    {
        var random = new Random(seed);
        var buffer = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            buffer[i] = (float)((random.NextDouble() * 2.0 - 1.0) * amplitude);
        }

        return buffer;
    }

    /// <summary>A decaying sine burst, used to observe what happens in the tail of a transient.</summary>
    public static float[] DecayingBurst(double frequency, double amplitude, double decayMs, int samples, int sampleRate = SampleRate)
    {
        var buffer = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            double t = i / (double)sampleRate;
            double envelope = Math.Exp(-t / (decayMs / 1000.0));
            buffer[i] = (float)(amplitude * envelope * Math.Sin(2.0 * Math.PI * frequency * t));
        }

        return buffer;
    }

    /// <summary>
    /// Amplitude of the sinusoid at the given frequency (single DFT bin).
    /// When stride is used the sequence is decimated, so the effective sample rate changes.
    /// </summary>
    public static double AmplitudeAt(float[] data, double frequency, int sampleRate = SampleRate, int stride = 1, int offset = 0)
    {
        double re = 0.0;
        double im = 0.0;
        int count = 0;

        // Skipping samples keeps the original sample rate, so the phase advances with
        // the decimated index while the rate stays the same.
        for (int i = offset; i < data.Length; i += stride)
        {
            double w = 2.0 * Math.PI * frequency * count / sampleRate;
            re += data[i] * Math.Cos(w);
            im -= data[i] * Math.Sin(w);
            count++;
        }

        if (count == 0)
            return 0.0;

        return 2.0 * Math.Sqrt(re * re + im * im) / count;
    }

    public static double Rms(float[] data, int offset = 0, int count = -1)
    {
        if (count < 0)
            count = data.Length - offset;

        double sum = 0.0;
        for (int i = offset; i < offset + count; i++)
            sum += data[i] * data[i];

        return Math.Sqrt(sum / Math.Max(1, count));
    }

    public static double Peak(float[] data)
    {
        double peak = 0.0;
        foreach (float value in data)
            peak = Math.Max(peak, Math.Abs(value));
        return peak;
    }

    public static float[] ToStereo(float[] left, float[] right)
    {
        var stereo = new float[left.Length * 2];
        for (int i = 0; i < left.Length; i++)
        {
            stereo[i * 2] = left[i];
            stereo[i * 2 + 1] = right[i];
        }

        return stereo;
    }
}
