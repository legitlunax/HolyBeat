using System;
using System.Collections.Generic;

namespace HolyBeat.LiveGuard.Core.Analysis;

/// <summary>Classification of the incoming audio, using the failure modes from the HolyBeat research.</summary>
public enum AcousticProfileKind
{
    Safe,
    Mute,
    Noise,
    DarkNoise,
    DualLayer
}

/// <summary>Outcome of the live classification, including the reasons behind the decision.</summary>
public sealed class DetectionResult
{
    public AcousticProfileKind Kind { get; init; }
    public double Confidence { get; init; }
    public string RecommendedProfileId { get; init; } = "safe";
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<AcousticProfileKind, double> Scores { get; init; } =
        new Dictionary<AcousticProfileKind, double>();
}

/// <summary>
/// Classifies live audio into the four acoustic failure modes described in the research:
/// Mute (sensory shutdown), Noise (ear pain), Dark Noise (frequency disorientation),
/// Dual-Layer (mute + noise) and the Safe / Optimal Zone.
/// </summary>
public sealed class ProfileDetector
{
    private readonly List<string> _reasons = new List<string>();

    public DetectionResult Detect(AnalysisSnapshot snapshot)
    {
        _reasons.Clear();

        double subScore = ScoreSubPressure(snapshot.SubPressure);
        double scoopScore = ScoreMidScoop(snapshot.MidScoop);
        double pumpScore = ScorePumping(snapshot.PumpingDepthDb, snapshot.PumpingRateHz);
        double harshScore = ScoreHarshness(snapshot.HarshnessDb);
        double darkScore = ScoreDarkNoise(snapshot.DarkInstability);
        double splitScore = ScoreSplit(snapshot.HighFrequencySideRatio);

        double muteScore = Math.Clamp(0.45 * subScore + 0.30 * scoopScore + 0.25 * pumpScore, 0.0, 1.0);
        double noiseScore = Math.Clamp(0.72 * harshScore + 0.28 * Math.Clamp((snapshot.CrestFactorDb - 8.0) / 12.0, 0.0, 1.0), 0.0, 1.0);
        double dualScore = Math.Clamp(0.45 * splitScore + 0.30 * muteScore + 0.25 * noiseScore, 0.0, 1.0);

        var scores = new Dictionary<AcousticProfileKind, double>
        {
            [AcousticProfileKind.Safe] = 0.0,
            [AcousticProfileKind.Mute] = muteScore,
            [AcousticProfileKind.Noise] = noiseScore,
            [AcousticProfileKind.DarkNoise] = darkScore,
            [AcousticProfileKind.DualLayer] = dualScore
        };

        // The Safe zone is the fallback: it wins when no failure signature is clearly present.
        scores[AcousticProfileKind.Safe] = Math.Clamp(1.0 - Math.Max(Math.Max(muteScore, noiseScore), Math.Max(darkScore, dualScore)), 0.0, 1.0);

        AcousticProfileKind best = AcousticProfileKind.Safe;
        double bestScore = scores[AcousticProfileKind.Safe];

        foreach (var pair in scores)
        {
            if (pair.Key == AcousticProfileKind.Safe)
                continue;
            if (pair.Value > bestScore)
            {
                best = pair.Key;
                bestScore = pair.Value;
            }
        }

        if (bestScore < 0.35)
        {
            best = AcousticProfileKind.Safe;
            bestScore = scores[AcousticProfileKind.Safe];
            if (_reasons.Count == 0)
                _reasons.Add("Unified full-spectrum cohesion - inside the Safe / Optimal Zone.");
        }

        return new DetectionResult
        {
            Kind = best,
            Confidence = Math.Clamp(bestScore, 0.0, 1.0),
            RecommendedProfileId = RecommendedProfile(best),
            Reasons = _reasons.ToArray(),
            Scores = scores
        };
    }

    private static string RecommendedProfile(AcousticProfileKind kind) => kind switch
    {
        AcousticProfileKind.Mute => "anti-mute",
        AcousticProfileKind.Noise => "anti-noise",
        AcousticProfileKind.DarkNoise => "dark-noise",
        AcousticProfileKind.DualLayer => "dual-layer",
        _ => "safe"
    };

    private double ScoreSubPressure(double subPressure)
    {
        if (subPressure >= 0.30)
            _reasons.Add($"Sub-bass pressure at 40-80 Hz takes {subPressure * 100:F0}% of the spectrum.");
        if (subPressure >= 0.45)
            _reasons.Add("Prolonged sub-bass swells risk the Mute (sensory shutdown) response.");
        return Math.Clamp((subPressure - 0.12) / 0.38, 0.0, 1.0);
    }

    private double ScoreMidScoop(double midScoop)
    {
        if (midScoop >= 0.25)
            _reasons.Add($"Mid range (250 Hz - 1.5 kHz) is scooped out by {midScoop * 100:F0}%.");
        return Math.Clamp(midScoop / 0.6, 0.0, 1.0);
    }

    private double ScorePumping(double depthDb, double rateHz)
    {
        if (depthDb >= 4.0)
            _reasons.Add($"Kick-triggered pumping dips of {depthDb:F1} dB at about {rateHz:F1} Hz.");
        if (depthDb >= 7.0)
            _reasons.Add("Sidechain volume vacuums are deep enough to trigger the Mute response.");
        double depthScore = Math.Clamp((depthDb - 2.0) / 8.0, 0.0, 1.0);
        double rateScore = Math.Clamp((rateHz - 0.8) / 5.0, 0.0, 1.0);
        return 0.7 * depthScore + 0.3 * rateScore;
    }

    private double ScoreHarshness(double harshnessDb)
    {
        if (harshnessDb >= 8.0)
            _reasons.Add($"Narrow-band resonance of {harshnessDb:F1} dB inside the 2.5-6 kHz pain band.");
        return Math.Clamp((harshnessDb - 6.0) / 10.0, 0.0, 1.0);
    }

    private double ScoreDarkNoise(double instability)
    {
        if (instability >= 0.35)
            _reasons.Add("Spectral shape keeps moving - background filter sweeps detected.");
        return Math.Clamp((instability - 0.12) / 0.55, 0.0, 1.0);
    }

    private double ScoreSplit(double sideRatio)
    {
        if (sideRatio >= 0.35)
            _reasons.Add($"High-frequency side/mid ratio of {sideRatio:F2} - the bright layer is detached from the bass.");
        return Math.Clamp((sideRatio - 0.20) / 0.60, 0.0, 1.0);
    }
}
