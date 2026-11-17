using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using HolyBeat.LiveGuard.Core.Analysis;
using HolyBeat.LiveGuard.Core.Dsp;
using HolyBeat.LiveGuard.Core.Profiles;
using NAudio.Wave;

namespace HolyBeat.LiveGuard.Core.Conversion;

public enum OutputAudioFormat
{
    Wav24,
    WavFloat32,
    Mp3
}

public sealed class ConversionOptions
{
    public bool AutoSelectProfile { get; init; } = true;
    public SafetyProfile? OverrideProfile { get; init; }
    public OutputAudioFormat Format { get; init; } = OutputAudioFormat.Wav24;
    public int Mp3Bitrate { get; init; } = 320_000;
}

public sealed class ConversionReport
{
    public string SourcePath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public AcousticProfileKind DetectedCategory { get; init; }
    public double DetectionConfidence { get; init; }
    public string AppliedProfileId { get; init; } = ProfileLibrary.Safe;
    public string AppliedProfileName { get; init; } = "Safe / Optimal Zone";
    public TimeSpan Duration { get; init; }

    public double SourceLufs { get; init; }
    public double OutputLufs { get; init; }
    public double SourceTruePeakDb { get; init; }
    public double OutputTruePeakDb { get; init; }
    public double SourceHarshnessDb { get; init; }
    public double OutputHarshnessDb { get; init; }
    public double SourceSubPressure { get; init; }
    public double OutputSubPressure { get; init; }
    public double SourceDarkInstability { get; init; }
    public double OutputDarkInstability { get; init; }
    public IReadOnlyList<string> DetectionReasons { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Offline safe-music converter: analyses a track for Mute, Noise, Dark Noise,
/// or Dual-Layer signatures and renders a fatigue-free, headroom-safe audio file
/// through the full HolyBeat DSP chain.
/// </summary>
public sealed class SafeAudioConverter
{
    private const int BlockFrames = 4096;

    public ConversionReport ConvertFile(
        string sourcePath,
        string outputPath,
        ConversionOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path is required.", nameof(sourcePath));
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Source audio file not found.", sourcePath);
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is required.", nameof(outputPath));

        options ??= new ConversionOptions();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        var profiles = ProfileLibrary.CreateDefaults();
        DetectionResult? detection = null;
        SafetyProfile chosenProfile = options.OverrideProfile?.Clone() ?? profiles[ProfileLibrary.Safe].Clone();

        if (options.AutoSelectProfile)
        {
            detection = AnalyzeFile(sourcePath, progress, 0.0, 0.25, cancellationToken);
            if (profiles.TryGetValue(detection.RecommendedProfileId, out var matched))
                chosenProfile = matched.Clone();
        }

        double renderStart = options.AutoSelectProfile ? 0.25 : 0.0;
        return RenderFile(sourcePath, outputPath, chosenProfile, detection, options, progress, renderStart, 1.0, cancellationToken);
    }

    public static DetectionResult AnalyzeFile(
        string sourcePath,
        IProgress<double>? progress = null,
        double progressStart = 0.0,
        double progressEnd = 1.0,
        CancellationToken cancellationToken = default)
    {
        using var reader = new AudioFileReader(sourcePath);
        int sr = reader.WaveFormat.SampleRate;
        int ch = reader.WaveFormat.Channels;
        var analyzer = new SpectrumAnalyzer(sr, 2048);
        var detector = new ProfileDetector();

        float[] buffer = new float[BlockFrames * ch];
        long totalFrames = reader.Length / (sizeof(float) * ch);
        long framesRead = 0;

        double sumSub = 0, sumScoop = 0, sumHarsh = 0, sumDark = 0, sumSide = 0, sumPump = 0;
        int count = 0;
        AnalysisSnapshot? last = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = reader.Read(buffer.AsSpan());
            if (read <= 0)
                break;

            int frames = read / ch;
            framesRead += frames;
            last = analyzer.Analyze(buffer, ch, frames, sr);

            if (last.MomentaryLufs > -55.0)
            {
                sumSub += last.SubPressure;
                sumScoop += last.MidScoop;
                sumHarsh += last.HarshnessDb;
                sumDark += last.DarkInstability;
                sumSide += last.HighFrequencySideRatio;
                sumPump += last.PumpingDepthDb;
                count++;
            }

            if (totalFrames > 0)
            {
                double frac = Math.Clamp((double)framesRead / totalFrames, 0.0, 1.0);
                progress?.Report(progressStart + frac * (progressEnd - progressStart));
            }
        }

        if (last is null || count == 0)
            return detector.Detect(new AnalysisSnapshot { SampleRate = sr });

        var aggregate = new AnalysisSnapshot
        {
            SampleRate = sr,
            IntegratedLufs = last.IntegratedLufs,
            TruePeakDb = last.TruePeakDb,
            CrestFactorDb = last.CrestFactorDb,
            SubPressure = sumSub / count,
            MidScoop = sumScoop / count,
            HarshnessDb = sumHarsh / count,
            DarkInstability = sumDark / count,
            HighFrequencySideRatio = sumSide / count,
            PumpingDepthDb = sumPump / count,
            PumpingRateHz = last.PumpingRateHz
        };

        return detector.Detect(aggregate);
    }

    private static ConversionReport RenderFile(
        string sourcePath,
        string outputPath,
        SafetyProfile profile,
        DetectionResult? preDetection,
        ConversionOptions options,
        IProgress<double>? progress,
        double progressStart,
        double progressEnd,
        CancellationToken cancellationToken)
    {
        using var reader = new AudioFileReader(sourcePath);
        int sr = reader.WaveFormat.SampleRate;
        int ch = reader.WaveFormat.Channels;
        TimeSpan duration = reader.TotalTime;
        long totalFrames = reader.Length / (sizeof(float) * ch);

        var chain = new DspChain(profile, sr);
        float[] buffer = new float[BlockFrames * ch];

        double sumSrcHarsh = 0, sumOutHarsh = 0;
        double sumSrcSub = 0, sumOutSub = 0;
        double sumSrcDark = 0, sumOutDark = 0;
        double maxSrcPeak = -140.0, maxOutPeak = -140.0;
        int statsCount = 0;

        string actualOutputPath = outputPath;
        string wavTarget = options.Format == OutputAudioFormat.Mp3
            ? Path.Combine(Path.GetTempPath(), $"holybeat-conv-{Guid.NewGuid():N}.wav")
            : outputPath;

        try
        {
            WaveFormat outFormat = options.Format == OutputAudioFormat.WavFloat32
                ? WaveFormat.CreateIeeeFloatWaveFormat(sr, ch)
                : new WaveFormat(sr, 24, ch);

            using (var writer = new WaveFileWriter(wavTarget, outFormat))
            {
                byte[] pcm24 = options.Format == OutputAudioFormat.WavFloat32
                    ? Array.Empty<byte>()
                    : new byte[BlockFrames * ch * 3];

                long framesProcessed = 0;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int read = reader.Read(buffer.AsSpan());
                    if (read <= 0)
                        break;

                    int frames = read / ch;
                    chain.Process(buffer, ch, frames);

                    if (options.Format == OutputAudioFormat.WavFloat32)
                    {
                        writer.WriteSamples(buffer, 0, read);
                    }
                    else
                    {
                        WritePcm24(buffer, read, pcm24, writer);
                    }

                    var src = chain.SourceAnalysis;
                    var dst = chain.OutputAnalysis;
                    if (src.TruePeakDb > maxSrcPeak) maxSrcPeak = src.TruePeakDb;
                    if (dst.TruePeakDb > maxOutPeak) maxOutPeak = dst.TruePeakDb;

                    if (src.MomentaryLufs > -55.0)
                    {
                        sumSrcHarsh += src.HarshnessDb;
                        sumOutHarsh += dst.HarshnessDb;
                        sumSrcSub += src.SubPressure;
                        sumOutSub += dst.SubPressure;
                        sumSrcDark += src.DarkInstability;
                        sumOutDark += dst.DarkInstability;
                        statsCount++;
                    }

                    framesProcessed += frames;
                    if (totalFrames > 0)
                    {
                        double frac = Math.Clamp((double)framesProcessed / totalFrames, 0.0, 1.0);
                        double encodeReserve = options.Format == OutputAudioFormat.Mp3 ? 0.90 : 1.0;
                        progress?.Report(progressStart + frac * encodeReserve * (progressEnd - progressStart));
                    }
                }
            }

            if (options.Format == OutputAudioFormat.Mp3)
            {
                try
                {
                    using var wavReader = new WaveFileReader(wavTarget);
                    MediaFoundationEncoder.EncodeToMp3(wavReader, actualOutputPath, options.Mp3Bitrate);
                }
                catch
                {
                    // Fallback to lossless 24-bit WAV if Media Foundation MP3 encoder is unavailable.
                    actualOutputPath = Path.ChangeExtension(outputPath, ".wav");
                    File.Copy(wavTarget, actualOutputPath, overwrite: true);
                }
            }

            progress?.Report(progressEnd);

            var finalDetection = preDetection ?? chain.Detection;
            return new ConversionReport
            {
                SourcePath = sourcePath,
                OutputPath = actualOutputPath,
                DetectedCategory = finalDetection.Kind,
                DetectionConfidence = finalDetection.Confidence,
                AppliedProfileId = profile.Id,
                AppliedProfileName = profile.Name,
                Duration = duration,
                SourceLufs = chain.SourceAnalysis.IntegratedLufs,
                OutputLufs = chain.OutputAnalysis.IntegratedLufs,
                SourceTruePeakDb = maxSrcPeak,
                OutputTruePeakDb = maxOutPeak,
                SourceHarshnessDb = statsCount > 0 ? sumSrcHarsh / statsCount : 0.0,
                OutputHarshnessDb = statsCount > 0 ? sumOutHarsh / statsCount : 0.0,
                SourceSubPressure = statsCount > 0 ? sumSrcSub / statsCount : 0.0,
                OutputSubPressure = statsCount > 0 ? sumOutSub / statsCount : 0.0,
                SourceDarkInstability = statsCount > 0 ? sumSrcDark / statsCount : 0.0,
                OutputDarkInstability = statsCount > 0 ? sumOutDark / statsCount : 0.0,
                DetectionReasons = finalDetection.Reasons
            };
        }
        finally
        {
            if (options.Format == OutputAudioFormat.Mp3 && File.Exists(wavTarget))
            {
                try { File.Delete(wavTarget); } catch { }
            }
        }
    }

    private static void WritePcm24(float[] samples, int sampleCount, byte[] scratch, WaveFileWriter writer)
    {
        int byteCount = sampleCount * 3;
        for (int i = 0; i < sampleCount; i++)
        {
            double clamped = Math.Clamp(samples[i], -1.0, 1.0);
            int val = (int)Math.Round(clamped * 8_388_607.0);
            int offset = i * 3;
            scratch[offset] = (byte)(val & 0xFF);
            scratch[offset + 1] = (byte)((val >> 8) & 0xFF);
            scratch[offset + 2] = (byte)((val >> 16) & 0xFF);
        }

        writer.Write(scratch, 0, byteCount);
    }
}
