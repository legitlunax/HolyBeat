using System.Collections.Generic;

namespace HolyBeat.LiveGuard.Core.Profiles;

/// <summary>
/// The preset library. Every preset is derived from a failure mode that was verified
/// across the 103 tracks of the research, and names the tracks it was tuned against.
/// </summary>
public static class ProfileLibrary
{
    public const string Safe = "safe";
    public const string AntiMute = "anti-mute";
    public const string AntiNoise = "anti-noise";
    public const string DarkNoise = "dark-noise";
    public const string DualLayer = "dual-layer";
    public const string WarmTail = "warm-tail";
    public const string Reference = "reference";
    public const string Spatial = "spatial";

    public static Dictionary<string, SafetyProfile> CreateDefaults()
    {
        var profiles = new Dictionary<string, SafetyProfile>
        {
            [Safe] = CreateSafe(),
            [AntiMute] = CreateAntiMute(),
            [AntiNoise] = CreateAntiNoise(),
            [DarkNoise] = CreateDarkNoise(),
            [DualLayer] = CreateDualLayer(),
            [WarmTail] = CreateWarmTail(),
            [Reference] = CreateReference(),
            [Spatial] = CreateSpatial()
        };

        return profiles;
    }

    public static List<string> DefaultOrder() => new List<string>
    {
        Safe, AntiMute, AntiNoise, DarkNoise, DualLayer, WarmTail, Spatial, Reference
    };

    private static SafetyProfile CreateSafe() => new SafetyProfile
    {
        Id = Safe,
        Name = "Safe / Optimal Zone",
        Description = "Unified full-spectrum cohesion: rich 250 Hz - 2 kHz mid glue, tight non-pumping bass, " +
                      "steady background frequencies. Tuned against Survivor, Who's The Dreamer?, Half A Heart and Burn Our Bridges Down.",
        AccentColor = "#37D6B0",
        HighPassHz = 35.0,
        DeHarshThresholdDb = -22.0,
        DeHarshMaxReductionDb = 5.0,
        HighShelfDb = -1.8,
        MidGlueDb = 1.6,
        MidGlueHz = 600.0,
        ExpansionMaxBoostDb = 3.0,
        ExpansionSensitivity = 0.40,
        SubThresholdDb = -18.0,
        SubMaxReductionDb = 4.0,
        SubRatio = 2.5,
        WarmBedMixDb = -26.0,
        EnableDarkNoiseGuard = true,
        DarkNoiseGuardStrength = 0.65,
        DarkNoiseGuardMaxDb = 4.5,
        WarmTailHoldMs = 12.0,
        WarmTailClosedHz = 2200.0,
        WarmTailMaxBlend = 0.62,
        SaturationDrive = 1.18,
        SaturationMix = 0.30,
        RoomMix = 0.04,
        TargetLufs = -12.0,
        TruePeakCeilingDbTp = -1.0
    };

    private static SafetyProfile CreateAntiMute()
    {
        var profile = CreateSafe();
        profile.Id = AntiMute;
        profile.Name = "Anti-Mute (Sensory Shutdown Guard)";
        profile.Description = "Upward expansion fills the kick-triggered volume vacuums of heavy sidechains, " +
                              "while 40 - 120 Hz is tightened so bass hits stay punchy instead of swelling. " +
                              "Tuned against King of Hearts, Volcano, Redemption and Maestro.";
        profile.AccentColor = "#5B8CFF";
        profile.EnableExpansion = true;
        profile.ExpansionMaxBoostDb = 3.5;
        profile.ExpansionSensitivity = 0.42;
        profile.ExpansionAttackMs = 10.0;
        profile.ExpansionReleaseMs = 160.0;
        profile.SubThresholdDb = -18.0;
        profile.SubMaxReductionDb = 4.5;
        profile.SubRatio = 2.5;
        profile.MidGlueDb = 2.0;
        profile.MidGlueHz = 520.0;
        profile.WarmBedMixDb = -24.0;
        profile.EnableDarkNoiseGuard = true;
        profile.DarkNoiseGuardStrength = 0.35;
        profile.DarkNoiseGuardMaxDb = 3.0;
        profile.EnableWarmTail = false;
        profile.TargetLufs = -12.5;
        return profile;
    }

    private static SafetyProfile CreateAntiNoise()
    {
        var profile = CreateSafe();
        profile.Id = AntiNoise;
        profile.Name = "Anti-Noise (Ear Pain Guard)";
        profile.Description = "Dynamic suppression of the 2.8 - 5.5 kHz pain band, a gentle roll-off above 9 kHz " +
                              "and the warm tail that closes every harsh transient. " +
                              "Tuned against Maps, Question, Awake and Alive and Mortals.";
        profile.AccentColor = "#FF7A59";
        profile.DeHarshThresholdDb = -24.0;
        profile.DeHarshMaxReductionDb = 6.5;
        profile.DeHarshRatio = 3.2;
        profile.DeHarshAttackMs = 1.5;
        profile.DeHarshReleaseMs = 55.0;
        profile.HighShelfDb = -3.0;
        profile.HighShelfHz = 9000.0;
        profile.MidGlueDb = 1.6;
        profile.WarmBedMixDb = -25.0;
        profile.EnableDarkNoiseGuard = true;
        profile.DarkNoiseGuardStrength = 0.65;
        profile.DarkNoiseGuardMaxDb = 4.5;
        profile.EnableWarmTail = true;
        profile.WarmTailHoldMs = 11.0;
        profile.WarmTailCloseMs = 15.0;
        profile.WarmTailClosedHz = 2600.0;
        profile.WarmTailMaxBlend = 0.55;
        profile.EnableSaturation = true;
        profile.SaturationDrive = 1.22;
        profile.SaturationMix = 0.35;
        profile.TargetLufs = -12.0;
        return profile;
    }

    private static SafetyProfile CreateDarkNoise()
    {
        var profile = CreateSafe();
        profile.Id = DarkNoise;
        profile.Name = "Dark Noise Stabilizer";
        profile.Description = "A warm harmonic bed plus tape saturation masks the erratic background filter sweeps, " +
                              "and the bright layer is pulled back so nothing floats away from the vocal. " +
                              "Tuned against Flutter (Weeb Like Me).";
        profile.AccentColor = "#B98CFF";
        profile.EnableDarkNoiseGuard = true;
        profile.DarkNoiseGuardStrength = 1.20;
        profile.DarkNoiseGuardMaxDb = 6.5;
        profile.EnableWarmBed = true;
        profile.WarmBedMixDb = -24.0;
        profile.EnableSaturation = true;
        profile.SaturationDrive = 1.25;
        profile.SaturationMix = 0.35;
        profile.EnableWarmTail = true;
        profile.WarmTailHoldMs = 14.0;
        profile.WarmTailCloseMs = 18.0;
        profile.WarmTailClosedHz = 2800.0;
        profile.WarmTailMaxBlend = 0.55;
        profile.EnableHighFrequencyNarrowing = true;
        profile.HighFrequencyWidth = 0.72;
        profile.HighShelfDb = -2.5;
        profile.MidGlueDb = 1.8;
        profile.MidGlueHz = 700.0;
        profile.EnableRoom = true;
        profile.RoomMix = 0.05;
        profile.TargetLufs = -12.0;
        return profile;
    }

    private static SafetyProfile CreateDualLayer()
    {
        var profile = CreateSafe();
        profile.Id = DualLayer;
        profile.Name = "Dual-Layer Guard";
        profile.Description = "Re-joins a dry high-treble layer with the compressed sub-bass bottom: 30% narrower " +
                              "side signal above 4 kHz, mono bass below 120 Hz and a warm mid glue. " +
                              "Tuned against K/DA - VILLAIN and Build A B*tch.";
        profile.AccentColor = "#FFB020";
        profile.EnableHighFrequencyNarrowing = true;
        profile.HighFrequencyWidth = 0.7;
        profile.NarrowingHz = 4000.0;
        profile.EnableMonoBass = true;
        profile.MonoBassHz = 120.0;
        profile.MidGlueDb = 3.0;
        profile.MidGlueHz = 650.0;
        profile.MidGlueQ = 0.85;
        profile.EnableExpansion = true;
        profile.ExpansionMaxBoostDb = 4.0;
        profile.EnableWarmTail = true;
        profile.WarmTailClosedHz = 2400.0;
        profile.TargetLufs = -12.0;
        return profile;
    }

    private static SafetyProfile CreateWarmTail()
    {
        var profile = CreateSafe();
        profile.Id = WarmTail;
        profile.Name = "Who's The Dreamer? Warm Tail";
        profile.Description = "The signature discovery: every noisy transient keeps its bite for 12 ms, then the high band " +
                              "closes to 2.2 kHz and a warm tape saturation plus a short dark room catches the tail.";
        profile.AccentColor = "#FF5FA2";
        profile.EnableWarmTail = true;
        profile.WarmTailHoldMs = 12.0;
        profile.WarmTailCloseMs = 15.0;
        profile.WarmTailReopenMs = 120.0;
        profile.WarmTailClosedHz = 2400.0;
        profile.WarmTailThresholdDb = -34.0;
        profile.WarmTailOnsetRatio = 1.8;
        profile.WarmTailMaxBlend = 0.72;
        profile.EnableSaturation = true;
        profile.SaturationDrive = 1.30;
        profile.SaturationMix = 0.45;
        profile.EnableRoom = true;
        profile.RoomMix = 0.07;
        profile.RoomDecayMs = 130.0;
        profile.DeHarshThresholdDb = -30.0;
        profile.DeHarshMaxReductionDb = 4.0;
        profile.HighShelfDb = -2.0;
        profile.MidGlueDb = 1.6;
        profile.MidGlueHz = 500.0;
        profile.TargetLufs = -13.0;
        return profile;
    }

    private static SafetyProfile CreateSpatial()
    {
        var profile = CreateSafe();
        profile.Id = Spatial;
        profile.Name = "Virtual Stage (Headphone Surround)";
        profile.Description = "The Safe chain plus a virtual 5.1.4 headphone stage: the 120 - 1500 Hz glue band is " +
                              "anchored to the centre, the de-harshed 2.8 - 5.5 kHz presence band becomes the height " +
                              "layer and the 40 - 120 Hz pressure band becomes the rumble bus.";
        profile.AccentColor = "#7C8CFF";
        profile.EnableVirtualStage = true;
        profile.StageWidth = 1.0;
        profile.StageHeight = 0.45;
        profile.StageCentreFocus = 0.65;
        profile.StageLfeMix = 0.5;
        profile.StageSurroundMix = 0.25;
        profile.MidGlueDb = 2.4;
        profile.MidGlueHz = 650.0;
        profile.DeHarshThresholdDb = -24.0;
        profile.DeHarshMaxReductionDb = 7.0;
        profile.TargetLufs = -12.0;
        return profile;
    }

    private static SafetyProfile CreateReference()
    {
        var profile = CreateSafe();
        profile.Id = Reference;
        profile.Name = "Safe Reference (Light)";
        profile.Description = "Minimal intervention for already balanced material: a light de-harsh and the warm tail only, " +
                              "so the energy of the track survives untouched.";
        profile.AccentColor = "#9AA7B8";
        profile.EnableExpansion = false;
        profile.EnableSubTightening = false;
        profile.EnableMonoBass = false;
        profile.EnableHighFrequencyNarrowing = false;
        profile.EnableRoom = false;
        profile.DeHarshThresholdDb = -28.0;
        profile.DeHarshMaxReductionDb = 3.5;
        profile.HighShelfDb = -2.0;
        profile.MidGlueDb = 1.2;
        profile.WarmTailHoldMs = 12.0;
        profile.TargetLufs = -13.0;
        return profile;
    }
}
