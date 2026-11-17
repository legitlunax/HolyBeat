using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HolyBeat.LiveGuard.App.ViewModels;

/// <summary>A single live metric shown in the centre panel.</summary>
public sealed class MetricTile : INotifyPropertyChanged
{
    private string _value = "--";

    public MetricTile(string label, string unit = "")
    {
        Label = label;
        Unit = unit;
    }

    public string Label { get; }
    public string Unit { get; }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
                return;

            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
