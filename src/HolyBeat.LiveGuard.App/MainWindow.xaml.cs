using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using System.Windows.Threading;
using HolyBeat.LiveGuard.App.Services;
using HolyBeat.LiveGuard.App.ViewModels;
using HolyBeat.LiveGuard.Core.Analysis;
using HolyBeat.LiveGuard.Core.Config;
using HolyBeat.LiveGuard.Core.Dsp;
using HolyBeat.LiveGuard.Core.Profiles;
using Microsoft.Win32;

namespace HolyBeat.LiveGuard.App;

public partial class MainWindow : Window
{
    private readonly AppConfig _config;
    private readonly DspChain _chain;
    private readonly AudioEngineService _engine;
    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _saveTimer;
    private readonly ObservableCollection<string> _reasons = new ObservableCollection<string>();
    private readonly List<MetricTile> _tiles = new List<MetricTile>();
    private readonly List<AudioDeviceInfo> _devices = new List<AudioDeviceInfo>();

    private SafetyProfile _profile;
    // True from construction until the profile has been pushed into the controls:
    // slider ValueChanged also fires while the XAML tree is being built.
    private bool _updatingUi = true;
    private bool _ready;
    private bool _reallyExit;
    private TrayIconService? _tray;

    public MainWindow()
    {
        _config = App.Config;
        _profile = GetProfile(_config.ActiveProfileId);

        InitializeComponent();

        _chain = new DspChain(_profile);
        _engine = new AudioEngineService(_chain, _config);
        _engine.ErrorRaised += message => Dispatcher.Invoke(() => StatusText.Text = message);

        BuildMetricTiles();
        MetricTiles.ItemsSource = _tiles;
        ReasonList.ItemsSource = _reasons;

        BuildProfileList();
        LoadProfileIntoUi(_profile);

        AutoProfileCheck.IsChecked = _config.AutoProfile;
        RunAtStartupCheck.IsChecked = _config.RunAtStartup;
        VolumeSlider.Value = _config.Routing.MasterVolume;

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _uiTimer.Tick += (_, _) => RefreshLiveUi();
        _uiTimer.Start();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveConfig();
        };

        RefreshDevices();
        SelectModeCombo(_config.Routing.Mode);
        SetupTray();

        _ready = true;

        // Write the configuration on first launch so the file always exists.
        SaveConfig();
    }

    // ------------------------------------------------------------------ setup

    private SafetyProfile GetProfile(string id) =>
        _config.Profiles.TryGetValue(id, out var profile) ? profile : ProfileLibrary.CreateDefaults()[ProfileLibrary.Safe];

    private void BuildProfileList()
    {
        var ordered = ProfileLibrary.DefaultOrder()
            .Where(id => _config.Profiles.ContainsKey(id))
            .Select(id => _config.Profiles[id])
            .ToList();

        foreach (var extra in _config.Profiles.Values.Where(p => !ProfileLibrary.DefaultOrder().Contains(p.Id)))
            ordered.Add(extra);

        ProfileList.ItemsSource = ordered;
    }

    private void BuildMetricTiles()
    {
        _tiles.Add(new MetricTile("Integrated", "LUFS"));
        _tiles.Add(new MetricTile("True peak", "dBTP"));
        _tiles.Add(new MetricTile("Crest factor", "dB"));
        _tiles.Add(new MetricTile("Sub pressure", "%"));
        _tiles.Add(new MetricTile("Harshness", "dB"));
        _tiles.Add(new MetricTile("Pumping depth", "dB"));
        _tiles.Add(new MetricTile("HF side ratio", ""));
        _tiles.Add(new MetricTile("Instability", ""));
    }

    private void SetupTray()
    {
        try
        {
            _tray = new TrayIconService(this, "HolyBeat Live Guard", new List<TrayMenuItem>
            {
                new TrayMenuItem { Header = "Show HolyBeat Live Guard", Click = () => Dispatcher.Invoke(RestoreWindow) },
                new TrayMenuItem { Header = "Bypass (A/B)", Click = () => Dispatcher.Invoke(ToggleBypass) },
                new TrayMenuItem { IsSeparator = true },
                new TrayMenuItem
                {
                    Header = "Exit",
                    Click = () => Dispatcher.Invoke(() =>
                    {
                        _reallyExit = true;
                        Close();
                    })
                }
            });

            _tray.DoubleClick += () => Dispatcher.Invoke(RestoreWindow);
        }
        catch (Exception)
        {
            // A tray icon is a convenience only; the app works without it.
        }
    }

    private void RestoreWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    // ------------------------------------------------------------------ live UI

    private void RefreshLiveUi()
    {
        var output = _chain.OutputAnalysis;
        var source = _chain.SourceAnalysis;
        var detection = _chain.Detection;

        Spectrum.Update(BuildBars(output.SpectrumDb, output.SampleRate, 64));
        StageMap.Update(_chain.StageBusLevels);
        SpectrumCaption.Text = $"{output.SampleRate / 1000.0:0.0} kHz  -  limiter {(_chain.LimiterGainReductionDb):0.0} dB  -  de-harsh {(_chain.DeHarshGainDb):0.0} dB  -  dark guard {(_chain.DarkNoiseSuppressionDb):0.0} dB  -  warm tail {(_chain.WarmTailBlend * 100):0} %";

        _tiles[0].Value = output.IntegratedLufs.ToString("0.0");
        _tiles[1].Value = Math.Max(output.TruePeakDb, source.TruePeakDb).ToString("0.0");
        _tiles[2].Value = source.CrestFactorDb.ToString("0.0");
        _tiles[3].Value = (source.SubPressure * 100).ToString("0");
        _tiles[4].Value = source.HarshnessDb.ToString("0.0");
        _tiles[5].Value = source.PumpingDepthDb.ToString("0.0");
        _tiles[6].Value = source.HighFrequencySideRatio.ToString("0.00");
        _tiles[7].Value = source.DarkInstability.ToString("0.00");

        DetectedChipText.Text = "DETECTED: " + detection.Kind.ToString().ToUpperInvariant();
        ClassificationText.Text = detection.Kind.ToString().ToUpperInvariant();
        ConfidenceText.Text = $"confidence {(detection.Confidence * 100):0} %";
        ConfidenceBar.Value = detection.Confidence;

        ClassificationText.Foreground = new SolidColorBrush(Color.FromRgb(
            detection.Kind switch
            {
                AcousticProfileKind.Safe => (byte)0x37,
                AcousticProfileKind.Mute => (byte)0x5B,
                AcousticProfileKind.Noise => (byte)0xFF,
                AcousticProfileKind.DarkNoise => (byte)0xB9,
                _ => (byte)0xFF
            },
            detection.Kind switch
            {
                AcousticProfileKind.Safe => (byte)0xD6,
                AcousticProfileKind.Mute => (byte)0x8C,
                AcousticProfileKind.Noise => (byte)0x7A,
                AcousticProfileKind.DarkNoise => (byte)0x8C,
                _ => (byte)0x5F
            },
            detection.Kind switch
            {
                AcousticProfileKind.Safe => (byte)0xB0,
                AcousticProfileKind.Mute => (byte)0xFF,
                AcousticProfileKind.Noise => (byte)0x59,
                AcousticProfileKind.DarkNoise => (byte)0xFF,
                _ => (byte)0xA2
            }));

        _reasons.Clear();
        foreach (string reason in detection.Reasons)
            _reasons.Add(reason);

        if (detection.Reasons.Count == 0)
            _reasons.Add("Listening...");

        if (_config.AutoProfile && detection.Confidence > 0.45 && detection.RecommendedProfileId != _profile.Id)
            ActivateProfile(detection.RecommendedProfileId);

        _engine.RefreshPosition();
        StatusText.Text = string.IsNullOrEmpty(_engine.CurrentFile)
            ? $"Mode {_config.Routing.Mode} - {_engine.Duration:g} - ready"
            : $"{System.IO.Path.GetFileName(_engine.CurrentFile)}  {_engine.Position.ToString(@"mm\:ss")} / {_engine.Duration.ToString(@"mm\:ss")}";
    }

    private static float[] BuildBars(float[] spectrum, int sampleRate, int barCount)
    {
        var bars = new float[barCount];
        for (int i = 0; i < barCount; i++)
            bars[i] = -120f;

        if (spectrum.Length < 2 || sampleRate <= 0)
            return bars;

        const double minFrequency = 20.0;
        const double maxFrequency = 20000.0;
        double binWidth = (double)sampleRate / (spectrum.Length * 2);

        for (int i = 0; i < barCount; i++)
        {
            double low = minFrequency * Math.Pow(maxFrequency / minFrequency, i / (double)barCount);
            double high = minFrequency * Math.Pow(maxFrequency / minFrequency, (i + 1) / (double)barCount);
            int first = Math.Max(1, (int)(low / binWidth));
            int last = Math.Min(spectrum.Length - 1, (int)(high / binWidth));

            double sum = 0.0;
            int count = 0;
            for (int bin = first; bin <= last; bin++)
            {
                sum += spectrum[bin];
                count++;
            }

            bars[i] = count > 0 ? (float)(sum / count) : -120f;
        }

        return bars;
    }

    // ------------------------------------------------------------------ profiles

    private void ProfileCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string id)
            ActivateProfile(id);
    }

    private void ActivateProfile(string id)
    {
        if (!_config.Profiles.TryGetValue(id, out var profile))
            return;

        _profile = profile;
        _config.ActiveProfileId = id;
        _config.AutoProfile = false;
        AutoProfileCheck.IsChecked = false;

        LoadProfileIntoUi(profile);
        _engine.ApplyProfile(profile);
        ActiveProfileText.Text = profile.Name;
        QueueSave();
    }

    private void LoadProfileIntoUi(SafetyProfile profile)
    {
        _updatingUi = true;
        try
        {
            ChkStage.IsChecked = profile.EnableVirtualStage;
            SldStageWidth.Value = profile.StageWidth;
            SldStageHeight.Value = profile.StageHeight;
            SldStageCentre.Value = profile.StageCentreFocus;
            SldStageLfe.Value = profile.StageLfeMix;
            SldStageSurround.Value = profile.StageSurroundMix;

            ChkHighPass.IsChecked = profile.EnableHighPass;
            SldHighPass.Value = profile.HighPassHz;
            ChkMidGlue.IsChecked = profile.EnableMidGlue;
            SldMidGlueDb.Value = profile.MidGlueDb;
            SldMidGlueHz.Value = profile.MidGlueHz;
            ChkHighShelf.IsChecked = profile.EnableHighShelf;
            SldHighShelfDb.Value = profile.HighShelfDb;
            SldHighShelfHz.Value = profile.HighShelfHz;

            ChkDeHarsh.IsChecked = profile.EnableDeHarsh;
            SldDeHarshLow.Value = profile.DeHarshLowHz;
            SldDeHarshHigh.Value = profile.DeHarshHighHz;
            SldDeHarshThreshold.Value = profile.DeHarshThresholdDb;
            SldDeHarshRatio.Value = profile.DeHarshRatio;
            SldDeHarshMax.Value = profile.DeHarshMaxReductionDb;

            ChkExpansion.IsChecked = profile.EnableExpansion;
            SldExpansionBoost.Value = profile.ExpansionMaxBoostDb;
            SldExpansionSens.Value = profile.ExpansionSensitivity;
            SldExpansionAttack.Value = profile.ExpansionAttackMs;
            SldExpansionRelease.Value = profile.ExpansionReleaseMs;

            ChkSub.IsChecked = profile.EnableSubTightening;
            SldSubThreshold.Value = profile.SubThresholdDb;
            SldSubMax.Value = profile.SubMaxReductionDb;
            SldSubAttack.Value = profile.SubAttackMs;
            SldSubRelease.Value = profile.SubReleaseMs;
            ChkMonoBass.IsChecked = profile.EnableMonoBass;
            SldMonoBassHz.Value = profile.MonoBassHz;
            ChkNarrowing.IsChecked = profile.EnableHighFrequencyNarrowing;
            SldHfWidth.Value = profile.HighFrequencyWidth;

            ChkDarkNoiseGuard.IsChecked = profile.EnableDarkNoiseGuard;
            SldDarkGuardStrength.Value = profile.DarkNoiseGuardStrength;
            SldDarkGuardMaxDb.Value = profile.DarkNoiseGuardMaxDb;

            ChkWarmTail.IsChecked = profile.EnableWarmTail;
            SldHoldMs.Value = profile.WarmTailHoldMs;
            SldCloseMs.Value = profile.WarmTailCloseMs;
            SldReopenMs.Value = profile.WarmTailReopenMs;
            SldClosedHz.Value = profile.WarmTailClosedHz;
            ChkSaturation.IsChecked = profile.EnableSaturation;
            SldSaturationDrive.Value = profile.SaturationDrive;
            ChkRoom.IsChecked = profile.EnableRoom;
            SldRoomMix.Value = profile.RoomMix;

            ChkLoudness.IsChecked = profile.EnableLoudnessTarget;
            SldTargetLufs.Value = profile.TargetLufs;
            SldTruePeak.Value = profile.TruePeakCeilingDbTp;
            SldOutputGain.Value = profile.OutputGainDb;

            ActiveProfileText.Text = profile.Name;
        }
        finally
        {
            _updatingUi = false;
        }

        _engine.ApplyProfile(profile);
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        var defaults = ProfileLibrary.CreateDefaults();
        if (defaults.TryGetValue(_profile.Id, out var preset))
        {
            var fresh = preset.Clone();
            _config.Profiles[_profile.Id] = fresh;
            _profile = fresh;
            LoadProfileIntoUi(fresh);
            QueueSave();
            StatusText.Text = "Research defaults restored for this profile.";
        }
    }

    // ------------------------------------------------------------------ parameter edits

    private void Check_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingUi)
            return;

        _profile.EnableVirtualStage = ChkStage.IsChecked == true;
        _profile.EnableHighPass = ChkHighPass.IsChecked == true;
        _profile.EnableMidGlue = ChkMidGlue.IsChecked == true;
        _profile.EnableHighShelf = ChkHighShelf.IsChecked == true;
        _profile.EnableDeHarsh = ChkDeHarsh.IsChecked == true;
        _profile.EnableExpansion = ChkExpansion.IsChecked == true;
        _profile.EnableSubTightening = ChkSub.IsChecked == true;
        _profile.EnableMonoBass = ChkMonoBass.IsChecked == true;
        _profile.EnableHighFrequencyNarrowing = ChkNarrowing.IsChecked == true;
        _profile.EnableDarkNoiseGuard = ChkDarkNoiseGuard.IsChecked == true;
        _profile.EnableWarmTail = ChkWarmTail.IsChecked == true;
        _profile.EnableSaturation = ChkSaturation.IsChecked == true;
        _profile.EnableRoom = ChkRoom.IsChecked == true;
        _profile.EnableLoudnessTarget = ChkLoudness.IsChecked == true;

        ApplyProfileEdits();
    }

    private void SldStageWidth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.StageWidth = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldStageHeight_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.StageHeight = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldStageCentre_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.StageCentreFocus = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldStageLfe_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.StageLfeMix = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldStageSurround_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.StageSurroundMix = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldHighPass_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.HighPassHz = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldMidGlueDb_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.MidGlueDb = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldMidGlueHz_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.MidGlueHz = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldHighShelfDb_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.HighShelfDb = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldHighShelfHz_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.HighShelfHz = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldDeHarshLow_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.DeHarshLowHz = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldDeHarshHigh_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.DeHarshHighHz = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldDeHarshThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.DeHarshThresholdDb = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldDeHarshRatio_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.DeHarshRatio = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldDeHarshMax_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.DeHarshMaxReductionDb = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldExpansionBoost_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.ExpansionMaxBoostDb = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldExpansionSens_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.ExpansionSensitivity = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldExpansionAttack_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.ExpansionAttackMs = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldExpansionRelease_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.ExpansionReleaseMs = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldSubThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.SubThresholdDb = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldSubMax_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.SubMaxReductionDb = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldSubAttack_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.SubAttackMs = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldSubRelease_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.SubReleaseMs = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldMonoBassHz_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.MonoBassHz = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldHfWidth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.HighFrequencyWidth = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldDarkGuardStrength_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.DarkNoiseGuardStrength = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldDarkGuardMaxDb_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.DarkNoiseGuardMaxDb = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldHoldMs_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.WarmTailHoldMs = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldCloseMs_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.WarmTailCloseMs = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldReopenMs_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.WarmTailReopenMs = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldClosedHz_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.WarmTailClosedHz = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldSaturationDrive_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.SaturationDrive = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldRoomMix_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.RoomMix = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldTargetLufs_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.TargetLufs = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldTruePeak_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.TruePeakCeilingDbTp = e.NewValue;
        ApplyProfileEdits();
    }

    private void SldOutputGain_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        _profile.OutputGainDb = e.NewValue;
        ApplyProfileEdits();
    }

    private void ApplyProfileEdits()
    {
        // Slider value changes also fire while the XAML tree is being built.
        if (!_ready)
            return;

        _engine.ApplyProfile(_profile);
        QueueSave();
    }

    // ------------------------------------------------------------------ transport and routing

    private void ConvertButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new ConverterWindow(_profile, convertedPath =>
        {
            _config.Routing.Mode = RoutingMode.Player;
            _engine.OpenFile(convertedPath);
            PlayButton.Content = "Pause";
        })
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose an audio file",
            Filter = "Audio files|*.wav;*.mp3;*.flac;*.ogg;*.m4a;*.aac|All files|*.*"
        };

        if (dialog.ShowDialog(this) == true)
            _engine.OpenFile(dialog.FileName);
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_engine.CurrentFile))
        {
            OpenFileButton_Click(sender, e);
            return;
        }

        if (_engine.IsPaused)
        {
            _engine.Resume();
            PlayButton.Content = "Pause";
        }
        else if (_engine.IsPlaying)
        {
            _engine.Pause();
            PlayButton.Content = "Play";
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _engine.StopPlayer();
        PlayButton.Content = "Play";
    }

    private void BypassButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleBypass();
    }

    private void ToggleBypass()
    {
        if (!_ready)
            return;

        _config.Bypass = !_config.Bypass;
        _engine.SetBypass(_config.Bypass);
        BypassButton.Content = _config.Bypass ? "Bypassed (click to engage)" : "Bypass (A/B)";
        BypassButton.Background = _config.Bypass
            ? new SolidColorBrush(Color.FromRgb(0x5A, 0x2A, 0x24))
            : (Brush)FindResource("PanelAltBrush");
        QueueSave();
    }

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || !_ready)
            return;

        RoutingMode mode = ModeCombo.SelectedIndex switch
        {
            1 => RoutingMode.SystemMonitor,
            2 => RoutingMode.SystemWide,
            _ => RoutingMode.Player
        };

        ModeHint.Text = mode switch
        {
            RoutingMode.SystemMonitor => "Captures system audio and analyses it; playback stays untouched.",
            RoutingMode.SystemWide => "Captures, processes and re-routes system audio to the output device.",
            _ => "Processes the built-in player."
        };

        _engine.SetMode(mode);
        QueueSave();
    }

    private void SelectModeCombo(RoutingMode mode)
    {
        _updatingUi = true;
        ModeCombo.SelectedIndex = mode switch
        {
            RoutingMode.SystemMonitor => 1,
            RoutingMode.SystemWide => 2,
            _ => 0
        };
        _updatingUi = false;
        ModeHint.Text = mode switch
        {
            RoutingMode.SystemMonitor => "Captures system audio and analyses it; playback stays untouched.",
            RoutingMode.SystemWide => "Captures, processes and re-routes system audio to the output device.",
            _ => "Processes the built-in player."
        };
    }

    private void RefreshDevices_Click(object sender, RoutedEventArgs e) => RefreshDevices();

    private void RefreshDevices()
    {
        _devices.Clear();
        _devices.AddRange(_engine.RefreshDevices());

        CaptureCombo.ItemsSource = _devices.ToList();
        OutputCombo.ItemsSource = _devices.ToList();

        if (_devices.Count > 0)
        {
            int captureIndex = _devices.FindIndex(d => d.Id == _config.Routing.InputDeviceId);
            int outputIndex = _devices.FindIndex(d => d.Id == _config.Routing.OutputDeviceId);

            CaptureCombo.SelectedIndex = captureIndex >= 0 ? captureIndex : 0;
            OutputCombo.SelectedIndex = outputIndex >= 0 ? outputIndex : 0;
        }
    }

    private void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || !_ready)
            return;

        if (CaptureCombo.SelectedItem is AudioDeviceInfo capture)
            _config.Routing.InputDeviceId = capture.Id;

        if (OutputCombo.SelectedItem is AudioDeviceInfo output)
            _config.Routing.OutputDeviceId = output.Id;

        QueueSave();
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi || !_ready)
            return;

        _config.Routing.MasterVolume = e.NewValue;
        QueueSave();
    }

    private void AutoProfileCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready)
            return;

        _config.AutoProfile = AutoProfileCheck.IsChecked == true;
        QueueSave();
    }

    private void RunAtStartupCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready)
            return;

        _config.RunAtStartup = RunAtStartupCheck.IsChecked == true;
        QueueSave();
    }

    // ------------------------------------------------------------------ lifecycle

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int value = 1;
            _ = DwmSetWindowAttribute(handle, 20, ref value, sizeof(int));
        }
        catch
        {
            // Dark title bar is cosmetic only.
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!_reallyExit && _config.Ui.CloseToTray && _tray is not null)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        SaveConfig();
        _engine.Dispose();
        _tray?.Dispose();
    }

    private void QueueSave()
    {
        if (!_ready)
            return;

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveConfig()
    {
        try
        {
            ConfigStore.Save(_config);
        }
        catch (Exception)
        {
            // A failed save must never take the app down.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
