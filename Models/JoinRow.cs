using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DataLens.Models;

public class JoinRow : INotifyPropertyChanged
{
    public static readonly string[] JoinTypes = { "INNER JOIN", "LEFT JOIN", "RIGHT JOIN" };

    private string _joinType = "LEFT JOIN";
    private string _relatedTableName = "";
    private string _leftTableRef = "";
    private string _leftColumn = "";
    private string _rightColumn = "";

    public string JoinType
    {
        get => _joinType;
        set { _joinType = value; OnPropertyChanged(); }
    }

    public string RelatedTableName
    {
        get => _relatedTableName;
        set { _relatedTableName = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Empty = main table, "j1" = first join, "j2" = second join, etc.</summary>
    public string LeftTableRef
    {
        get => _leftTableRef;
        set { _leftTableRef = value ?? ""; OnPropertyChanged(); }
    }

    public string LeftColumn
    {
        get => _leftColumn;
        set { _leftColumn = value ?? ""; OnPropertyChanged(); }
    }

    public string RightColumn
    {
        get => _rightColumn;
        set { _rightColumn = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Populated when RelatedTableName is set; used for RightColumn dropdown.</summary>
    public ObservableCollection<string> RelatedTableColumns { get; } = new();

    /// <summary>Populated when LeftTableRef is set; used for LeftColumn dropdown.</summary>
    public ObservableCollection<string> LeftColumnOptions { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
