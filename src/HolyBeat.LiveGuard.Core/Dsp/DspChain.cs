using System;
using System.Collections.Generic;
using HolyBeat.LiveGuard.Core.Analysis;
using HolyBeat.LiveGuard.Core.Dsp;
using HolyBeat.LiveGuard.Core.Profiles;

namespace HolyBeat.LiveGuard.Core.Dsp;

/// <summary>
/// The complete HolyBeat processing chain, in the order established by the research:
/// safety EQ -> dynamic de-harshing -> anti-pumping expansion -> sub tightening ->
/// stereo glue -> warm bed -> warm tail -> saturation -> room -> loudness target -> true peak limit.
/// It also hosts the analyser that classifies the incoming material.
/// </summary>
public sealed class DspChain
{
    private readonly object _sync = new();

    private readonly List<Biquad> _highPassFilters = new List<Biquad>();
    private Biquad? _midGlue;
    private Biquad? _highShelf;

    private readonly BandCompressor _deHarsh = new BandCompressor();
    private readonly BandExpander _expander = new BandExpander();
    private readonly BandCompressor _subTighter = new BandCompressor();
    private readonly MidSideController _midSide = new MidSideController();
    private readonly DarkNoiseGuard _darkNoiseGuard = new DarkNoiseGuard();
    private readonly VirtualStage _stage = new VirtualStage();
    private readonly WarmTailFilter _warmTail = new WarmTailFilter();
    private readonly SoftSaturation _saturation = new SoftSaturation();
    private readonly ShortRoom _room = new ShortRoom();
    private readonly TruePeakLimiter _limiter = new TruePeakLimiter();
    private readonly LoudnessMeter _processedLoudness = new LoudnessMeter();

    private readonly SoftSaturation _bedSaturation = new SoftSaturation { Drive = 1.5, Mix = 1.0 };
    private BandFilter? _bedBand;

    private readonly SpectrumAnalyzer _sourceAnalyzer = new SpectrumAnalyzer();
    private readonly SpectrumAnalyzer _outputAnalyzer = new SpectrumAnalyzer();
    private readonly ProfileDetector _detector = new ProfileDetector();

    private float[] _scratch = Array.Empty<float>();
    private float[] _bedScratch = Array.Empty<float>();

    private SafetyProfile _profile;
    private int _sampleRate = 48000;
    private int _configuredSampleRate;
    private bool _bypass;
    private double _loudnessGainDb;

    public DspChain(SafetyProfile profile, int sampleRate = 48000)
    {
        _profile = profile;
        _sampleRate = sampleRate;
        Configure(sampleRate);
    }

    public SafetyProfile Profile => _profile;

    public bool Bypass
    {
        get { lock (_sync) return _bypass; }
        set { lock (_sync) _bypass = value; }
    }

    public int SampleRate => _sampleRate;

    /// <summary>Live classification of the incoming audio.</summary>
    public DetectionResult Detection { get; private set; } = new DetectionResult();

    public AnalysisSnapshot SourceAnalysis { get; private set; } = new AnalysisSnapshot();
    public AnalysisSnapshot OutputAnalysis { get; private set; } = new AnalysisSnapshot();

    public double LimiterGainReductionDb => _limiter.GainReductionDb;
    public double DeHarshGainDb => _deHarsh.LastGainReductionDb;
    public double ExpansionBoostDb => _expander.LastBoostDb;
    public double SubGainDb => _subTighter.LastGainReductionDb;
    public double WarmTailBlend => _warmTail.CurrentBlend;
    public double WarmTailCutoffHz => _warmTail.CurrentCutoffHz;
    public double LoudnessCorrectionDb => _loudnessGainDb;

    /// <summary>Live level of every bus of the virtual stage.</summary>
    public double[] StageBusLevels => _stage.BusLevels;

    /// <summary>Live sweep suppression applied by the Dark Noise Guard in dB.</summary>
    public double DarkNoiseSuppressionDb => _darkNoiseGuard.CurrentSuppressionDb;

    public void ApplyProfile(SafetyProfile profile)
    {
        lock (_sync)
        {
            _profile = profile;
        }

        Configure(_sampleRate);
    }

    public void SetSampleRate(int sampleRate)
    {
        if (sampleRate <= 0 || sampleRate == _sampleRate)
            return;

        _sampleRate = sampleRate;
        Configure(sampleRate);
    }

    private void Configure(int sampleRate)
    {
        SafetyProfile profile;
        lock (_sync)
        {
            profile = _profile;
        }

        _configuredSampleRate = sampleRate;

        _highPassFilters.Clear();
        if (profile.EnableHighPass)
        {
            int sections = Math.Max(1, profile.HighPassOrder / 2);
            for (int i = 0; i < sections; i++)
                _highPassFilters.Add(Biquad.Create(BiquadKind.HighPass, sampleRate, profile.HighPassHz, 0.7071));
        }

        _midGlue = profile.EnableMidGlue
            ? Biquad.Create(BiquadKind.Peaking, sampleRate, profile.MidGlueHz, profile.MidGlueQ, profile.MidGlueDb)
            : null;

        _highShelf = profile.EnableHighShelf
            ? Biquad.Create(BiquadKind.HighShelf, sampleRate, profile.HighShelfHz, 0.7071, profile.HighShelfDb)
            : null;

        _deHarsh.Enabled = profile.EnableDeHarsh;
        _deHarsh.Configure(sampleRate, profile.DeHarshLowHz, profile.DeHarshHighHz, 2,
            profile.DeHarshAttackMs, profile.DeHarshReleaseMs, profile.DeHarshThresholdDb,
            profile.DeHarshRatio, profile.DeHarshMaxReductionDb);

        _expander.Enabled = profile.EnableExpansion;
        _expander.Configure(sampleRate, profile.ExpansionLowHz, profile.ExpansionHighHz, 2,
            profile.ExpansionAttackMs, profile.ExpansionReleaseMs, profile.ExpansionSlowReleaseMs,
            profile.ExpansionMaxBoostDb, profile.ExpansionSensitivity);

        _subTighter.Enabled = profile.EnableSubTightening;
        _subTighter.Configure(sampleRate, profile.SubLowHz, profile.SubHighHz, 2,
            profile.SubAttackMs, profile.SubReleaseMs, profile.SubThresholdDb,
            profile.SubRatio, profile.SubMaxReductionDb);

        _midSide.MonoBassEnabled = profile.EnableMonoBass;
        _midSide.HighFrequencyNarrowingEnabled = profile.EnableHighFrequencyNarrowing;
        _midSide.Configure(sampleRate, profile.MonoBassHz, profile.NarrowingHz, profile.HighFrequencyWidth);

        _darkNoiseGuard.Enabled = profile.EnableDarkNoiseGuard;
        _darkNoiseGuard.Configure(sampleRate, profile.DarkNoiseGuardStrength, profile.DarkNoiseGuardMaxDb);

        _stage.Enabled = profile.EnableVirtualStage;
        _stage.Configure(sampleRate, profile.StageWidth, profile.StageHeight, profile.StageCentreFocus,
            profile.StageLfeMix, profile.StageSurroundMix);

        _warmTail.Enabled = profile.EnableWarmTail;
        _warmTail.Configure(sampleRate, profile.WarmTailHoldMs, profile.WarmTailCloseMs, profile.WarmTailReopenMs,
            profile.WarmTailClosedHz, profile.WarmTailThresholdDb, profile.WarmTailOnsetRatio, profile.WarmTailMaxBlend);

        _saturation.Enabled = profile.EnableSaturation;
        _saturation.Drive = profile.SaturationDrive;
        _saturation.Mix = profile.SaturationMix;

        _room.Enabled = profile.EnableRoom;
        _room.Configure(sampleRate, profile.RoomDecayMs, profile.RoomMix);

        _bedBand = profile.EnableWarmBed
            ? new BandFilter(sampleRate, 250.0, 1200.0, 2)
            : null;
        _bedSaturation.Enabled = profile.EnableWarmBed;

        _limiter.Enabled = true;
        _limiter.Configure(sampleRate, profile.TruePeakCeilingDbTp, 2.5, 80.0);

        _processedLoudness.Configure(sampleRate);
        _sourceAnalyzer.Configure(sampleRate, 2048);
        _outputAnalyzer.Configure(sampleRate, 2048);
        _loudnessGainDb = 0.0;
    }

    public void Process(float[] buffer, int channels, int frames)
    {
        if (frames <= 0 || channels < 2)
            return;

        if (_configuredSampleRate != _sampleRate)
            Configure(_sampleRate);

        EnsureScratch(frames, channels);

        // --- analysis of the untouched source -----------------------------------
        Array.Copy(buffer, _scratch, frames * channels);
        var source = _sourceAnalyzer.Analyze(_scratch, channels, frames, _sampleRate);
        var detection = _detector.Detect(source);

        lock (_sync)
        {
            SourceAnalysis = source;
            Detection = detection;
        }

        bool bypass;
        lock (_sync)
        {
            bypass = _bypass;
        }

        if (bypass)
        {
            ApplyGain(buffer, channels, frames, _profile.OutputGainDb);
        }
        else
        {
            // --- Stage 2: surgical EQ --------------------------------------------
            foreach (var filter in _highPassFilters)
                filter.Process(buffer, channels, 0, frames);

            _midGlue?.Process(buffer, channels, 0, frames);
            _highShelf?.Process(buffer, channels, 0, frames);

            // --- Stage 2b: dynamic resonance suppression ---------------------------
            _deHarsh.Process(buffer, channels, frames, 2.0);

            // --- Stage 3: anti-pumping expansion and sub tightening -----------------
            _expander.Process(buffer, channels, frames, 15.0);
            _subTighter.Process(buffer, channels, frames, 15.0);

            // --- Stage 4: stereo glue, dark-noise guard and warm bed -------------
            _midSide.Process(buffer, channels, frames);
            _darkNoiseGuard.Process(buffer, channels, frames);

            if (_bedBand is not null)
                ApplyWarmBed(buffer, channels, frames);

            // Stage 4b: the virtual headphone stage, fed by the already de-harshed
            // presence band and the glued mid band.
            _stage.Process(buffer, channels, frames);

            // --- Stage 5: the warm tail, saturation and room -----------------------
            _warmTail.Process(buffer, channels, frames);
            _saturation.Process(buffer, frames, channels);
            _room.Process(buffer, frames, channels);

            // --- Stage 6: healthy headroom mastering --------------------------------
            _processedLoudness.Process(buffer, channels, frames);
            UpdateLoudnessGain();
            ApplyGain(buffer, channels, frames, _loudnessGainDb);

            _limiter.Process(buffer, channels, frames);
            ApplyGain(buffer, channels, frames, _profile.OutputGainDb);
        }

        Array.Copy(buffer, _scratch, frames * channels);
        var output = _outputAnalyzer.Analyze(_scratch, channels, frames, _sampleRate);

        lock (_sync)
        {
            OutputAnalysis = output;
        }
    }

    private void ApplyWarmBed(float[] buffer, int channels, int frames)
    {
        if (_bedBand is null)
            return;

        int count = frames * channels;
        if (_bedScratch.Length < count)
            _bedScratch = new float[count];

        Array.Copy(buffer, _bedScratch, count);
        _bedSaturation.Process(_bedScratch, frames, channels);
        _bedBand.Process(_bedScratch, channels, 0, frames);

        double mix = AudioMath.DbToLinear(_profile.WarmBedMixDb);
        for (int i = 0; i < count; i++)
            buffer[i] += (float)(_bedScratch[i] * mix);
    }

    private void UpdateLoudnessGain()
    {
        SafetyProfile profile;
        lock (_sync)
        {
            profile = _profile;
        }

        if (!profile.EnableLoudnessTarget)
        {
            _loudnessGainDb = 0.0;
            return;
        }

        double current = _processedLoudness.IntegratedLufs;
        if (current < -60.0)
            current = _processedLoudness.ShortTermLufs;
        if (current < -60.0)
            return;

        double correction = AudioMath.Clamp(profile.TargetLufs - current, -6.0, 6.0);
        _loudnessGainDb += 0.08 * (correction - _loudnessGainDb);
    }

    private void ApplyGain(float[] buffer, int channels, int frames, double gainDb)
    {
        if (Math.Abs(gainDb) < 0.001)
            return;

        double gain = AudioMath.DbToLinear(gainDb);
        int count = frames * channels;
        for (int i = 0; i < count; i++)
            buffer[i] *= (float)gain;
    }

    private void EnsureScratch(int frames, int channels)
    {
        int count = frames * channels;
        if (_scratch.Length < count)
            _scratch = new float[count];
    }

    public void Reset()
    {
        foreach (var filter in _highPassFilters)
            filter.Reset();
        _midGlue?.Reset();
        _highShelf?.Reset();
        _deHarsh.Reset();
        _expander.Reset();
        _subTighter.Reset();
        _midSide.Reset();
        _darkNoiseGuard.Reset();
        _stage.Reset();
        _warmTail.Reset();
        _limiter.Reset();
        _processedLoudness.Reset();
        _sourceAnalyzer.Reset();
        _outputAnalyzer.Reset();
    }
}
