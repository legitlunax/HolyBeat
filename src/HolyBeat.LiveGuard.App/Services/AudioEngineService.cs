using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HolyBeat.LiveGuard.Core.Config;
using HolyBeat.LiveGuard.Core.Dsp;
using HolyBeat.LiveGuard.Core.Profiles;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace HolyBeat.LiveGuard.App.Services;

/// <summary>Describes an audio endpoint that can be used for capture or playback.</summary>
public sealed class AudioDeviceInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public bool IsDefault { get; init; }
    public override string ToString() => Name;
}

/// <summary>
/// Owns the audio plumbing for the three routing modes:
/// Player (built in player), System Monitor (capture and analyse only) and
/// System Wide (capture, process and re-route to another endpoint).
/// </summary>
public sealed class AudioEngineService : IDisposable
{
    private readonly DspChain _chain;
    private readonly AppConfig _config;

    private WasapiPlayer? _output;
    private WasapiRecorder? _capture;
    private WasapiRecorderProvider? _captureProvider;
    private AudioFileReader? _fileReader;
    private IWaveProvider? _fileProvider;
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;

    public AudioEngineService(DspChain chain, AppConfig config)
    {
        _chain = chain;
        _config = config;
    }

    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }
    public string CurrentFile { get; private set; } = string.Empty;
    public TimeSpan Duration { get; private set; } = TimeSpan.Zero;
    public TimeSpan Position { get; private set; } = TimeSpan.Zero;

    public event Action? StateChanged;
    public event Action<string>? ErrorRaised;

    public List<AudioDeviceInfo> RefreshDevices()
    {
        var devices = new List<AudioDeviceInfo>();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var defaultRender = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            string? defaultId = defaultRender?.ID;

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                devices.Add(new AudioDeviceInfo
                {
                    Id = device.ID,
                    Name = device.FriendlyName,
                    Detail = device.State.ToString(),
                    IsDefault = device.ID == defaultId
                });
            }
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Could not enumerate audio devices: " + ex.Message);
        }

        return devices;
    }

    public MMDevice? FindRenderDevice(string? deviceId)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (string.IsNullOrEmpty(deviceId))
                return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                if (device.ID == deviceId)
                    return device;
            }

            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Could not open the audio device: " + ex.Message);
            return null;
        }
    }

    public void ApplyProfile(SafetyProfile profile) => _chain.ApplyProfile(profile);

    public void SetBypass(bool bypass) => _chain.Bypass = bypass;

    public void SetMasterVolume(double volume)
    {
        if (_output is not null)
            _output.Volume = (float)Math.Clamp(volume, 0.0, 1.0);
    }

    public void SetMode(RoutingMode mode)
    {
        StopSystemWide();
        StopCapture();
        StopPlayer();

        _config.Routing.Mode = mode;

        switch (mode)
        {
            case RoutingMode.SystemMonitor:
                StartCapture(passThrough: true);
                break;
            case RoutingMode.SystemWide:
                StartCapture(passThrough: false);
                break;
            case RoutingMode.Player:
            default:
                break;
        }

        StateChanged?.Invoke();
    }

    private void StartCapture(bool passThrough)
    {
        try
        {
            var device = FindRenderDevice(_config.Routing.InputDeviceId);
            if (device is null)
                return;

            var recorder = new WasapiRecorderBuilder()
                .WithDevice(device)
                .WithSharedMode()
                .WithEventSync()
                .WithLoopbackCapture()
                .Build();

            var provider = new WasapiRecorderProvider(recorder);

            if (!passThrough)
            {
                var processed = new DspWaveProvider(provider, _chain);
                var outputDevice = FindRenderDevice(_config.Routing.OutputDeviceId);
                if (outputDevice is null)
                {
                    recorder.Dispose();
                    return;
                }

                var player = BuildPlayer(outputDevice);
                player.Init(processed);
                player.Play();
                _output = player;
            }
            else
            {
                // Monitor only: pull the captured audio so the analyser and the detector stay live.
                var processed = new DspWaveProvider(provider, _chain);
                _monitorCts = new CancellationTokenSource();
                CancellationToken token = _monitorCts.Token;
                _monitorTask = Task.Run(() =>
                {
                    var scratch = new byte[16384];
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            int read = processed.Read(scratch.AsSpan(0, scratch.Length));
                            if (read <= 0)
                                break;
                        }
                    }
                    catch
                    {
                        // The capture device can disappear while running.
                    }
                }, token);
            }

            recorder.StartRecording();
            _capture = recorder;
            _captureProvider = provider;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("System audio capture failed: " + ex.Message);
        }
    }

    private WasapiPlayer BuildPlayer(MMDevice device)
    {
        var player = new WasapiPlayerBuilder()
            .WithDevice(device)
            .WithSharedMode()
            .WithLatency(120)
            .Build();

        player.Volume = (float)Math.Clamp(_config.Routing.MasterVolume, 0.0, 1.0);
        return player;
    }

    public void OpenFile(string path)
    {
        try
        {
            StopPlayer();

            var reader = new AudioFileReader(path);
            var device = FindRenderDevice(_config.Routing.OutputDeviceId);
            if (device is null)
            {
                reader.Dispose();
                return;
            }

            var player = BuildPlayer(device);
            WaveFormat? mixFormat = player.DeviceMixFormat;

            IWaveProvider source = reader;
            if (mixFormat is not null && !reader.WaveFormat.Equals(mixFormat))
            {
                source = new MediaFoundationResampler(reader, mixFormat);
            }

            var processed = new DspWaveProvider(source, _chain);
            player.Init(processed);
            player.Play();

            _fileReader = reader;
            _fileProvider = source;
            _output = player;
            CurrentFile = path;
            Duration = reader.TotalTime;
            Position = TimeSpan.Zero;
            IsPlaying = true;
            IsPaused = false;
            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Could not open that file: " + ex.Message);
        }
    }

    public void Pause()
    {
        if (_output is null)
            return;

        _output.Pause();
        IsPaused = true;
        StateChanged?.Invoke();
    }

    public void Resume()
    {
        if (_output is null)
            return;

        _output.Play();
        IsPaused = false;
        StateChanged?.Invoke();
    }

    public void StopPlayer()
    {
        try
        {
            _output?.Stop();
        }
        catch
        {
            // The device may already be gone.
        }

        _output?.Dispose();
        _output = null;

        if (_fileReader is not null)
        {
            _fileReader.Dispose();
            _fileReader = null;
        }

        _fileProvider = null;
        IsPlaying = false;
        IsPaused = false;
        Position = TimeSpan.Zero;
        StateChanged?.Invoke();
    }

    private void StopCapture()
    {
        try
        {
            _monitorCts?.Cancel();
            _monitorCts?.Dispose();
            _monitorCts = null;
            _monitorTask = null;
        }
        catch
        {
            // Ignore cancellation faults.
        }

        try
        {
            _capture?.StopRecording();
        }
        catch
        {
            // Ignore device removal.
        }

        _capture?.Dispose();
        _capture = null;
        _captureProvider = null;
    }

    private void StopSystemWide()
    {
        try
        {
            _output?.Stop();
        }
        catch
        {
            // Ignore device removal.
        }

        _output?.Dispose();
        _output = null;
    }

    public void RefreshPosition()
    {
        if (_fileReader is not null)
        {
            Position = _fileReader.CurrentTime;
            StateChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        StopCapture();
        StopSystemWide();
        StopPlayer();
    }
}
