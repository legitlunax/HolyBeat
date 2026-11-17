using System;

namespace HolyBeat.LiveGuard.Core.Dsp;

/// <summary>
/// Attack/release envelope follower. Used as the detector in every dynamic stage
/// so that threshold behaviour matches the timings stated in the HolyBeat spec.
/// </summary>
public sealed class EnvelopeFollower
{
    private double _sampleRate;
    private double _attackCoefficient = 0.0;
    private double _releaseCoefficient = 0.0;
    private double _envelope;

    public EnvelopeFollower(double sampleRate, double attackMs, double releaseMs, bool rms = false, bool peakHold = false)
    {
        Rms = rms;
        PeakHold = peakHold;
        SetTiming(sampleRate, attackMs, releaseMs);
    }

    public bool Rms { get; }

    /// <summary>When true the detector jumps instantly to a new peak and only decays afterwards.</summary>
    public bool PeakHold { get; }

    public double Envelope => _envelope;

    public void SetTiming(double sampleRate, double attackMs, double releaseMs)
    {
        _sampleRate = Math.Max(sampleRate, 1000.0);
        _attackCoefficient = Coefficient(attackMs);
        _releaseCoefficient = Coefficient(releaseMs);
    }

    private double Coefficient(double milliseconds)
    {
        double seconds = Math.Max(milliseconds, 0.01) / 1000.0;
        return Math.Exp(-1.0 / (seconds * _sampleRate));
    }

    public double Process(double x)
    {
        double magnitude = Rms ? x * x : Math.Abs(x);

        if (PeakHold)
        {
            if (magnitude >= _envelope)
                _envelope = magnitude;
            else
                _envelope = magnitude + _releaseCoefficient * (_envelope - magnitude);
            return _envelope;
        }

        double coefficient = magnitude > _envelope ? _attackCoefficient : _releaseCoefficient;
        _envelope = magnitude + coefficient * (_envelope - magnitude);
        return _envelope;
    }

    public void Reset() => _envelope = 0.0;
}

/// <summary>Direction of the dynamic gain applied by a <see cref="BandGainStage"/>.</summary>
public enum DynamicMode
{
    /// <summary>Attenuates the band when it exceeds the threshold (de-harshing, sub tightening).</summary>
    Compress,

    /// <summary>Raises the band when it falls into a hole (anti-pumping recovery).</summary>
    Expand
}

/// <summary>
/// Base class for stages that change the level of one frequency band only.
/// The band is extracted, measured and then re-inserted with a smoothed gain,
/// which leaves the rest of the spectrum untouched.
/// </summary>
public abstract class BandGainStage
{
    protected BandFilter Filter = null!;
    protected double SampleRate = 48000.0;
    protected float[] BandLeft = Array.Empty<float>();
    protected float[] BandRight = Array.Empty<float>();
    protected double SmoothedGainDb;

    public bool Enabled { get; set; } = true;

    protected void ConfigureFilter(double sampleRate, double lowHz, double highHz, int orderPerEdge)
    {
        SampleRate = sampleRate;
        Filter = new BandFilter(sampleRate, lowHz, highHz, orderPerEdge);
        SmoothedGainDb = 0.0;
    }

    protected void EnsureScratch(int frames)
    {
        if (BandLeft.Length < frames)
        {
            BandLeft = new float[frames];
            BandRight = new float[frames];
        }
    }

    /// <summary>Computes the target gain in dB for the measured band level.</summary>
    protected abstract double ComputeTargetGainDb(double levelDb, double referenceDb);

    public virtual void Process(float[] buffer, int channels, int frames, double smoothingMs)
    {
        if (!Enabled || channels < 2 || frames <= 0)
            return;

        EnsureScratch(frames);

        for (int i = 0; i < frames; i++)
        {
            BandLeft[i] = buffer[i * channels];
            BandRight[i] = buffer[i * channels + 1];
        }

        Filter.Process(BandLeft, 1, 0, frames);
        Filter.Process(BandRight, 1, 0, frames);

        double smoothingCoefficient = Math.Exp(-1.0 / (Math.Max(smoothingMs, 0.1) / 1000.0 * SampleRate));

        for (int i = 0; i < frames; i++)
        {
            double mono = 0.5 * (BandLeft[i] + BandRight[i]);
            double levelDb = AudioMath.LinearToDb(Math.Abs(mono));
            double referenceDb = ReferenceLevelDb(i);
            double targetDb = ComputeTargetGainDb(levelDb, referenceDb);

            SmoothedGainDb = targetDb + smoothingCoefficient * (SmoothedGainDb - targetDb);
            double gain = AudioMath.DbToLinear(SmoothedGainDb);
            double delta = gain - 1.0;

            buffer[i * channels] += (float)(BandLeft[i] * delta);
            buffer[i * channels + 1] += (float)(BandRight[i] * delta);
        }
    }

    /// <summary>Optional slow reference level (used by the expander to find holes).</summary>
    protected virtual double ReferenceLevelDb(int frameIndex) => double.NegativeInfinity;

    public virtual void Reset()
    {
        SmoothedGainDb = 0.0;
        Filter?.Reset();
    }
}

/// <summary>
/// Stage 2 - dynamic resonance suppression. Attenuates the 2.8 kHz - 5.5 kHz pain band
/// only when the transient energy crosses the threshold (max -6 dB, ratio 3:1).
/// </summary>
public sealed class BandCompressor : BandGainStage
{
    private double _thresholdDb = -22.0;
    private double _ratio = 3.0;
    private double _maxReductionDb = 6.0;

    public void Configure(double sampleRate, double lowHz, double highHz, int orderPerEdge,
        double attackMs, double releaseMs, double thresholdDb, double ratio, double maxReductionDb)
    {
        ConfigureFilter(sampleRate, lowHz, highHz, orderPerEdge);
        Detector = new EnvelopeFollower(sampleRate, attackMs, releaseMs);
        _thresholdDb = thresholdDb;
        _ratio = Math.Max(1.0, ratio);
        _maxReductionDb = maxReductionDb;
    }

    private EnvelopeFollower Detector = null!;

    public double LastGainReductionDb => SmoothedGainDb;

    protected override double ComputeTargetGainDb(double levelDb, double referenceDb)
    {
        double over = levelDb - _thresholdDb;
        if (over <= 0.0)
            return 0.0;

        double reduction = over * (1.0 - 1.0 / _ratio);
        return -Math.Min(_maxReductionDb, reduction);
    }

    public override void Process(float[] buffer, int channels, int frames, double smoothingMs)
    {
        if (!Enabled || channels < 2 || frames <= 0)
            return;

        EnsureScratch(frames);
        for (int i = 0; i < frames; i++)
        {
            BandLeft[i] = buffer[i * channels];
            BandRight[i] = buffer[i * channels + 1];
        }

        Filter.Process(BandLeft, 1, 0, frames);
        Filter.Process(BandRight, 1, 0, frames);

        double smoothingCoefficient = Math.Exp(-1.0 / (Math.Max(smoothingMs, 0.1) / 1000.0 * SampleRate));

        for (int i = 0; i < frames; i++)
        {
            double mono = 0.5 * (BandLeft[i] + BandRight[i]);
            double env = Detector.Process(mono);
            double levelDb = AudioMath.LinearToDb(env);
            double targetDb = ComputeTargetGainDb(levelDb, double.NegativeInfinity);

            SmoothedGainDb = targetDb + smoothingCoefficient * (SmoothedGainDb - targetDb);
            double delta = AudioMath.DbToLinear(SmoothedGainDb) - 1.0;

            buffer[i * channels] += (float)(BandLeft[i] * delta);
            buffer[i * channels + 1] += (float)(BandRight[i] * delta);
        }
    }
}

/// <summary>
/// Stage 3 - upward expansion / envelope smoothing. Detects the volume vacuums left by
/// kick-triggered sidechain pumping and lifts the 100 Hz - 1.5 kHz band back up.
/// </summary>
public sealed class BandExpander : BandGainStage
{
    private EnvelopeFollower _fast = null!;
    private EnvelopeFollower _slow = null!;
    private double _maxBoostDb = 4.5;
    private double _sensitivity = 0.5;
    private double _minimumDipDb = 0.8;

    public void Configure(double sampleRate, double lowHz, double highHz, int orderPerEdge,
        double attackMs, double releaseMs, double slowReleaseMs, double maxBoostDb, double sensitivity)
    {
        ConfigureFilter(sampleRate, lowHz, highHz, orderPerEdge);
        _fast = new EnvelopeFollower(sampleRate, attackMs, releaseMs);
        _slow = new EnvelopeFollower(sampleRate, 30.0, slowReleaseMs);
        _maxBoostDb = maxBoostDb;
        _sensitivity = sensitivity;
    }

    public double LastBoostDb => SmoothedGainDb;

    protected override double ComputeTargetGainDb(double levelDb, double referenceDb)
    {
        if (double.IsNegativeInfinity(referenceDb))
            return 0.0;

        double dipDb = referenceDb - levelDb;
        if (dipDb <= _minimumDipDb)
            return 0.0;

        return Math.Min(_maxBoostDb, dipDb * _sensitivity);
    }

    public override void Process(float[] buffer, int channels, int frames, double smoothingMs)
    {
        if (!Enabled || channels < 2 || frames <= 0)
            return;

        EnsureScratch(frames);
        for (int i = 0; i < frames; i++)
        {
            BandLeft[i] = buffer[i * channels];
            BandRight[i] = buffer[i * channels + 1];
        }

        Filter.Process(BandLeft, 1, 0, frames);
        Filter.Process(BandRight, 1, 0, frames);

        double smoothingCoefficient = Math.Exp(-1.0 / (Math.Max(smoothingMs, 0.1) / 1000.0 * SampleRate));

        for (int i = 0; i < frames; i++)
        {
            double mono = 0.5 * (BandLeft[i] + BandRight[i]);
            double fast = _fast.Process(mono);
            double slow = _slow.Process(mono);

            double levelDb = AudioMath.LinearToDb(fast);
            double referenceDb = AudioMath.LinearToDb(slow);
            double targetDb = ComputeTargetGainDb(levelDb, referenceDb);

            SmoothedGainDb = targetDb + smoothingCoefficient * (SmoothedGainDb - targetDb);
            double delta = AudioMath.DbToLinear(SmoothedGainDb) - 1.0;

            buffer[i * channels] += (float)(BandLeft[i] * delta);
            buffer[i * channels + 1] += (float)(BandRight[i] * delta);
        }
    }
}
