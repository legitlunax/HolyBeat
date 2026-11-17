using System;
using System.IO;
using HolyBeat.LiveGuard.Core.Analysis;
using HolyBeat.LiveGuard.Core.Conversion;
using HolyBeat.LiveGuard.Core.Profiles;
using NAudio.Wave;
using Xunit;

namespace HolyBeat.LiveGuard.Tests;

public class ConverterTests
{
    private static string WriteTempWav(float[] interleaved, int sampleRate = 48000)
    {
        string path = Path.Combine(Path.GetTempPath(), $"holybeat-conv-src-{Guid.NewGuid():N}.wav");
        using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, 2));
        byte[] bytes = new byte[interleaved.Length * 2];
        for (int i = 0; i < interleaved.Length; i++)
        {
            short val = (short)(Math.Clamp(interleaved[i], -1f, 1f) * 32767f);
            BitConverter.GetBytes(val).CopyTo(bytes, i * 2);
        }

        writer.Write(bytes, 0, bytes.Length);
        return path;
    }

    [Fact]
    public void Converter_AutoDetectsHarshNoiseAndProducesSafeWav24()
    {
        string src = WriteTempWav(IntegrationSignalFactory.Harsh());
        string dst = Path.Combine(Path.GetTempPath(), $"holybeat-conv-dst-{Guid.NewGuid():N}.wav");

        try
        {
            var converter = new SafeAudioConverter();
            double lastProgress = 0;
            var progress = new Progress<double>(p => lastProgress = p);

            var report = converter.ConvertFile(src, dst, new ConversionOptions
            {
                AutoSelectProfile = true,
                Format = OutputAudioFormat.Wav24
            }, progress);

            Assert.True(File.Exists(report.OutputPath));
            Assert.Equal(AcousticProfileKind.Noise, report.DetectedCategory);
            Assert.Equal(ProfileLibrary.AntiNoise, report.AppliedProfileId);
            Assert.True(report.OutputTruePeakDb <= -0.5,
                $"Expected safe true-peak ceiling, got {report.OutputTruePeakDb:F2} dBTP");

            using var verifyReader = new AudioFileReader(report.OutputPath);
            Assert.True(verifyReader.Length > 0);
        }
        finally
        {
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(dst)) File.Delete(dst);
        }
    }

    [Fact]
    public void Converter_SupportsExplicitProfileOverrideAndFloat32Output()
    {
        string src = WriteTempWav(IntegrationSignalFactory.Safe());
        string dst = Path.Combine(Path.GetTempPath(), $"holybeat-conv-f32-{Guid.NewGuid():N}.wav");

        try
        {
            var warmTail = ProfileLibrary.CreateDefaults()[ProfileLibrary.WarmTail];
            var converter = new SafeAudioConverter();

            var report = converter.ConvertFile(src, dst, new ConversionOptions
            {
                AutoSelectProfile = false,
                OverrideProfile = warmTail,
                Format = OutputAudioFormat.WavFloat32
            });

            Assert.True(File.Exists(report.OutputPath));
            Assert.Equal(ProfileLibrary.WarmTail, report.AppliedProfileId);

            using var verifyReader = new WaveFileReader(report.OutputPath);
            Assert.Equal(32, verifyReader.WaveFormat.BitsPerSample);
        }
        finally
        {
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(dst)) File.Delete(dst);
        }
    }
}
