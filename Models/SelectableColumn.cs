using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DataLens.Models;

/// <summary>Display label is TableRef.ColumnName (e.g. "users.id" or "orders.id").</summary>
public class SelectableColumn : INotifyPropertyChanged
{
    private bool _isSelected;

    public string TableRef { get; set; } = "";
    public string TableDisplayName { get; set; } = "";
    public string ColumnName { get; set; } = "";

    public string DisplayLabel => string.IsNullOrEmpty(TableRef) ? ColumnName : $"{TableDisplayName}.{ColumnName}";

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
