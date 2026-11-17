using HolyBeat.LiveGuard.Core.Analysis;
using Xunit;

namespace HolyBeat.LiveGuard.Tests;

public class AnalysisTests
{
    private static AnalysisSnapshot Snapshot(
        double subPressure = 0,
        double midScoop = 0,
        double harshness = 0,
        double instability = 0,
        double sideRatio = 0,
        double pumping = 0,
        double pumpingRate = 0,
        double crest = 6)
    {
        return new AnalysisSnapshot
        {
            SubPressure = subPressure,
            MidScoop = midScoop,
            HarshnessDb = harshness,
            DarkInstability = instability,
            HighFrequencySideRatio = sideRatio,
            PumpingDepthDb = pumping,
            PumpingRateHz = pumpingRate,
            CrestFactorDb = crest
        };
    }

    [Fact]
    public void Detector_RecognisesTheNoiseSignature()
    {
        var detector = new ProfileDetector();
        var result = detector.Detect(Snapshot(harshness: 14, crest: 15));

        Assert.Equal(AcousticProfileKind.Noise, result.Kind);
        Assert.Equal("anti-noise", result.RecommendedProfileId);
        Assert.NotEmpty(result.Reasons);
    }

    [Fact]
    public void Detector_RecognisesTheMuteSignature()
    {
        var detector = new ProfileDetector();
        var result = detector.Detect(Snapshot(subPressure: 0.55, midScoop: 0.5, pumping: 9, pumpingRate: 2));

        Assert.Equal(AcousticProfileKind.Mute, result.Kind);
        Assert.Equal("anti-mute", result.RecommendedProfileId);
    }

    [Fact]
    public void Detector_RecognisesTheDarkNoiseSignature()
    {
        var detector = new ProfileDetector();
        var result = detector.Detect(Snapshot(instability: 0.8));

        Assert.Equal(AcousticProfileKind.DarkNoise, result.Kind);
        Assert.Equal("dark-noise", result.RecommendedProfileId);
    }

    [Fact]
    public void Detector_RecognisesTheDualLayerSignature()
    {
        var detector = new ProfileDetector();
        var result = detector.Detect(Snapshot(sideRatio: 0.9, subPressure: 0.45, midScoop: 0.4, harshness: 10));

        Assert.Equal(AcousticProfileKind.DualLayer, result.Kind);
        Assert.Equal("dual-layer", result.RecommendedProfileId);
    }

    [Fact]
    public void Detector_LeavesBalancedMaterialInTheSafeZone()
    {
        var detector = new ProfileDetector();
        var result = detector.Detect(Snapshot(subPressure: 0.1, midScoop: 0.05, harshness: 2, instability: 0.05, sideRatio: 0.1));

        Assert.Equal(AcousticProfileKind.Safe, result.Kind);
        Assert.Equal("safe", result.RecommendedProfileId);
    }

    [Fact]
    public void SpectrumAnalyzer_FindsANarrowBandResonance()
    {
        var analyzer = new SpectrumAnalyzer(SignalHelper.SampleRate, 2048);

        // Bin centred tone (bin 170 of a 2048 point FFT at 48 kHz) so the peak lands on one bin.
        double binFrequency = 170.0 * SignalHelper.SampleRate / 2048.0;
        float[] tone = SignalHelper.Sine(binFrequency, 0.3, 4096);
        var stereo = SignalHelper.ToStereo(tone, tone);

        var snapshot = analyzer.Analyze(stereo, 2, 4096, SignalHelper.SampleRate);

        Assert.True(snapshot.HarshnessDb > 4,
            $"A pure tone should show a clear peak, prominence was {snapshot.HarshnessDb:F2} dB");
        Assert.True(snapshot.HarshFrequencyHz is > 3700 and < 4300,
            $"The peak should be reported near 4 kHz, was {snapshot.HarshFrequencyHz:F0} Hz");
    }

    [Fact]
    public void SpectrumAnalyzer_ReportsLoudnessAndCrestFactor()
    {
        var analyzer = new SpectrumAnalyzer(SignalHelper.SampleRate, 2048);
        float[] tone = SignalHelper.Sine(1000, 0.5, 48000);
        var stereo = SignalHelper.ToStereo(tone, tone);

        var snapshot = analyzer.Analyze(stereo, 2, 48000, SignalHelper.SampleRate);

        // A 0.5 amplitude 1 kHz tone in both channels measures about -6.7 LUFS.
        Assert.True(snapshot.MomentaryLufs is > -7.6 and < -5.4,
            $"A 0.5 amplitude 1 kHz tone is about -6.7 LUFS, was {snapshot.MomentaryLufs:F2}");
        // A pure sine has a 3 dB crest factor; real material sits far above that.
        Assert.True(snapshot.CrestFactorDb > 2.5, $"Crest factor was {snapshot.CrestFactorDb:F2} dB");
    }
}
