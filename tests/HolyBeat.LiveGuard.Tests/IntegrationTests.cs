using System;
using System.IO;
using HolyBeat.LiveGuard.Core.Analysis;
using HolyBeat.LiveGuard.Core.Dsp;
using HolyBeat.LiveGuard.Core.Profiles;
using NAudio.Wave;
using Xunit;

namespace HolyBeat.LiveGuard.Tests;

/// <summary>
/// End to end tests that follow the exact path the player uses:
/// decode the file with NAudio, push the samples through the HolyBeat chain and read
/// back what the live detector concluded.
/// </summary>
public class IntegrationTests
{
    private const int SampleRate = 48000;

    private static string WriteTemporaryWav(string name, float[] interleaved)
    {
        string path = Path.Combine(Path.GetTempPath(), $"holybeat-{name}-{Guid.NewGuid():N}.wav");

        using (var writer = new WaveFileWriter(path, new WaveFormat(SampleRate, 16, 2)))
        {
            var bytes = new byte[interleaved.Length * 2];
            for (int i = 0; i < interleaved.Length; i++)
            {
                short value = (short)(Math.Clamp(interleaved[i], -1f, 1f) * 32767f);
                BitConverter.GetBytes(value).CopyTo(bytes, i * 2);
            }

            writer.Write(bytes, 0, bytes.Length);
        }

        return path;
    }

    private static (DetectionResult Detection, float[] Processed, float[] Source) RunThroughFile(
        float[] interleaved, SafetyProfile profile)
    {
        string path = WriteTemporaryWav("signal", interleaved);

        try
        {
            using var reader = new AudioFileReader(path);

            // AudioFileReader exposes IEEE float samples; Length is in bytes.
            var decoded = new float[reader.Length / sizeof(float)];
            int filled = 0;
            while (filled < decoded.Length)
            {
                int read = reader.Read(decoded.AsSpan(filled, decoded.Length - filled));
                if (read <= 0)
                    break;
                filled += read;
            }

            var source = new float[filled];
            Array.Copy(decoded, source, filled);

            var chain = new DspChain(profile, reader.WaveFormat.SampleRate);
            var work = new float[ChainRunner.BlockFrames * 2];

            for (int offset = 0; offset + work.Length <= filled; offset += work.Length)
            {
                Array.Copy(decoded, offset, work, 0, work.Length);
                chain.Process(work, 2, ChainRunner.BlockFrames);
                Array.Copy(work, 0, decoded, offset, work.Length);
            }

            return (chain.Detection, decoded, source);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void PlayerPath_ClassifiesAHarshSignalAsNoise()
    {
        var profile = ProfileLibrary.CreateDefaults()[ProfileLibrary.Safe];
        var result = RunThroughFile(IntegrationSignalFactory.Harsh(), profile);

        Assert.Equal(AcousticProfileKind.Noise, result.Detection.Kind);
        Assert.True(result.Detection.Confidence > 0.35,
            $"Confidence was only {result.Detection.Confidence:F2}");
        Assert.NotEmpty(result.Detection.Reasons);
    }

    [Fact]
    public void PlayerPath_ClassifiesSubPressureAsMute()
    {
        var profile = ProfileLibrary.CreateDefaults()[ProfileLibrary.Safe];
        var result = RunThroughFile(IntegrationSignalFactory.SubPressure(), profile);

        Assert.Equal(AcousticProfileKind.Mute, result.Detection.Kind);
        Assert.True(result.Detection.Confidence > 0.35,
            $"Confidence was only {result.Detection.Confidence:F2}");
    }

    [Fact]
    public void PlayerPath_LeavesBalancedMaterialInTheSafeZone()
    {
        var profile = ProfileLibrary.CreateDefaults()[ProfileLibrary.Safe];
        var result = RunThroughFile(IntegrationSignalFactory.Safe(), profile);

        Assert.Equal(AcousticProfileKind.Safe, result.Detection.Kind);
    }

    /// <summary>Level of one frequency band, measured in the time domain.</summary>
    private static double BandLevel(float[] interleaved, double frequency)
    {
        var band = Core.Dsp.Biquad.Create(Core.Dsp.BiquadKind.Peaking, SampleRate, frequency, 2.5, 12.0);
        var copy = new float[interleaved.Length];
        Array.Copy(interleaved, copy, interleaved.Length);
        band.Process(copy, 2, 0, copy.Length / 2);

        double sum = 0.0;
        int count = 0;
        for (int i = 0; i < copy.Length / 2; i++)
        {
            sum += copy[i * 2] * copy[i * 2];
            count++;
        }

        return count > 0 ? Math.Sqrt(sum / count) : 0.0;
    }

    [Fact]
    public void PlayerPath_TamesTheHarshBand()
    {
        var profile = ProfileLibrary.CreateDefaults()[ProfileLibrary.AntiNoise];
        profile.EnableLoudnessTarget = false;

        var result = RunThroughFile(IntegrationSignalFactory.Harsh(), profile);

        double sourceLevel = BandLevel(result.Source, 4200);
        double processedLevel = BandLevel(result.Processed, 4200);

        Assert.True(processedLevel < sourceLevel * 0.92,
            $"The 4.2 kHz pain band should be tamed, ratio was {processedLevel / sourceLevel:F3}");
    }

    [Fact]
    public void PlayerPath_ProducesFiniteSamplesAndStaysBelowFullScale()
    {
        var profile = ProfileLibrary.CreateDefaults()[ProfileLibrary.Safe];
        var result = RunThroughFile(IntegrationSignalFactory.Harsh(), profile);

        foreach (float sample in result.Processed)
            Assert.True(float.IsFinite(sample), "The player path must never emit NaN or infinity");

        Assert.True(SignalHelper.Peak(result.Processed) <= 1.1f, "The output must stay below full scale");
    }
}
