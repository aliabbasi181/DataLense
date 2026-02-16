using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DataLens.Models;

public class EditableCell : INotifyPropertyChanged
{
    private string _newValue = "";

    public string ColumnName { get; set; } = "";

    /// <summary>Original raw value from the DataRow (can be DBNull).</summary>
    public object? OriginalValueRaw { get; set; }

    /// <summary>Original value as string for display.</summary>
    public string OriginalValueDisplay { get; set; } = "";

    /// <summary>Editable value as string.</summary>
    public string NewValue
    {
        get => _newValue;
        set
        {
            if (_newValue == value) return;
            _newValue = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsChanged));
        }
    }

    /// <summary>True if the edited value differs from the original display value.</summary>
    public bool IsChanged => !string.Equals(OriginalValueDisplay ?? "", NewValue ?? "", StringComparison.Ordinal);

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

