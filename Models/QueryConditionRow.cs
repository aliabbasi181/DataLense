using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DataLens.Models;

public class QueryConditionRow : INotifyPropertyChanged
{
    public static readonly string[] Operators = { "=", "!=", "<", ">", "<=", ">=", "LIKE", "NOT LIKE", "IN", "IS NULL", "IS NOT NULL" };

    private string _column = "";
    private string _operator = "=";
    private string _value = "";

    public string Column
    {
        get => _column;
        set { _column = value; OnPropertyChanged(); }
    }

    public string Operator
    {
        get => _operator;
        set { _operator = value; OnPropertyChanged(); }
    }

    public string Value
    {
        get => _value;
        set { _value = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
