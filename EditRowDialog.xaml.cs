using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows;
using DataLens.Models;

namespace DataLens;

public partial class EditRowDialog : Window
{
    private readonly string _tableName;
    private readonly ObservableCollection<EditableCell> _cells;
    private readonly IReadOnlyList<string> _mainTableColumns;
    private readonly IReadOnlyList<string> _primaryKeyColumns;

    public EditRowDialog(string tableName, ObservableCollection<EditableCell> cells,
        IReadOnlyList<string> mainTableColumns, IReadOnlyList<string> primaryKeyColumns)
    {
        InitializeComponent();
        _tableName = tableName;
        _cells = cells;
        _mainTableColumns = mainTableColumns;
        _primaryKeyColumns = primaryKeyColumns;
        EditItemsControl.ItemsSource = _cells;
    }

    private void BtnGenerate_OnClick(object sender, RoutedEventArgs e)
    {
        var changedColumns = _cells
            .Where(c => c.IsChanged && _mainTableColumns.Contains(c.ColumnName))
            .ToList();
        if (changedColumns.Count == 0)
        {
            MessageBox.Show("No changes — edit at least one column to generate an UPDATE query.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_primaryKeyColumns.Count == 0)
        {
            MessageBox.Show("This table has no primary key. Cannot build a safe UPDATE with a unique WHERE.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var safeTable = SanitizeIdentifier(_tableName);
        var sb = new StringBuilder();
        sb.Append("UPDATE `").Append(safeTable).Append("` SET ");
        var setParts = changedColumns
            .Select(cell => "`" + SanitizeIdentifier(cell.ColumnName) + "` = " + QuoteValue(cell.NewValue));
        sb.Append(string.Join(", ", setParts));

        var whereParts = new List<string>();
        foreach (var colName in _primaryKeyColumns)
        {
            var cell = _cells.FirstOrDefault(c => c.ColumnName == colName);
            var safeCol = SanitizeIdentifier(colName);
            if (cell == null || cell.OriginalValueRaw == null || cell.OriginalValueRaw == DBNull.Value)
                whereParts.Add("`" + safeCol + "` IS NULL");
            else
                whereParts.Add("`" + safeCol + "` = " + QuoteValue(cell.OriginalValueDisplay));
        }
        sb.Append(" WHERE ").Append(string.Join(" AND ", whereParts));

        TxtGeneratedUpdate.Text = sb.ToString();
    }

    private void BtnCopy_OnClick(object sender, RoutedEventArgs e)
    {
        var text = TxtGeneratedUpdate.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            MessageBox.Show("Generate an UPDATE query first.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            Clipboard.SetText(text);
            MessageBox.Show("UPDATE query copied to clipboard.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not copy: " + ex.Message, "DataLens", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnClose_OnClick(object sender, RoutedEventArgs e) => Close();

    private static string SanitizeIdentifier(string value) =>
        string.Join("", (value ?? "").Where(c => char.IsLetterOrDigit(c) || c == '_'));

    private static string QuoteValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return "NULL";
        if (long.TryParse(value, out _) || double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
            return value;
        return "'" + (value ?? "").Replace("\\", "\\\\").Replace("'", "''") + "'";
    }
}
