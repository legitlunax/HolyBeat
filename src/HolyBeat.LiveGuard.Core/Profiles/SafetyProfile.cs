namespace HolyBeat.LiveGuard.Core.Profiles;

/// <summary>
/// Every tunable parameter of the HolyBeat processing chain.
/// The default values are the ones measured in the research and are used as the
/// reference points in the GUI sliders.
/// </summary>
public sealed class SafetyProfile
{
    public string Id { get; set; } = "safe";
    public string Name { get; set; } = "Safe / Optimal Zone";
    public string Description { get; set; } = string.Empty;
    public string AccentColor { get; set; } = "#4DD0E1";
    public bool Enabled { get; set; } = true;

    // ---- Stage 2: surgical EQ and de-harshing -------------------------------------
    public bool EnableHighPass { get; set; } = true;
    public double HighPassHz { get; set; } = 35.0;
    public int HighPassOrder { get; set; } = 4;

    public bool EnableDeHarsh { get; set; } = true;
    public double DeHarshLowHz { get; set; } = 2800.0;
    public double DeHarshHighHz { get; set; } = 5500.0;
    public double DeHarshThresholdDb { get; set; } = -22.0;
    public double DeHarshRatio { get; set; } = 3.0;
    public double DeHarshMaxReductionDb { get; set; } = 6.0;
    public double DeHarshAttackMs { get; set; } = 2.0;
    public double DeHarshReleaseMs { get; set; } = 60.0;

    public bool EnableHighShelf { get; set; } = true;
    public double HighShelfDb { get; set; } = -3.0;
    public double HighShelfHz { get; set; } = 9000.0;

    public bool EnableMidGlue { get; set; } = true;
    public double MidGlueDb { get; set; } = 2.0;
    public double MidGlueHz { get; set; } = 600.0;
    public double MidGlueQ { get; set; } = 0.9;

    // ---- Stage 3: anti-pumping and sub-bass tightening ------------------------------
    public bool EnableExpansion { get; set; } = true;
    public double ExpansionLowHz { get; set; } = 100.0;
    public double ExpansionHighHz { get; set; } = 1500.0;
    public double ExpansionAttackMs { get; set; } = 8.0;
    public double ExpansionReleaseMs { get; set; } = 150.0;
    public double ExpansionSlowReleaseMs { get; set; } = 1200.0;
    public double ExpansionMaxBoostDb { get; set; } = 4.5;
    public double ExpansionSensitivity { get; set; } = 0.5;

    public bool EnableSubTightening { get; set; } = true;
    public double SubLowHz { get; set; } = 40.0;
    public double SubHighHz { get; set; } = 120.0;
    public double SubAttackMs { get; set; } = 10.0;
    public double SubReleaseMs { get; set; } = 70.0;
    public double SubRatio { get; set; } = 3.0;
    public double SubThresholdDb { get; set; } = -24.0;
    public double SubMaxReductionDb { get; set; } = 6.0;

    // ---- Stage 4: stereo field glue --------------------------------------------------
    public bool EnableMonoBass { get; set; } = true;
    public double MonoBassHz { get; set; } = 120.0;
    public bool EnableHighFrequencyNarrowing { get; set; } = true;
    public double NarrowingHz { get; set; } = 4000.0;
    public double HighFrequencyWidth { get; set; } = 0.7;

    public bool EnableWarmBed { get; set; } = true;
    public double WarmBedMixDb { get; set; } = -26.0;

    public bool EnableDarkNoiseGuard { get; set; } = true;
    public double DarkNoiseGuardStrength { get; set; } = 0.50;
    public double DarkNoiseGuardMaxDb { get; set; } = 3.5;

    // ---- Stage 4b: the virtual headphone stage ------------------------------------
    public bool EnableVirtualStage { get; set; }
    public double StageWidth { get; set; } = 1.0;
    public double StageHeight { get; set; } = 0.35;
    public double StageCentreFocus { get; set; } = 0.5;
    public double StageLfeMix { get; set; }
    public double StageSurroundMix { get; set; }

    // ---- Stage 5: warm tail (the "Who's The Dreamer?" discovery) ---------------------
    public bool EnableWarmTail { get; set; } = true;
    public double WarmTailHoldMs { get; set; } = 12.0;
    public double WarmTailCloseMs { get; set; } = 15.0;
    public double WarmTailReopenMs { get; set; } = 120.0;
    public double WarmTailClosedHz { get; set; } = 2200.0;
    public double WarmTailThresholdDb { get; set; } = -34.0;
    public double WarmTailOnsetRatio { get; set; } = 1.8;
    public double WarmTailMaxBlend { get; set; } = 1.0;

    public bool EnableSaturation { get; set; } = true;
    public double SaturationDrive { get; set; } = 1.20;
    public double SaturationMix { get; set; } = 0.30;

    public bool EnableRoom { get; set; } = true;
    public double RoomMix { get; set; } = 0.04;
    public double RoomDecayMs { get; set; } = 120.0;

    // ---- Stage 6: healthy headroom mastering -----------------------------------------
    public bool EnableLoudnessTarget { get; set; } = true;
    public double TargetLufs { get; set; } = -12.0;
    public double TruePeakCeilingDbTp { get; set; } = -1.0;
    public double OutputGainDb { get; set; } = 0.0;

    public SafetyProfile Clone() => (SafetyProfile)MemberwiseClone();

    /// <summary>
    /// Restores every value that fell outside the supported range from the factory preset.
    /// This keeps a damaged or hand edited configuration file from breaking the chain.
    /// </summary>
    public void Repair(SafetyProfile reference)
    {
        static double Fix(double value, double min, double max, double fallback)
            => double.IsNaN(value) || value < min || value > max ? fallback : value;

        HighPassHz = Fix(HighPassHz, 20, 60, reference.HighPassHz);
        DeHarshLowHz = Fix(DeHarshLowHz, 1800, 4000, reference.DeHarshLowHz);
        DeHarshHighHz = Fix(DeHarshHighHz, 4500, 8000, reference.DeHarshHighHz);
        DeHarshThresholdDb = Fix(DeHarshThresholdDb, -40, -6, reference.DeHarshThresholdDb);
        DeHarshRatio = Fix(DeHarshRatio, 1, 8, reference.DeHarshRatio);
        DeHarshMaxReductionDb = Fix(DeHarshMaxReductionDb, 0, 12, reference.DeHarshMaxReductionDb);
        DeHarshAttackMs = Fix(DeHarshAttackMs, 0, 50, reference.DeHarshAttackMs);
        DeHarshReleaseMs = Fix(DeHarshReleaseMs, 10, 300, reference.DeHarshReleaseMs);
        HighShelfDb = Fix(HighShelfDb, -8, 0, reference.HighShelfDb);
        HighShelfHz = Fix(HighShelfHz, 6000, 12000, reference.HighShelfHz);
        MidGlueDb = Fix(MidGlueDb, -4, 6, reference.MidGlueDb);
        MidGlueHz = Fix(MidGlueHz, 300, 1000, reference.MidGlueHz);
        MidGlueQ = Fix(MidGlueQ, 0.1, 4, reference.MidGlueQ);
        ExpansionLowHz = Fix(ExpansionLowHz, 40, 800, reference.ExpansionLowHz);
        ExpansionHighHz = Fix(ExpansionHighHz, 200, 6000, reference.ExpansionHighHz);
        ExpansionAttackMs = Fix(ExpansionAttackMs, 1, 30, reference.ExpansionAttackMs);
        ExpansionReleaseMs = Fix(ExpansionReleaseMs, 60, 400, reference.ExpansionReleaseMs);
        ExpansionSlowReleaseMs = Fix(ExpansionSlowReleaseMs, 200, 3000, reference.ExpansionSlowReleaseMs);
        ExpansionMaxBoostDb = Fix(ExpansionMaxBoostDb, 0, 10, reference.ExpansionMaxBoostDb);
        ExpansionSensitivity = Fix(ExpansionSensitivity, 0, 1.5, reference.ExpansionSensitivity);
        SubLowHz = Fix(SubLowHz, 20, 150, reference.SubLowHz);
        SubHighHz = Fix(SubHighHz, 40, 400, reference.SubHighHz);
        SubAttackMs = Fix(SubAttackMs, 2, 40, reference.SubAttackMs);
        SubReleaseMs = Fix(SubReleaseMs, 30, 250, reference.SubReleaseMs);
        SubRatio = Fix(SubRatio, 1, 8, reference.SubRatio);
        SubThresholdDb = Fix(SubThresholdDb, -40, -8, reference.SubThresholdDb);
        SubMaxReductionDb = Fix(SubMaxReductionDb, 0, 12, reference.SubMaxReductionDb);
        MonoBassHz = Fix(MonoBassHz, 60, 240, reference.MonoBassHz);
        NarrowingHz = Fix(NarrowingHz, 2000, 9000, reference.NarrowingHz);
        HighFrequencyWidth = Fix(HighFrequencyWidth, 0.2, 1, reference.HighFrequencyWidth);
        WarmBedMixDb = Fix(WarmBedMixDb, -40, 0, reference.WarmBedMixDb);
        DarkNoiseGuardStrength = Fix(DarkNoiseGuardStrength, 0, 1.5, reference.DarkNoiseGuardStrength);
        DarkNoiseGuardMaxDb = Fix(DarkNoiseGuardMaxDb, 0, 9, reference.DarkNoiseGuardMaxDb);
        WarmTailMaxBlend = Fix(WarmTailMaxBlend, 0, 1, reference.WarmTailMaxBlend);
        StageWidth = Fix(StageWidth, 0, 1.5, reference.StageWidth);
        StageHeight = Fix(StageHeight, 0, 1, reference.StageHeight);
        StageCentreFocus = Fix(StageCentreFocus, 0, 1, reference.StageCentreFocus);
        StageLfeMix = Fix(StageLfeMix, 0, 1, reference.StageLfeMix);
        StageSurroundMix = Fix(StageSurroundMix, 0, 1, reference.StageSurroundMix);
        WarmTailHoldMs = Fix(WarmTailHoldMs, 0, 40, reference.WarmTailHoldMs);
        WarmTailCloseMs = Fix(WarmTailCloseMs, 3, 60, reference.WarmTailCloseMs);
        WarmTailReopenMs = Fix(WarmTailReopenMs, 40, 400, reference.WarmTailReopenMs);
        WarmTailClosedHz = Fix(WarmTailClosedHz, 1200, 5000, reference.WarmTailClosedHz);
        WarmTailThresholdDb = Fix(WarmTailThresholdDb, -60, -6, reference.WarmTailThresholdDb);
        WarmTailOnsetRatio = Fix(WarmTailOnsetRatio, 1.1, 4, reference.WarmTailOnsetRatio);
        SaturationDrive = Fix(SaturationDrive, 1, 4, reference.SaturationDrive);
        SaturationMix = Fix(SaturationMix, 0, 1, reference.SaturationMix);
        RoomMix = Fix(RoomMix, 0, 0.3, reference.RoomMix);
        RoomDecayMs = Fix(RoomDecayMs, 20, 500, reference.RoomDecayMs);
        TargetLufs = Fix(TargetLufs, -18, -8, reference.TargetLufs);
        TruePeakCeilingDbTp = Fix(TruePeakCeilingDbTp, -3, -0.1, reference.TruePeakCeilingDbTp);
        OutputGainDb = Fix(OutputGainDb, -12, 12, reference.OutputGainDb);
    }

    public SafetyProfile WithDefaults()
    {
        var clone = Clone();
        clone.HighPassHz = Math.Clamp(clone.HighPassHz, 20.0, 80.0);
        clone.DeHarshLowHz = Math.Clamp(clone.DeHarshLowHz, 1000.0, 8000.0);
        clone.DeHarshHighHz = Math.Clamp(clone.DeHarshHighHz, clone.DeHarshLowHz + 200.0, 12000.0);
        clone.MidGlueHz = Math.Clamp(clone.MidGlueHz, 100.0, 2000.0);
        clone.ExpansionLowHz = Math.Clamp(clone.ExpansionLowHz, 40.0, 800.0);
        clone.ExpansionHighHz = Math.Clamp(clone.ExpansionHighHz, clone.ExpansionLowHz + 200.0, 6000.0);
        clone.SubLowHz = Math.Clamp(clone.SubLowHz, 20.0, 150.0);
        clone.SubHighHz = Math.Clamp(clone.SubHighHz, clone.SubLowHz + 20.0, 400.0);
        clone.MonoBassHz = Math.Clamp(clone.MonoBassHz, 60.0, 240.0);
        clone.NarrowingHz = Math.Clamp(clone.NarrowingHz, 2000.0, 9000.0);
        clone.HighFrequencyWidth = Math.Clamp(clone.HighFrequencyWidth, 0.2, 1.0);
        clone.WarmTailClosedHz = Math.Clamp(clone.WarmTailClosedHz, 1200.0, 5000.0);
        clone.TargetLufs = Math.Clamp(clone.TargetLufs, -18.0, -8.0);
        clone.TruePeakCeilingDbTp = Math.Clamp(clone.TruePeakCeilingDbTp, -3.0, -0.1);
        clone.SaturationDrive = Math.Clamp(clone.SaturationDrive, 1.0, 4.0);
        clone.RoomMix = Math.Clamp(clone.RoomMix, 0.0, 0.4);
        return clone;
    }
}
