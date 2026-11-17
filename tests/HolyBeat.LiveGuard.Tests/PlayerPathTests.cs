using System;
using HolyBeat.LiveGuard.Core.Dsp;
using HolyBeat.LiveGuard.Core.Profiles;
using NAudio.Wave;
using Xunit;

namespace HolyBeat.LiveGuard.Tests;

/// <summary>
/// Regression tests for the playback path. The audio engine asks for large blocks,
/// which used to overflow the scratch buffer and stop the player at second zero.
/// </summary>
public class PlayerPathTests
{
    private sealed class BlockSource : IWaveProvider
    {
        private readonly int _blockBytes;
        private int _position;

        public BlockSource(int blockBytes)
        {
            _blockBytes = blockBytes;
            WaveFormat = new WaveFormat(48000, 32, 2);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(Span<byte> buffer)
        {
            if (_position >= 48000 * 8 * 4)
                return 0;

            int count = Math.Min(buffer.Length, _blockBytes);
            for (int i = 0; i < count; i++)
                buffer[i] = (byte)((_position + i) % 251);

            _position += count;
            return count;
        }
    }

    [Theory]
    [InlineData(8192)]
    [InlineData(32768)]
    [InlineData(65536)]
    [InlineData(131072)]
    public void DspProvider_HandlesAnyBlockSizeTheEngineAsksFor(int blockBytes)
    {
        var profile = ProfileLibrary.CreateDefaults()[ProfileLibrary.Safe];
        var chain = new DspChain(profile, 48000);
        var provider = new DspWaveProvider(new BlockSource(blockBytes), chain);

        byte[] buffer = new byte[blockBytes];
        for (int i = 0; i < 5; i++)
        {
            int read = provider.Read(buffer.AsSpan(0, blockBytes));
            Assert.True(read > 0, $"The provider stopped reading after {i} blocks");
        }
    }

    [Fact]
    public void DspProvider_ProcessesTheAudioItReturns()
    {
        var profile = ProfileLibrary.CreateDefaults()[ProfileLibrary.Safe];
        var chain = new DspChain(profile, 48000);
        var provider = new DspWaveProvider(new BlockSource(65536), chain);

        byte[] buffer = new byte[65536];
        int read = provider.Read(buffer.AsSpan(0, buffer.Length));

        Assert.True(read > 0);
        Assert.Contains(buffer.AsSpan(0, read).ToArray(), value => value != 0);
    }

    [Fact]
    public void DspProvider_SurvivesTheFullStageChainOnLargeBlocks()
    {
        var profile = ProfileLibrary.CreateDefaults()[ProfileLibrary.Spatial];
        var chain = new DspChain(profile, 48000);
        var provider = new DspWaveProvider(new BlockSource(65536), chain);

        byte[] buffer = new byte[65536];
        for (int i = 0; i < 3; i++)
            Assert.True(provider.Read(buffer.AsSpan(0, buffer.Length)) > 0);
    }
}
