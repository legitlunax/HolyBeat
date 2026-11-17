namespace HolyBeat.LiveGuard.Core.Dsp;

/// <summary>
/// Stage 4 - stereo field glue.
/// Everything below the crossover is mono summed so the sub content is identical in both
/// channels, and the side information above the narrowing frequency is scaled down so
/// bright layers stay anchored to the vocal instead of floating away from it
/// (the "Dual-Layer Split" failure mode).
/// </summary>
public sealed class MidSideController
{
    private Biquad[] _monoLow = Array.Empty<Biquad>();
    private Biquad[] _highLeft = Array.Empty<Biquad>();
    private Biquad[] _highRight = Array.Empty<Biquad>();
    private Biquad? _sideHigh;

    public bool MonoBassEnabled { get; set; } = true;
    public bool HighFrequencyNarrowingEnabled { get; set; } = true;

    /// <summary>Width multiplier applied to the side signal above the narrowing frequency (0.7 = 30% narrower).</summary>
    public double HighFrequencyWidth { get; set; } = 0.7;

    public double MonoBassFrequency { get; private set; } = 120.0;
    public double NarrowingFrequency { get; private set; } = 4000.0;

    public void Configure(double sampleRate, double monoBassHz, double narrowingHz, double highFrequencyWidth)
    {
        MonoBassFrequency = monoBassHz;
        NarrowingFrequency = narrowingHz;
        HighFrequencyWidth = highFrequencyWidth;

        // Two cascaded Butterworth sections give a 4th order crossover.
        _monoLow = new[]
        {
            Biquad.Create(BiquadKind.LowPass, sampleRate, monoBassHz, 0.7071),
            Biquad.Create(BiquadKind.LowPass, sampleRate, monoBassHz, 0.7071)
        };

        _highLeft = new[]
        {
            Biquad.Create(BiquadKind.HighPass, sampleRate, monoBassHz, 0.7071),
            Biquad.Create(BiquadKind.HighPass, sampleRate, monoBassHz, 0.7071)
        };

        _highRight = new[]
        {
            Biquad.Create(BiquadKind.HighPass, sampleRate, monoBassHz, 0.7071),
            Biquad.Create(BiquadKind.HighPass, sampleRate, monoBassHz, 0.7071)
        };

        _sideHigh = Biquad.Create(BiquadKind.HighPass, sampleRate, narrowingHz, 0.7071);
    }

    public void Process(float[] buffer, int channels, int frames)
    {
        if (channels < 2 || frames <= 0)
            return;

        for (int i = 0; i < frames; i++)
        {
            int baseIndex = i * channels;
            double left = buffer[baseIndex];
            double right = buffer[baseIndex + 1];

            if (MonoBassEnabled && _monoLow.Length > 0)
            {
                // Shared low band plus complementary high pass on both channels:
                // below the crossover both channels carry exactly the same signal.
                double mono = 0.5 * (left + right);
                double monoLow = mono;
                foreach (var section in _monoLow)
                    monoLow = section.ProcessChannel(monoLow, 0);

                double highLeft = left;
                foreach (var section in _highLeft)
                    highLeft = section.ProcessChannel(highLeft, 0);

                double highRight = right;
                foreach (var section in _highRight)
                    highRight = section.ProcessChannel(highRight, 0);

                left = highLeft + monoLow;
                right = highRight + monoLow;
            }

            if (HighFrequencyNarrowingEnabled && _sideHigh is not null)
            {
                double mid = 0.5 * (left + right);
                double side = 0.5 * (left - right);
                double sideHigh = _sideHigh.ProcessChannel(side, 0);
                side += sideHigh * (HighFrequencyWidth - 1.0);

                left = mid + side;
                right = mid - side;
            }

            buffer[baseIndex] = (float)left;
            buffer[baseIndex + 1] = (float)right;
        }
    }

    public void Reset()
    {
        foreach (var section in _monoLow) section.Reset();
        foreach (var section in _highLeft) section.Reset();
        foreach (var section in _highRight) section.Reset();
        _sideHigh?.Reset();
    }
}
