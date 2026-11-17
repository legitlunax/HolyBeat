using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HolyBeat.LiveGuard.Core.Analysis;
using HolyBeat.LiveGuard.Core.Conversion;
using HolyBeat.LiveGuard.Core.Profiles;
using Microsoft.Win32;

namespace HolyBeat.LiveGuard.App;

public partial class ConverterWindow : Window
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".m4a", ".wma", ".aac"
    };

    private readonly SafetyProfile _currentUiProfile;
    private readonly ObservableCollection<ConversionItem> _items = new();
    private readonly Action<string>? _playFileCallback;
    private string? _customOutputFolder;
    private CancellationTokenSource? _cts;

    public ConverterWindow(SafetyProfile currentUiProfile, Action<string>? playFileCallback = null)
    {
        InitializeComponent();
        _currentUiProfile = currentUiProfile.Clone();
        _playFileCallback = playFileCallback;
        QueueList.ItemsSource = _items;

        PopulateProfiles();
    }

    private void PopulateProfiles()
    {
        ProfileCombo.Items.Add(new ComboBoxItem { Content = "Auto-Detect & Fix (Recommended)", Tag = "__auto__", IsSelected = true });
        ProfileCombo.Items.Add(new ComboBoxItem { Content = $"Current UI Profile ({_currentUiProfile.Name})", Tag = "__ui__" });

        foreach (var profile in ProfileLibrary.CreateDefaults().Values)
            ProfileCombo.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Id });
    }

    private void AddFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select audio files to convert to Safe",
            Filter = "Audio files|*.mp3;*.wav;*.flac;*.m4a;*.wma;*.aac|All files|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) == true)
            EnqueuePaths(dialog.FileNames);
    }

    private void AddFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select a folder of tracks to batch convert"
        };

        if (dialog.ShowDialog(this) == true)
        {
            var files = Directory.GetFiles(dialog.FolderName, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
                .OrderBy(f => f);
            EnqueuePaths(files);
        }
    }

    private void EnqueuePaths(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (string path in paths)
        {
            if (_items.Any(i => string.Equals(i.SourcePath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            _items.Add(new ConversionItem(path));
            added++;
        }

        if (added > 0 && string.IsNullOrEmpty(_customOutputFolder) && _items.Count > 0)
        {
            string defaultDir = Path.Combine(Path.GetDirectoryName(_items[0].SourcePath) ?? ".", "Safe");
            OutputFolderText.Text = defaultDir;
        }

        ConvertButton.IsEnabled = _items.Count > 0;
        ProgressText.Text = $"{_items.Count} track(s) queued.";
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _items.Clear();
        ConvertButton.IsEnabled = false;
        PlayConvertedButton.IsEnabled = false;
        ProgressBar.Value = 0;
        ProgressText.Text = "Ready. Add files or a folder to convert.";
    }

    private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select output folder for safe converted tracks"
        };

        if (dialog.ShowDialog(this) == true)
        {
            _customOutputFolder = dialog.FolderName;
            OutputFolderText.Text = _customOutputFolder;
        }
    }

    private async void ConvertButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0)
            return;

        SetBusy(true);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var format = GetSelectedFormat();
        string ext = format == OutputAudioFormat.Mp3 ? ".mp3" : ".wav";
        string? profileTag = (ProfileCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "__auto__";
        bool autoProfile = profileTag == "__auto__";
        SafetyProfile? overrideProfile = null;

        if (profileTag == "__ui__")
            overrideProfile = _currentUiProfile.Clone();
        else if (!autoProfile && ProfileLibrary.CreateDefaults().TryGetValue(profileTag, out var chosen))
            overrideProfile = chosen.Clone();

        var options = new ConversionOptions
        {
            AutoSelectProfile = autoProfile,
            OverrideProfile = overrideProfile,
            Format = format
        };

        var converter = new SafeAudioConverter();
        int total = _items.Count;
        int completed = 0;

        try
        {
            for (int i = 0; i < total; i++)
            {
                token.ThrowIfCancellationRequested();
                var item = _items[i];
                item.Status = "Converting...";

                string targetDir = !string.IsNullOrEmpty(_customOutputFolder)
                    ? _customOutputFolder
                    : Path.Combine(Path.GetDirectoryName(item.SourcePath) ?? ".", "Safe");
                Directory.CreateDirectory(targetDir);

                string baseName = Path.GetFileNameWithoutExtension(item.SourcePath);
                string outPath = Path.Combine(targetDir, baseName + " (Safe)" + ext);

                int index = i;
                var progress = new Progress<double>(p =>
                {
                    ProgressBar.Value = (index + p) / total;
                    ProgressText.Text = $"Converting {index + 1}/{total}: {item.FileName} ({p * 100:0}%)";
                });

                try
                {
                    var report = await Task.Run(() =>
                        converter.ConvertFile(item.SourcePath, outPath, options, progress, token), token);

                    item.OutputPath = report.OutputPath;
                    item.Status = "Safe";
                    item.Category = FormatCategory(report.DetectedCategory);
                    item.ProfileApplied = report.AppliedProfileName;
                    item.LufsChange = $"{report.SourceLufs:0.0} → {report.OutputLufs:0.0}";
                    item.HarshChange = $"{report.SourceHarshnessDb:0.0} → {report.OutputHarshnessDb:0.0}";
                    completed++;
                }
                catch (OperationCanceledException)
                {
                    item.Status = "Cancelled";
                    throw;
                }
                catch (Exception ex)
                {
                    item.Status = $"Error: {ex.Message}";
                }
            }

            ProgressBar.Value = 1.0;
            ProgressText.Text = $"Done. Converted {completed}/{total} track(s) to Safe.";
            PlayConvertedButton.IsEnabled = _items.Any(i => !string.IsNullOrEmpty(i.OutputPath));
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = $"Cancelled after {completed}/{total} track(s).";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        CancelButton.IsEnabled = false;
    }

    private void PlayConvertedButton_Click(object sender, RoutedEventArgs e)
    {
        var item = QueueList.SelectedItem as ConversionItem
            ?? _items.FirstOrDefault(i => !string.IsNullOrEmpty(i.OutputPath));

        if (item is not null && !string.IsNullOrEmpty(item.OutputPath) && File.Exists(item.OutputPath))
            _playFileCallback?.Invoke(item.OutputPath);
    }

    private void QueueList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (QueueList.SelectedItem is ConversionItem item &&
            !string.IsNullOrEmpty(item.OutputPath) &&
            File.Exists(item.OutputPath))
        {
            _playFileCallback?.Invoke(item.OutputPath);
        }
    }

    private OutputAudioFormat GetSelectedFormat()
    {
        string? tag = (FormatCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        return tag switch
        {
            "WavFloat32" => OutputAudioFormat.WavFloat32,
            "Mp3" => OutputAudioFormat.Mp3,
            _ => OutputAudioFormat.Wav24
        };
    }

    private void SetBusy(bool busy)
    {
        AddFilesButton.IsEnabled = !busy;
        AddFolderButton.IsEnabled = !busy;
        ClearButton.IsEnabled = !busy;
        BrowseOutputButton.IsEnabled = !busy;
        ProfileCombo.IsEnabled = !busy;
        FormatCombo.IsEnabled = !busy;
        ConvertButton.IsEnabled = !busy && _items.Count > 0;
        CancelButton.IsEnabled = busy;
    }

    private static string FormatCategory(AcousticProfileKind kind) => kind switch
    {
        AcousticProfileKind.DarkNoise => "Dark Noise (100% Danger)",
        AcousticProfileKind.Noise => "Noise (Headache Risk)",
        AcousticProfileKind.Mute => "Mute (Isolation Risk)",
        AcousticProfileKind.DualLayer => "Dual-Layer (Mute+Noise)",
        _ => "Safe / Optimal"
    };

    private sealed class ConversionItem : INotifyPropertyChanged
    {
        private string _status = "Queued";
        private string _category = "-";
        private string _profileApplied = "-";
        private string _lufsChange = "-";
        private string _harshChange = "-";

        public ConversionItem(string sourcePath)
        {
            SourcePath = sourcePath;
            FileName = Path.GetFileName(sourcePath);
        }

        public string SourcePath { get; }
        public string FileName { get; }
        public string? OutputPath { get; set; }

        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
        public string Category { get => _category; set { _category = value; OnPropertyChanged(); } }
        public string ProfileApplied { get => _profileApplied; set { _profileApplied = value; OnPropertyChanged(); } }
        public string LufsChange { get => _lufsChange; set { _lufsChange = value; OnPropertyChanged(); } }
        public string HarshChange { get => _harshChange; set { _harshChange = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
