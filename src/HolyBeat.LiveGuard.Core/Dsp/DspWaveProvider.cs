using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace HolyBeat.LiveGuard.Core.Dsp;

/// <summary>
/// Wraps any PCM source and runs it through the HolyBeat DSP chain.
/// Supports IEEE float32 and signed 16 bit PCM, which covers every endpoint
/// mix format used by Windows and by the built-in player.
/// </summary>
public sealed class DspWaveProvider : IWaveProvider
{
    private readonly IWaveProvider _source;
    private readonly DspChain _chain;
    private readonly bool _isFloat;
    private readonly int _channels;
    private readonly int _bytesPerSample;
    private float[] _samples;

    public DspWaveProvider(IWaveProvider source, DspChain chain)
    {
        _source = source;
        _chain = chain;

        WaveFormat = source.WaveFormat;
        _channels = Math.Max(1, WaveFormat.Channels);
        _isFloat = WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat && WaveFormat.BitsPerSample == 32;
        _bytesPerSample = Math.Max(1, WaveFormat.BitsPerSample / 8);
        _samples = new float[Math.Max(8192, _channels * 4096)];
    }

    public WaveFormat WaveFormat { get; }

    public int Read(Span<byte> buffer)
    {
        byte[] scratch = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            int read = _source.Read(scratch.AsSpan(0, buffer.Length));
            if (read <= 0)
                return read;

            int bytesPerFrame = _channels * _bytesPerSample;
            int frames = read / bytesPerFrame;
            if (frames <= 0)
                return read;

            int sampleCount = frames * _channels;

            // The audio engine can ask for a block that is larger than the initial
            // buffer, so the scratch area grows with the request.
            if (_samples.Length < sampleCount)
                _samples = new float[Math.Max(sampleCount, _samples.Length * 2)];

            Span<byte> data = scratch.AsSpan(0, frames * bytesPerFrame);

            if (_isFloat)
            {
                Span<float> source = MemoryMarshal.Cast<byte, float>(data);
                source.CopyTo(_samples.AsSpan(0, sampleCount));
            }
            else
            {
                Span<short> source = MemoryMarshal.Cast<byte, short>(data);
                for (int i = 0; i < sampleCount; i++)
                    _samples[i] = source[i] / 32768f;
            }

            _chain.Process(_samples, _channels, frames);

            if (_isFloat)
            {
                Span<float> target = MemoryMarshal.Cast<byte, float>(data);
                _samples.AsSpan(0, sampleCount).CopyTo(target);
            }
            else
            {
                Span<short> target = MemoryMarshal.Cast<byte, short>(data);
                for (int i = 0; i < sampleCount; i++)
                {
                    float clamped = Math.Clamp(_samples[i], -1f, 1f);
                    target[i] = (short)(clamped * 32767f);
                }
            }

            data.CopyTo(buffer);
            return read;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }
}

/// <summary>
/// Pulls system audio from a WASAPI recorder and exposes it as an IWaveProvider
/// so the playback side can consume it like any other stream.
/// </summary>
public sealed class WasapiRecorderProvider : IWaveProvider
{
    private readonly ConcurrentQueue<byte[]> _queue = new ConcurrentQueue<byte[]>();
    private readonly AutoResetEvent _dataReady = new AutoResetEvent(false);
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private readonly Task _pump;
    private byte[]? _current;
    private int _currentOffset;

    public WasapiRecorderProvider(WasapiRecorder recorder)
    {
        Recorder = recorder;
        WaveFormat = recorder.WaveFormat;
        _pump = Task.Run(() => PumpAsync(_cts.Token));
    }

    public WasapiRecorder Recorder { get; }

    public WaveFormat WaveFormat { get; private set; }

    public void ReportError(string message) => ErrorRaised?.Invoke(message);

    public event Action<string>? ErrorRaised;

    private async Task PumpAsync(CancellationToken token)
    {
        try
        {
            await foreach (var chunk in Recorder.CaptureAsync(token))
            {
                ReadOnlyMemory<byte> data = chunk.Data;
                byte[] bytes = data.ToArray();

                if (bytes.Length == 0)
                    continue;

                WaveFormat = Recorder.WaveFormat;
                _queue.Enqueue(bytes);
                _dataReady.Set();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("System audio capture stopped: " + ex.Message);
        }
    }

    public int Read(Span<byte> buffer)
    {
        while (true)
        {
            if (_current is not null && _currentOffset < _current.Length)
            {
                int available = _current.Length - _currentOffset;
                int toCopy = Math.Min(buffer.Length, available);
                _current.AsSpan(_currentOffset, toCopy).CopyTo(buffer);
                _currentOffset += toCopy;
                if (_currentOffset >= _current.Length)
                    _current = null;
                return toCopy;
            }

            if (!_queue.TryDequeue(out var next))
            {
                _dataReady.Reset();
                if (!_dataReady.WaitOne(500))
                    return 0;
                continue;
            }

            _current = next;
            _currentOffset = 0;
        }
    }

    public void Stop()
    {
        try
        {
            _cts.Cancel();
        }
        catch
        {
            // Ignore cancellation faults.
        }
    }
}
