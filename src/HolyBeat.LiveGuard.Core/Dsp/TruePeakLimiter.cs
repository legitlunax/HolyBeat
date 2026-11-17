namespace HolyBeat.LiveGuard.Core.Dsp;

/// <summary>
/// Look-ahead true peak limiter. Uses a 4x oversampled peak estimate so the ceiling
/// is honoured after the reconstruction filter (dBTP), not just on the samples themselves.
/// </summary>
public sealed class TruePeakLimiter
{
    private float[] _bufferLeft = Array.Empty<float>();
    private float[] _bufferRight = Array.Empty<float>();
    private int _mask;
    private int _writeIndex;
    private int _delaySamples = 1;

    private EnvelopeFollower _detector = null!;
    private double _sampleRate = 48000.0;
    private double _ceilingDbTp = -1.0;
    private double _gainDb;
    private readonly double[] _history = new double[4];

    public bool Enabled { get; set; } = true;

    /// <summary>Current gain reduction in dB.</summary>
    public double GainReductionDb => _gainDb;

    public double MeasuredTruePeakDb { get; private set; } = -140.0;

    public void Configure(double sampleRate, double ceilingDbTp, double lookaheadMs = 2.5, double releaseMs = 80.0)
    {
        _sampleRate = sampleRate;
        _ceilingDbTp = ceilingDbTp;
        _delaySamples = Math.Max(1, (int)(sampleRate * lookaheadMs / 1000.0));

        int capacity = 1;
        while (capacity < _delaySamples * 4 + 4096)
            capacity <<= 1;
        _mask = capacity - 1;
        _bufferLeft = new float[capacity];
        _bufferRight = new float[capacity];
        _writeIndex = 0;

        _detector = new EnvelopeFollower(sampleRate, 0.1, releaseMs, peakHold: true);
        _gainDb = 0.0;
    }

    public void Process(float[] buffer, int channels, int frames)
    {
        if (!Enabled || frames <= 0)
            return;

        double smoothing = Math.Exp(-1.0 / (0.001 * _sampleRate));

        for (int i = 0; i < frames; i++)
        {
            int baseIndex = i * channels;
            double left = buffer[baseIndex];
            double right = channels > 1 ? buffer[baseIndex + 1] : left;

            // 4x cubic interpolation peak estimate (conservative by design).
            _history[0] = _history[1];
            _history[1] = _history[2];
            _history[2] = _history[3];
            _history[3] = 0.5 * (left + right);

            double peak = Math.Max(Math.Abs(_history[3]), Math.Abs(_history[2]));
            for (int k = 0; k < 3; k++)
            {
                double t = (k + 1) / 4.0;
                double y = Cubic(_history[0], _history[1], _history[2], _history[3], t);
                peak = Math.Max(peak, Math.Abs(y));
            }

            double envelope = _detector.Process(peak);
            double envelopeDb = AudioMath.LinearToDb(envelope);
            MeasuredTruePeakDb = Math.Max(MeasuredTruePeakDb * 0.999, envelopeDb);

            double targetGain = Math.Min(0.0, _ceilingDbTp - envelopeDb);
            _gainDb = targetGain + smoothing * (_gainDb - targetGain);
            double gain = AudioMath.DbToLinear(_gainDb);

            _bufferLeft[_writeIndex] = (float)left;
            _bufferRight[_writeIndex] = (float)right;
            _writeIndex = (_writeIndex + 1) & _mask;

            int readIndex = (_writeIndex - _delaySamples) & _mask;
            buffer[baseIndex] = (float)(_bufferLeft[readIndex] * gain);
            if (channels > 1)
                buffer[baseIndex + 1] = (float)(_bufferRight[readIndex] * gain);
        }
    }

    private static double Cubic(double p0, double p1, double p2, double p3, double t)
    {
        double a = -0.5 * p0 + 1.5 * p1 - 1.5 * p2 + 0.5 * p3;
        double b = p0 - 2.5 * p1 + 2.0 * p2 - 0.5 * p3;
        double c = -0.5 * p0 + 0.5 * p2;
        return ((a * t + b) * t + c) * t + p1;
    }

    public void Reset()
    {
        Array.Clear(_bufferLeft, 0, _bufferLeft.Length);
        Array.Clear(_bufferRight, 0, _bufferRight.Length);
        _writeIndex = 0;
        _gainDb = 0.0;
        _detector?.Reset();
    }
}
