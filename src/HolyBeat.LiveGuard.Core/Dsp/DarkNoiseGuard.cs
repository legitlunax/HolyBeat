using System;

namespace HolyBeat.LiveGuard.Core.Dsp;

/// <summary>
/// Multi-band quality-preserving Dark Noise Guard.
/// Tracks rapid spectral-tilt modulation and swirling stereo LFOs across three sweep
/// zones (Low-Mid 350-1500 Hz, Presence 1.5-6.5 kHz, and Air 6.5-14 kHz) relative to
/// the 250-1200 Hz harmonic anchor. When background filters sweep or warble, it smoothly
/// damps the unstable modulation and locks down swirling side energy while staying
/// transparent (0 dB) on steady musical passages.
/// </summary>
public sealed class DarkNoiseGuard
{
    private double _sampleRate = 48000.0;
    private double _strength = 0.6;
    private double _maxSuppressionDb = 5.5;
    private long _samplesProcessed;
    private long _warmupSamples;

    private BandFilter _anchorFilter = null!;
    private BandFilter _lowSweepFilter = null!;
    private BandFilter _midSweepFilter = null!;
    private BandFilter _highSweepFilter = null!;

    private EnvelopeFollower _anchorFast = null!;
    private EnvelopeFollower _anchorSlow = null!;

    private SweepBandState _lowBand = new();
    private SweepBandState _midBand = new();
    private SweepBandState _highBand = new();

    private float[] _anchorMono = Array.Empty<float>();
    private float[] _lowL = Array.Empty<float>();
    private float[] _lowR = Array.Empty<float>();
    private float[] _midL = Array.Empty<float>();
    private float[] _midR = Array.Empty<float>();
    private float[] _highL = Array.Empty<float>();
    private float[] _highR = Array.Empty<float>();

    public bool Enabled { get; set; } = true;
    public double CurrentSuppressionDb => Math.Max(-_midBand.SmoothedGainDb, Math.Max(-_lowBand.SmoothedGainDb, -_highBand.SmoothedGainDb));
    public double CurrentSideWidth => _midBand.SmoothedSideWidth;

    public void Configure(double sampleRate, double strength, double maxSuppressionDb = 5.5)
    {
        _sampleRate = Math.Max(sampleRate, 8000.0);
        _strength = Math.Clamp(strength, 0.0, 1.5);
        _maxSuppressionDb = Math.Clamp(maxSuppressionDb, 0.0, 9.0);

        _anchorFilter = new BandFilter(_sampleRate, 250.0, 1200.0, 1);
        _lowSweepFilter = new BandFilter(_sampleRate, 350.0, 1500.0, 1);
        _midSweepFilter = new BandFilter(_sampleRate, 1500.0, 6500.0, 1);

        double highTop = Math.Min(14000.0, _sampleRate * 0.45);
        _highSweepFilter = new BandFilter(_sampleRate, 6500.0, Math.Max(7500.0, highTop), 1);

        _anchorFast = new EnvelopeFollower(_sampleRate, 18.0, 90.0);
        _anchorSlow = new EnvelopeFollower(_sampleRate, 450.0, 900.0);

        _lowBand.Configure(_sampleRate);
        _midBand.Configure(_sampleRate);
        _highBand.Configure(_sampleRate);
        _samplesProcessed = 0;
        _warmupSamples = (long)(_sampleRate * 0.25);
    }

    public void Process(float[] buffer, int channels, int frames)
    {
        if (!Enabled || _strength <= 0.001 || channels < 2 || frames <= 0)
        {
            _lowBand.ResetGains();
            _midBand.ResetGains();
            _highBand.ResetGains();
            return;
        }

        EnsureScratch(frames);

        for (int i = 0; i < frames; i++)
        {
            float l = buffer[i * channels];
            float r = buffer[i * channels + 1];
            _anchorMono[i] = 0.5f * (l + r);
            _lowL[i] = l;
            _lowR[i] = r;
            _midL[i] = l;
            _midR[i] = r;
            _highL[i] = l;
            _highR[i] = r;
        }

        _anchorFilter.Process(_anchorMono, 1, 0, frames);
        _lowSweepFilter.Process(_lowL, 1, 0, frames);
        _lowSweepFilter.Process(_lowR, 1, 0, frames);
        _midSweepFilter.Process(_midL, 1, 0, frames);
        _midSweepFilter.Process(_midR, 1, 0, frames);
        _highSweepFilter.Process(_highL, 1, 0, frames);
        _highSweepFilter.Process(_highR, 1, 0, frames);

        double gainCoeff = Math.Exp(-1.0 / (0.025 * _sampleRate));
        double widthCoeff = Math.Exp(-1.0 / (0.035 * _sampleRate));

        for (int i = 0; i < frames; i++)
        {
            double aFast = _anchorFast.Process(_anchorMono[i]);
            double aSlow = _anchorSlow.Process(_anchorMono[i]);
            bool warmedUp = _samplesProcessed >= _warmupSamples && aSlow > 1e-4;
            _samplesProcessed += 1;

            double aFastDb = AudioMath.LinearToDb(aFast + 1e-6);
            double aSlowDb = AudioMath.LinearToDb(aSlow + 1e-6);

            ProcessBand(_lowBand, _lowL[i], _lowR[i], aFastDb, aSlowDb, warmedUp, 0.70, 0.85, gainCoeff, widthCoeff, out double dLowL, out double dLowR);
            ProcessBand(_midBand, _midL[i], _midR[i], aFastDb, aSlowDb, warmedUp, 1.00, 0.85, gainCoeff, widthCoeff, out double dMidL, out double dMidR);
            ProcessBand(_highBand, _highL[i], _highR[i], aFastDb, aSlowDb, warmedUp, 0.85, 0.90, gainCoeff, widthCoeff, out double dHighL, out double dHighR);

            buffer[i * channels] += (float)(dLowL + dMidL + dHighL);
            buffer[i * channels + 1] += (float)(dLowR + dMidR + dHighR);
        }
    }

    private void ProcessBand(
        SweepBandState band,
        double l,
        double r,
        double anchorFastDb,
        double anchorSlowDb,
        bool warmedUp,
        double bandWeight,
        double sensitivity,
        double gainCoeff,
        double widthCoeff,
        out double deltaL,
        out double deltaR)
    {
        double mid = 0.5 * (l + r);
        double side = 0.5 * (l - r);

        double mFast = band.MidFast.Process(mid);
        double mSlow = band.MidSlow.Process(mid);
        double sFast = band.SideFast.Process(side);
        double sSlow = band.SideSlow.Process(side);

        double combinedInstability = 0.0;
        if (warmedUp && mSlow > 1e-4)
        {
            double fastRatioDb = AudioMath.LinearToDb(mFast + 1e-6) - anchorFastDb;
            double slowRatioDb = AudioMath.LinearToDb(mSlow + 1e-6) - anchorSlowDb;

            // Detect both upward sweep surges and rapid spectral-tilt oscillation.
            double tiltDelta = Math.Max(0.0, Math.Abs(fastRatioDb - slowRatioDb) - 0.8);
            double sideMod = sSlow > 1e-4
                ? Math.Max(0.0, Math.Abs(AudioMath.LinearToDb(sFast + 1e-6) - AudioMath.LinearToDb(sSlow + 1e-6)) - 1.2)
                : 0.0;
            combinedInstability = tiltDelta + 0.55 * sideMod;
        }

        double targetSuppressDb = -Math.Min(_maxSuppressionDb * bandWeight, combinedInstability * sensitivity * _strength);
        band.SmoothedGainDb = targetSuppressDb + gainCoeff * (band.SmoothedGainDb - targetSuppressDb);
        double bandGain = AudioMath.DbToLinear(band.SmoothedGainDb);

        // Lock down swirling stereo LFO modulation when instability is active.
        double targetWidth = Math.Clamp(1.0 - (combinedInstability / 8.0) * 0.60 * _strength, 0.40, 1.0);
        band.SmoothedSideWidth = targetWidth + widthCoeff * (band.SmoothedSideWidth - targetWidth);

        double newMid = mid * bandGain;
        double newSide = side * bandGain * band.SmoothedSideWidth;

        deltaL = (newMid + newSide) - l;
        deltaR = (newMid - newSide) - r;
    }

    private void EnsureScratch(int frames)
    {
        if (_anchorMono.Length < frames)
        {
            _anchorMono = new float[frames];
            _lowL = new float[frames];
            _lowR = new float[frames];
            _midL = new float[frames];
            _midR = new float[frames];
            _highL = new float[frames];
            _highR = new float[frames];
        }
    }

    public void Reset()
    {
        _anchorFilter?.Reset();
        _lowSweepFilter?.Reset();
        _midSweepFilter?.Reset();
        _highSweepFilter?.Reset();
        _anchorFast?.Reset();
        _anchorSlow?.Reset();
        _lowBand.Reset();
        _midBand.Reset();
        _highBand.Reset();
        _samplesProcessed = 0;
    }

    private sealed class SweepBandState
    {
        public EnvelopeFollower MidFast = null!;
        public EnvelopeFollower MidSlow = null!;
        public EnvelopeFollower SideFast = null!;
        public EnvelopeFollower SideSlow = null!;
        public double SmoothedGainDb;
        public double SmoothedSideWidth = 1.0;

        public void Configure(double sampleRate)
        {
            MidFast = new EnvelopeFollower(sampleRate, 15.0, 80.0);
            MidSlow = new EnvelopeFollower(sampleRate, 450.0, 900.0);
            SideFast = new EnvelopeFollower(sampleRate, 15.0, 80.0);
            SideSlow = new EnvelopeFollower(sampleRate, 450.0, 900.0);
            ResetGains();
        }

        public void ResetGains()
        {
            SmoothedGainDb = 0.0;
            SmoothedSideWidth = 1.0;
        }

        public void Reset()
        {
            MidFast?.Reset();
            MidSlow?.Reset();
            SideFast?.Reset();
            SideSlow?.Reset();
            ResetGains();
        }
    }
}
