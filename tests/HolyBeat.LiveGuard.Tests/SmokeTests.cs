using System;
using System.IO;
using HolyBeat.LiveGuard.Core.Config;
using HolyBeat.LiveGuard.Core.Profiles;
using Xunit;

namespace HolyBeat.LiveGuard.Tests;

public class SmokeTests
{
    [Fact]
    public void Factory_ContainsEveryResearchProfile()
    {
        var profiles = ProfileLibrary.CreateDefaults();

        Assert.Equal(8, profiles.Count);
        Assert.Contains(ProfileLibrary.Safe, profiles.Keys);
        Assert.Contains(ProfileLibrary.AntiMute, profiles.Keys);
        Assert.Contains(ProfileLibrary.AntiNoise, profiles.Keys);
        Assert.Contains(ProfileLibrary.DarkNoise, profiles.Keys);
        Assert.Contains(ProfileLibrary.DualLayer, profiles.Keys);
        Assert.Contains(ProfileLibrary.WarmTail, profiles.Keys);
        Assert.Contains(ProfileLibrary.Reference, profiles.Keys);
        Assert.Contains(ProfileLibrary.Spatial, profiles.Keys);
    }

    [Fact]
    public void Factory_ProfilesUseTheResearchDefaults()
    {
        var safe = ProfileLibrary.CreateDefaults()[ProfileLibrary.Safe];

        Assert.Equal(35.0, safe.HighPassHz, 0.001);
        Assert.Equal(2800.0, safe.DeHarshLowHz, 0.001);
        Assert.Equal(5500.0, safe.DeHarshHighHz, 0.001);
        Assert.Equal(9000.0, safe.HighShelfHz, 0.001);
        Assert.Equal(600.0, safe.MidGlueHz, 0.001);
        Assert.Equal(120.0, safe.MonoBassHz, 0.001);
        Assert.Equal(4000.0, safe.NarrowingHz, 0.001);
        Assert.Equal(2200.0, safe.WarmTailClosedHz, 0.001);
        Assert.Equal(12.0, safe.WarmTailHoldMs, 0.001);
        Assert.Equal(-1.0, safe.TruePeakCeilingDbTp, 0.001);
    }

    [Fact]
    public void WithDefaults_ClampsOutOfRangeValues()
    {
        var profile = new SafetyProfile
        {
            HighPassHz = 5000,
            DeHarshLowHz = 20000,
            TargetLufs = 6,
            TruePeakCeilingDbTp = 12,
            HighFrequencyWidth = 9
        };

        var clamped = profile.WithDefaults();

        Assert.InRange(clamped.HighPassHz, 20, 80);
        Assert.InRange(clamped.DeHarshLowHz, 1000, 8000);
        Assert.InRange(clamped.TargetLufs, -18, -8);
        Assert.InRange(clamped.TruePeakCeilingDbTp, -3, -0.1);
        Assert.InRange(clamped.HighFrequencyWidth, 0.2, 1.0);
    }

    [Fact]
    public void ConfigStore_RoundTripsProfiles()
    {
        string path = Path.Combine(Path.GetTempPath(), $"holybeat-config-{Guid.NewGuid():N}.json");

        try
        {
            var config = ConfigStore.CreateDefault();
            config.ActiveProfileId = ProfileLibrary.AntiNoise;
            config.Profiles[ProfileLibrary.AntiNoise].DeHarshMaxReductionDb = 7.25;
            config.Routing.Mode = RoutingMode.SystemMonitor;
            ConfigStore.Save(config, path);

            var loaded = ConfigStore.Load(path);

            Assert.Equal(ProfileLibrary.AntiNoise, loaded.ActiveProfileId);
            Assert.Equal(7.25, loaded.Profiles[ProfileLibrary.AntiNoise].DeHarshMaxReductionDb, 0.001);
            Assert.Equal(RoutingMode.SystemMonitor, loaded.Routing.Mode);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void ConfigStore_FallsBackToDefaultsWhenFileIsBroken()
    {
        string path = Path.Combine(Path.GetTempPath(), $"holybeat-broken-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ this is not json");

        try
        {
            var loaded = ConfigStore.Load(path);
            Assert.NotEmpty(loaded.Profiles);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
