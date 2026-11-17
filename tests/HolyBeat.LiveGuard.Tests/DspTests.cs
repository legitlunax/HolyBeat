using System;
using HolyBeat.LiveGuard.Core.Dsp;
using HolyBeat.LiveGuard.Core.Profiles;
using Xunit;

namespace HolyBeat.LiveGuard.Tests;

public class DspTests
{
    private static SafetyProfile BareProfile() => new SafetyProfile
    {
        EnableHighPass = false,
        EnableDeHarsh = false,
        EnableHighShelf = false,
        EnableMidGlue = false,
        EnableExpansion = false,
        EnableSubTightening = false,
        EnableMonoBass = false,
        EnableHighFrequencyNarrowing = false,
        EnableDarkNoiseGuard = false,
        EnableWarmBed = false,
        EnableWarmTail = false,
        EnableSaturation = false,
        EnableRoom = false,
        EnableLoudnessTarget = false
    };

    private static double ProcessThrough(SafetyProfile profile, float[] mono, out float[] result)
    {
        var chain = new DspChain(profile, SignalHelper.SampleRate);
        var stereo = SignalHelper.ToStereo(mono, mono);
        chain.Process(stereo, 2, mono.Length);
        result = new float[mono.Length];
        for (int i = 0; i < mono.Length; i++)
            result[i] = stereo[i * 2];
        return chain.LimiterGainReductionDb;
    }

    [Fact]
    public void HighPass_AttenuatesSubRumbleButKeepsTheMidRange()
    {
        float[] low = SignalHelper.Sine(20, 0.5, 48000);
        float[] mid = SignalHelper.Sine(1000, 0.5, 48000);

        var filterLow = Biquad.Create(BiquadKind.HighPass, SignalHelper.SampleRate, 35, 0.7071);
        var filterLow2 = Biquad.Create(BiquadKind.HighPass, SignalHelper.SampleRate, 35, 0.7071);
        filterLow.Process(low, 1, 0, low.Length);
        filterLow2.Process(low, 1, 0, low.Length);

        var filterMid = Biquad.Create(BiquadKind.HighPass, SignalHelper.SampleRate, 35, 0.7071);
        var filterMid2 = Biquad.Create(BiquadKind.HighPass, SignalHelper.SampleRate, 35, 0.7071);
        filterMid.Process(mid, 1, 0, mid.Length);
        filterMid2.Process(mid, 1, 0, mid.Length);

        double lowAmplitude = SignalHelper.AmplitudeAt(low, 20);
        double midAmplitude = SignalHelper.AmplitudeAt(mid, 1000);

        Assert.True(lowAmplitude < 0.12, $"20 Hz should be removed, amplitude was {lowAmplitude:F4}");
        Assert.True(midAmplitude > 0.45, $"1 kHz should pass, amplitude was {midAmplitude:F4}");
    }

    [Fact]
    public void DeHarsh_AttenuatesThePainBandOnly()
    {
        var harsh = BareProfile();
        harsh.EnableDeHarsh = true;
        harsh.DeHarshThresholdDb = -22;
        harsh.DeHarshRatio = 3;
        harsh.DeHarshMaxReductionDb = 6;

        var off = BareProfile();

        float[] tone = SignalHelper.Sine(4000, 0.316, 96000);
        ProcessThrough(harsh, tone, out float[] processed);
        ProcessThrough(off, tone, out float[] untouched);

        double processedAmplitude = SignalHelper.AmplitudeAt(processed, 4000);
        double originalAmplitude = SignalHelper.AmplitudeAt(untouched, 4000);

        Assert.True(processedAmplitude < originalAmplitude * 0.85,
            $"4 kHz should be tamed, ratio was {processedAmplitude / originalAmplitude:F3}");

        float[] bass = SignalHelper.Sine(150, 0.316, 96000);
        ProcessThrough(harsh, bass, out float[] processedBass);
        ProcessThrough(off, bass, out float[] untouchedBass);

        double bassRatio = SignalHelper.AmplitudeAt(processedBass, 150) / SignalHelper.AmplitudeAt(untouchedBass, 150);
        Assert.True(bassRatio > 0.9, $"150 Hz must stay untouched, ratio was {bassRatio:F3}");
    }

    [Fact]
    public void WarmTail_ClosesTheHighBandOnTheTailOfATransient()
    {
        var warm = BareProfile();
        warm.EnableWarmTail = true;
        warm.WarmTailHoldMs = 12;
        warm.WarmTailCloseMs = 15;
        warm.WarmTailReopenMs = 120;
        warm.WarmTailClosedHz = 2200;
        warm.WarmTailThresholdDb = -34;

        var off = BareProfile();

        float[] burst = SignalHelper.DecayingBurst(6000, 0.3, 25, 96000);
        ProcessThrough(warm, burst, out float[] processed);
        ProcessThrough(off, burst, out float[] untouched);

        // Measure the tail right after the high band has closed (12 ms hold + 15 ms close).
        int tailStart = 28 * SignalHelper.SampleRate / 1000;

        double processedAmplitude = SignalHelper.AmplitudeAt(processed, 6000, SignalHelper.SampleRate, 1, tailStart);
        double originalAmplitude = SignalHelper.AmplitudeAt(untouched, 6000, SignalHelper.SampleRate, 1, tailStart);

        Assert.True(processedAmplitude < originalAmplitude * 0.6,
            $"The 6 kHz tail should be closed, ratio was {processedAmplitude / originalAmplitude:F3}");
    }

    [Fact]
    public void UpwardExpansion_FillsThePumpingDips()
    {
        var expanding = BareProfile();
        expanding.EnableExpansion = true;
        expanding.ExpansionLowHz = 100;
        expanding.ExpansionHighHz = 1500;
        expanding.ExpansionMaxBoostDb = 4.5;
        expanding.ExpansionSensitivity = 0.5;

        var off = BareProfile();

        // 100 ms of tone, then 100 ms of the same tone 20 dB down.
        var signal = new float[96000];
        float[] loud = SignalHelper.Sine(300, 0.5, 48000);
        float[] quiet = SignalHelper.Sine(300, 0.05, 48000);
        Array.Copy(loud, 0, signal, 0, 48000);
        Array.Copy(quiet, 0, signal, 48000, 48000);

        ProcessThrough(expanding, signal, out float[] processed);
        ProcessThrough(off, signal, out float[] untouched);

        double processedQuiet = SignalHelper.Rms(processed, 48000, 48000);
        double untouchedQuiet = SignalHelper.Rms(untouched, 48000, 48000);

        Assert.True(processedQuiet > untouchedQuiet * 1.15,
            $"The hole should be filled, ratio was {processedQuiet / untouchedQuiet:F3}");
    }

    [Fact]
    public void SubTightening_ShortensSustainedSubBass()
    {
        var tightening = BareProfile();
        tightening.EnableSubTightening = true;
        tightening.SubLowHz = 40;
        tightening.SubHighHz = 120;
        tightening.SubThresholdDb = -24;
        tightening.SubRatio = 3;
        tightening.SubMaxReductionDb = 6;

        var off = BareProfile();

        float[] sub = SignalHelper.Sine(60, 0.5, 96000);
        ProcessThrough(tightening, sub, out float[] processed);
        ProcessThrough(off, sub, out float[] untouched);

        double processedAmplitude = SignalHelper.AmplitudeAt(processed, 60);
        double originalAmplitude = SignalHelper.AmplitudeAt(untouched, 60);

        Assert.True(processedAmplitude < originalAmplitude * 0.85,
            $"60 Hz should be tightened, ratio was {processedAmplitude / originalAmplitude:F3}");
    }

    [Fact]
    public void MonoBass_MakesTheSubContentIdenticalInBothChannels()
    {
        var profile = BareProfile();
        profile.EnableMonoBass = true;
        profile.MonoBassHz = 120;

        float[] left = SignalHelper.Sine(60, 0.5, 96000);
        float[] right = SignalHelper.Sine(60, -0.5, 96000);
        var stereo = SignalHelper.ToStereo(left, right);

        var chain = new DspChain(profile, SignalHelper.SampleRate);
        chain.Process(stereo, 2, 96000);

        var processedLeft = new float[96000];
        var processedRight = new float[96000];
        for (int i = 0; i < 96000; i++)
        {
            processedLeft[i] = stereo[i * 2];
            processedRight[i] = stereo[i * 2 + 1];
        }

        double leftSub = SignalHelper.AmplitudeAt(processedLeft, 60);
        double rightSub = SignalHelper.AmplitudeAt(processedRight, 60);

        Assert.True(leftSub < 0.1, $"Sub content should be mono summed, amplitude was {leftSub:F4}");
        Assert.True(Math.Abs(leftSub - rightSub) < 0.02, "Both channels must carry the same sub content");
    }

    [Fact]
    public void HighFrequencyNarrowing_ReducesTheSideSignal()
    {
        var profile = BareProfile();
        profile.EnableHighFrequencyNarrowing = true;
        profile.NarrowingHz = 4000;
        profile.HighFrequencyWidth = 0.7;

        float[] left = SignalHelper.Sine(8000, 0.5, 96000);
        float[] right = SignalHelper.Sine(8000, -0.5, 96000);
        var stereo = SignalHelper.ToStereo(left, right);

        var sideBefore = new float[96000];
        for (int i = 0; i < 96000; i++)
            sideBefore[i] = 0.5f * (left[i] - right[i]);

        var chain = new DspChain(profile, SignalHelper.SampleRate);
        chain.Process(stereo, 2, 96000);

        var sideAfter = new float[96000];
        for (int i = 0; i < 96000; i++)
            sideAfter[i] = 0.5f * (stereo[i * 2] - stereo[i * 2 + 1]);

        double before = SignalHelper.AmplitudeAt(sideBefore, 8000);
        double after = SignalHelper.AmplitudeAt(sideAfter, 8000);
        double ratio = after / before;

        Assert.True(ratio is > 0.6 and < 0.82, $"Side width should be reduced by about 30 %, ratio was {ratio:F3}");
    }

    [Fact]
    public void TruePeakLimiter_HonoursTheCeiling()
    {
        var limiter = new TruePeakLimiter();
        limiter.Configure(SignalHelper.SampleRate, -1.0, 2.5, 80.0);

        float[] signal = SignalHelper.Sine(1000, 0.95, 96000);
        var stereo = SignalHelper.ToStereo(signal, signal);
        limiter.Process(stereo, 2, 96000);

        double peak = SignalHelper.Peak(stereo);
        double peakDb = 20.0 * Math.Log10(peak);

        Assert.True(peakDb <= -0.8, $"True peak must stay under the -1 dBTP ceiling, was {peakDb:F2} dBFS");
        Assert.True(peakDb > -5.0, $"The limiter must not over-attenuate, was {peakDb:F2} dBFS");
    }

    [Fact]
    public void LoudnessMeter_MatchesTheReferenceLevel()
    {
        var meter = new LoudnessMeter();
        meter.Configure(SignalHelper.SampleRate);

        float[] tone = SignalHelper.Sine(1000, 0.1, 48000 * 2);
        var stereo = SignalHelper.ToStereo(tone, tone);

        for (int i = 0; i < 20; i++)
            meter.Process(stereo, 2, 48000);

        // A 1 kHz sine at -20 dBFS measures about -20.7 LUFS.
        Assert.True(meter.MomentaryLufs is > -21.6 and < -19.9,
            $"Momentary loudness should be close to -20.7 LUFS, was {meter.MomentaryLufs:F2}");
        Assert.True(meter.IntegratedLufs is > -21.6 and < -19.9,
            $"Integrated loudness should be close to -20.7 LUFS, was {meter.IntegratedLufs:F2}");
    }

    [Fact]
    public void DspChain_ProducesFiniteSamplesForTheFullProfile()
    {
        var profile = ProfileLibrary.CreateDefaults()[ProfileLibrary.Safe];
        var chain = new DspChain(profile, SignalHelper.SampleRate);

        float[] noise = SignalHelper.Noise(48000, 0.4, 99);
        var stereo = SignalHelper.ToStereo(noise, noise);
        chain.Process(stereo, 2, 48000);

        foreach (float sample in stereo)
        {
            Assert.True(float.IsFinite(sample), "The chain must never emit NaN or infinity");
        }

        Assert.True(SignalHelper.Peak(stereo) <= 1.2f, "The chain must not blow up the signal");
    }

    [Fact]
    public void DarkNoiseGuard_StaysTransparentOnSteadyMaterial()
    {
        var guard = new DarkNoiseGuard();
        guard.Configure(SignalHelper.SampleRate, 1.0, 5.5);

        float[] anchor = SignalHelper.Sine(500, 0.3, 48000);
        float[] sweep = SignalHelper.Sine(3000, 0.15, 48000);
        float[] mix = new float[48000];
        for (int i = 0; i < mix.Length; i++)
            mix[i] = anchor[i] + sweep[i];

        var stereo = SignalHelper.ToStereo(mix, mix);
        guard.Process(stereo, 2, 48000);

        Assert.True(guard.CurrentSuppressionDb < 0.25,
            $"Steady music should remain untouched, suppression was {guard.CurrentSuppressionDb:F2} dB");
    }

    [Fact]
    public void DarkNoiseGuard_SmoothsSuddenSweepSurge()
    {
        var guard = new DarkNoiseGuard();
        guard.Configure(SignalHelper.SampleRate, 1.0, 5.5);

        // 0.6s steady anchor + low sweep so the slow baseline settles
        float[] anchor = SignalHelper.Sine(500, 0.3, 48000);
        float[] quietSweep = SignalHelper.Sine(3500, 0.03, 28800);
        float[] loudSweep = SignalHelper.Sine(3500, 0.45, 19200);

        float[] mix = new float[48000];
        for (int i = 0; i < 28800; i++)
            mix[i] = anchor[i] + quietSweep[i];
        for (int i = 28800; i < 48000; i++)
            mix[i] = anchor[i] + loudSweep[i - 28800];

        var stereo = SignalHelper.ToStereo(mix, mix);
        guard.Process(stereo, 2, 48000);

        Assert.True(guard.CurrentSuppressionDb > 0.5,
            $"A sudden filter sweep surge should be damped, suppression was {guard.CurrentSuppressionDb:F2} dB");
    }
}
