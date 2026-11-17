using System;

namespace HolyBeat.LiveGuard.Core.Dsp;

/// <summary>
/// The "warm tail" stage discovered in "Who's The Dreamer?".
/// Every noisy transient keeps its initial bite for a few milliseconds, then the high band
/// closes down to ~2.2 kHz so the ear hears a rounded tail instead of a metallic sting.
/// </summary>
public sealed class WarmTailFilter
{
    private const int PhaseIdle = 0;
    private const int PhaseHold = 1;
    private const int PhaseClose = 2;
    private const int PhaseReopen = 3;

    private EnvelopeFollower _detector = null!;
    private double _sampleRate = 48000.0;
    private double _previousEnvelope;
    private double _lpLeft;
    private double _lpRight;
    private int _phase;
    private int _counter;

    private int _holdSamples = 576;
    private int _closeSamples = 720;
    private int _reopenSamples = 5760;
    private double _openHz = 12000.0;
    private double _closedHz = 2200.0;
    private double _thresholdDb = -34.0;
    private double _onsetRatio = 1.8;
    private double _maxBlend = 1.0;

    public bool Enabled { get; set; } = true;

    /// <summary>Blend between the dry signal and the filtered tail (0 = dry, 1 = fully closed).</summary>
    public double CurrentBlend { get; private set; }

    /// <summary>Cutoff of the dynamic low pass in Hz.</summary>
    public double CurrentCutoffHz { get; private set; } = 12000.0;

    public void Configure(double sampleRate, double holdMs, double closeMs, double reopenMs,
        double closedHz, double thresholdDb, double onsetRatio, double maxBlend = 1.0)
    {
        _sampleRate = sampleRate;
        _holdSamples = Math.Max(1, (int)(sampleRate * holdMs / 1000.0));
        _closeSamples = Math.Max(1, (int)(sampleRate * closeMs / 1000.0));
        _reopenSamples = Math.Max(1, (int)(sampleRate * reopenMs / 1000.0));
        _closedHz = closedHz;
        _openHz = Math.Min(sampleRate * 0.45, 12000.0);
        _thresholdDb = thresholdDb;
        _onsetRatio = onsetRatio;
        _maxBlend = Math.Clamp(maxBlend, 0.0, 1.0);
        _detector = new EnvelopeFollower(sampleRate, 0.3, 25.0);
        _phase = PhaseIdle;
        _counter = 0;
        _lpLeft = 0.0;
        _lpRight = 0.0;
    }

    public void Process(float[] buffer, int channels, int frames)
    {
        if (!Enabled || frames <= 0)
            return;

        double threshold = AudioMath.DbToLinear(_thresholdDb);

        for (int i = 0; i < frames; i++)
        {
            int baseIndex = i * channels;
            double left = buffer[baseIndex];
            double right = channels > 1 ? buffer[baseIndex + 1] : left;
            double mono = 0.5 * (left + right);

            double envelope = _detector.Process(mono);

            // Onset: a sudden rise above both the absolute floor and the previous level.
            if (envelope > threshold && envelope > _previousEnvelope * _onsetRatio)
            {
                _phase = PhaseHold;
                _counter = 0;
            }

            _previousEnvelope = envelope;

            double cutoff = _openHz;
            double blend = 0.0;

            switch (_phase)
            {
                case PhaseHold:
                    cutoff = _openHz;
                    blend = 0.0;
                    _counter++;
                    if (_counter >= _holdSamples)
                    {
                        _phase = PhaseClose;
                        _counter = 0;
                    }
                    break;

                case PhaseClose:
                {
                    double t = Math.Min(1.0, _counter / (double)_closeSamples);
                    cutoff = AudioMath.Lerp(_openHz, _closedHz, t);
                    blend = t;
                    _counter++;
                    if (_counter >= _closeSamples)
                    {
                        _phase = PhaseReopen;
                        _counter = 0;
                    }
                    break;
                }

                case PhaseReopen:
                {
                    double t = Math.Min(1.0, _counter / (double)_reopenSamples);
                    cutoff = AudioMath.Lerp(_closedHz, _openHz, t);
                    blend = 1.0 - t;
                    _counter++;
                    if (_counter >= _reopenSamples)
                    {
                        _phase = PhaseIdle;
                        _counter = 0;
                        blend = 0.0;
                        cutoff = _openHz;
                    }
                    break;
                }
            }

            double coefficient = 1.0 - Math.Exp(-2.0 * Math.PI * cutoff / _sampleRate);
            _lpLeft += coefficient * (left - _lpLeft);
            _lpRight += coefficient * (right - _lpRight);

            double wetLeft = _lpLeft;
            double wetRight = _lpRight;
            double effectiveBlend = blend * _maxBlend;

            buffer[baseIndex] = (float)(left + (wetLeft - left) * effectiveBlend);
            if (channels > 1)
                buffer[baseIndex + 1] = (float)(right + (wetRight - right) * effectiveBlend);

            CurrentBlend = effectiveBlend;
            CurrentCutoffHz = cutoff;
        }
    }

    public void Reset()
    {
        _phase = PhaseIdle;
        _counter = 0;
        _lpLeft = 0.0;
        _lpRight = 0.0;
        _previousEnvelope = 0.0;
        _detector?.Reset();
    }
}

/// <summary>
/// Warm tape style saturation. The asymmetric soft clip rounds the transient corners and
/// adds low even harmonics in the 400 Hz - 700 Hz region, which is what makes a noisy hit
/// feel fuzzy instead of sharp.
/// </summary>
public sealed class SoftSaturation
{
    public bool Enabled { get; set; } = true;
    public double Drive { get; set; } = 1.35;
    public double Bias { get; set; } = 0.06;
    public double Mix { get; set; } = 0.6;

    public void Process(float[] buffer, int frames, int channels)
    {
        if (!Enabled)
            return;

        double biasOffset = Math.Tanh(Drive * Bias);

        for (int i = 0; i < frames; i++)
        {
            int baseIndex = i * channels;
            for (int c = 0; c < channels; c++)
            {
                double x = buffer[baseIndex + c];

                // Dividing by the drive keeps the small signal slope at unity, so the
                // saturation rounds the peaks and warms them without raising the level.
                double shaped = (Math.Tanh(Drive * (x + Bias)) - biasOffset) / Drive;
                buffer[baseIndex + c] = (float)(x + (shaped - x) * Mix);
            }
        }
    }
}

/// <summary>
/// Very short, dark room reflection. It catches the end of each noisy hit and blends it
/// into the mid bed instead of letting it die in a dry digital void.
/// </summary>
public sealed class ShortRoom
{
    private sealed class Comb
    {
        private readonly float[] _buffer;
        private readonly float _feedback;
        private int _index;

        public Comb(int delaySamples, float feedback)
        {
            _buffer = new float[delaySamples];
            _feedback = feedback;
        }

        public float Process(float x)
        {
            float y = _buffer[_index];
            _buffer[_index] = x + y * _feedback;
            _index = (_index + 1) % _buffer.Length;
            return y;
        }
    }

    private sealed class AllPass
    {
        private readonly float[] _buffer;
        private readonly float _feedback;
        private int _index;

        public AllPass(int delaySamples, float feedback)
        {
            _buffer = new float[delaySamples];
            _feedback = feedback;
        }

        public float Process(float x)
        {
            float y = _buffer[_index];
            _buffer[_index] = x + y * _feedback;
            _index = (_index + 1) % _buffer.Length;
            return y - x;
        }
    }

    private Comb[] _combs = Array.Empty<Comb>();
    private AllPass[] _allPasses = Array.Empty<AllPass>();
    private Biquad? _darkness;
    private int _preDelaySamples;
    private int _preDelayIndex;
    private float[] _preDelay = Array.Empty<float>();

    public bool Enabled { get; set; } = true;
    public double Mix { get; set; } = 0.08;

    public void Configure(double sampleRate, double decayMs, double mix, double darknessHz = 3200.0, double preDelayMs = 8.0)
    {
        Mix = mix;
        double scale = sampleRate / 48000.0;
        int[] combDelays = { 1237, 1481, 1699, 1877 };
        int[] allPassDelays = { 556, 441 };
        double decaySeconds = Math.Max(decayMs, 20.0) / 1000.0;

        _combs = combDelays.Select(d =>
        {
            int delay = Math.Max(16, (int)(d * scale));
            double delaySeconds = delay / sampleRate;
            float feedback = (float)Math.Pow(10.0, -3.0 * delaySeconds / decaySeconds);
            return new Comb(delay, Math.Min(feedback, 0.92f));
        }).ToArray();

        _allPasses = allPassDelays.Select(d =>
        {
            int delay = Math.Max(16, (int)(d * scale));
            return new AllPass(delay, 0.5f);
        }).ToArray();

        _darkness = Biquad.Create(BiquadKind.LowPass, sampleRate, darknessHz, 0.7071);
        _preDelaySamples = Math.Max(1, (int)(sampleRate * preDelayMs / 1000.0));
        _preDelay = new float[_preDelaySamples];
        _preDelayIndex = 0;
    }

    public void Process(float[] buffer, int frames, int channels)
    {
        if (!Enabled || frames <= 0 || Mix <= 0.0)
            return;

        for (int i = 0; i < frames; i++)
        {
            int baseIndex = i * channels;
            double mono = 0.5 * (buffer[baseIndex] + (channels > 1 ? buffer[baseIndex + 1] : buffer[baseIndex]));

            float delayed = _preDelay[_preDelayIndex];
            _preDelay[_preDelayIndex] = (float)mono;
            _preDelayIndex = (_preDelayIndex + 1) % _preDelaySamples;

            float wet = delayed;
            foreach (var comb in _combs)
                wet += comb.Process(delayed) * 0.25f;

            foreach (var allPass in _allPasses)
                wet = allPass.Process(wet);

            wet = (float)_darkness!.ProcessChannel(wet, 0);

            for (int c = 0; c < channels; c++)
                buffer[baseIndex + c] += (float)(wet * Mix);
        }
    }
}
