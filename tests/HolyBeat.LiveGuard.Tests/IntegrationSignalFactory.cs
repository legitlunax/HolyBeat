using System;

namespace HolyBeat.LiveGuard.Tests;

/// <summary>Shared signal generators for the integration tests.</summary>
internal static class IntegrationSignalFactory
{
    public const int SampleRate = 48000;

    public static float[] Harsh()
    {
        var random = new Random(7);
        var samples = new float[SampleRate * 4 * 2];

        for (int n = 0; n < SampleRate * 4; n++)
        {
            double t = n / (double)SampleRate;
            double burst = (t % 0.5) < 0.05 ? 1.0 : 0.15;
            double tone = 0.16 * burst * Math.Sin(2.0 * Math.PI * 4200.0 * t);
            double noise = 0.07 * burst * (random.NextDouble() * 2.0 - 1.0);
            samples[n * 2] = (float)(tone + noise);
            samples[n * 2 + 1] = (float)(tone + noise);
        }

        return samples;
    }

    public static float[] SubPressure()
    {
        var samples = new float[SampleRate * 4 * 2];

        for (int n = 0; n < SampleRate * 4; n++)
        {
            double t = n / (double)SampleRate;
            double kick = (t % 0.5) < 0.12 ? 1.0 : 0.25;
            double sub = 0.45 * kick * Math.Sin(2.0 * Math.PI * 52.0 * t);
            double mid = 0.05 * kick * Math.Sin(2.0 * Math.PI * 1200.0 * t);
            samples[n * 2] = (float)(sub + mid);
            samples[n * 2 + 1] = (float)(sub + mid);
        }

        return samples;
    }

    public static float[] Safe()
    {
        var random = new Random(11);
        var samples = new float[SampleRate * 4 * 2];
        double previous = 0.0;

        for (int n = 0; n < SampleRate * 4; n++)
        {
            double t = n / (double)SampleRate;
            double white = random.NextDouble() * 2.0 - 1.0;
            double pink = 0.6 * previous + 0.4 * white;
            previous = pink;

            double value = 0.10 * pink
                           + 0.05 * Math.Sin(2.0 * Math.PI * 220.0 * t)
                           + 0.05 * Math.Sin(2.0 * Math.PI * 660.0 * t)
                           + 0.03 * Math.Sin(2.0 * Math.PI * 1800.0 * t);

            samples[n * 2] = (float)value;
            samples[n * 2 + 1] = (float)value;
        }

        return samples;
    }
}
