using System;
using HolyBeat.LiveGuard.Core.Analysis;
using HolyBeat.LiveGuard.Core.Dsp;
using HolyBeat.LiveGuard.Core.Profiles;

namespace HolyBeat.LiveGuard.Tests;

/// <summary>
/// Runs a buffer through the chain exactly like the player does: block by block,
/// with each block copied into the working buffer because the chain works in place.
/// </summary>
internal static class ChainRunner
{
    public const int BlockFrames = 512;

    public static (DetectionResult Detection, AnalysisSnapshot Source, float[] Processed) Run(
        SafetyProfile profile, float[] interleaved, int sampleRate = 48000)
    {
        var chain = new DspChain(profile, sampleRate);
        var processed = new float[interleaved.Length];
        var work = new float[BlockFrames * 2];

        for (int offset = 0; offset + work.Length <= interleaved.Length; offset += work.Length)
        {
            Array.Copy(interleaved, offset, work, 0, work.Length);
            chain.Process(work, 2, BlockFrames);
            Array.Copy(work, 0, processed, offset, work.Length);
        }

        return (chain.Detection, chain.SourceAnalysis, processed);
    }
}
