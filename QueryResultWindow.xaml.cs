using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using DataLens.Models;
using Microsoft.Win32;

namespace DataLens;

public partial class QueryResultWindow : Window
{
    private readonly Func<string, Task<IReadOnlyList<string>>>? _getPrimaryKeyColumnsAsync;
    private readonly Action<string>? _onStatus;

    public QueryResultWindow(string title, string queryText, DataTable? resultSet, string message,
        string? tableName = null,
        Func<string, Task<IReadOnlyList<string>>>? getPrimaryKeyColumnsAsync = null,
        Action<string>? onStatus = null)
    {
        InitializeComponent();
        Title = title;
        TxtTitle.Text = title;
        TxtQuery.Text = queryText;
        TableName = tableName;
        _getPrimaryKeyColumnsAsync = getPrimaryKeyColumnsAsync;
        _onStatus = onStatus;

        if (resultSet != null)
        {
            DataGridResult.ItemsSource = resultSet.DefaultView;
            DataGridResult.Visibility = Visibility.Visible;
            TxtMessage.Visibility = Visibility.Collapsed;
            TxtTitle.Text = title + $" ({resultSet.Rows.Count} row(s))";
        }
        else
        {
            TxtMessage.Text = message;
            TxtMessage.Visibility = Visibility.Visible;
            DataGridResult.Visibility = Visibility.Collapsed;
        }
    }

    private string? TableName { get; }

    private IEnumerable<DataRowView> GetRowsToExport()
    {
        if (DataGridResult.ItemsSource is not DataView dv)
            yield break;
        if (DataGridResult.SelectedItems.Count > 0)
        {
            foreach (var item in DataGridResult.SelectedItems)
                if (item is DataRowView rowView)
                    yield return rowView;
        }
        else
        {
            foreach (DataRowView rowView in dv)
                yield return rowView;
        }
    }

    private static string EscapeCsvField(string value)
    {
        if (value == null) return "\"\"";
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (DataGridResult.ItemsSource is not DataView dv)
        {
            MessageBox.Show("No data to export.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var rows = GetRowsToExport().ToList();
        if (rows.Count == 0)
        {
            MessageBox.Show("No rows to export.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var fileName = (string.IsNullOrEmpty(TableName) ? "query_result" : TableName) + ".csv";
        var dlg = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*", DefaultExt = "csv", FileName = fileName };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var table = dv.Table ?? throw new InvalidOperationException("No table");
            var columns = table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", columns.Select(EscapeCsvField)));
            foreach (var rowView in rows)
            {
                var values = columns.Select(col => EscapeCsvField(rowView[col] == DBNull.Value ? "" : rowView[col]?.ToString() ?? ""));
                sb.AppendLine(string.Join(",", values));
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            _onStatus?.Invoke($"Exported {rows.Count} row(s) to CSV");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Export failed: " + ex.Message, "DataLens", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnExportJson_Click(object sender, RoutedEventArgs e)
    {
        if (DataGridResult.ItemsSource is not DataView dv)
        {
            MessageBox.Show("No data to export.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var rows = GetRowsToExport().ToList();
        if (rows.Count == 0)
        {
            MessageBox.Show("No rows to export.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var fileName = (string.IsNullOrEmpty(TableName) ? "query_result" : TableName) + ".json";
        var dlg = new SaveFileDialog { Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*", DefaultExt = "json", FileName = fileName };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var table = dv.Table ?? throw new InvalidOperationException("No table");
            var columns = table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
            var list = new List<Dictionary<string, object?>>();
            foreach (var rowView in rows)
            {
                var dict = new Dictionary<string, object?>();
                foreach (var col in columns)
                    dict[col] = rowView[col] == DBNull.Value ? null : rowView[col];
                list.Add(dict);
            }
            File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            _onStatus?.Invoke($"Exported {rows.Count} row(s) to JSON");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Export failed: " + ex.Message, "DataLens", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? TryParseTableNameFromSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return null;
        var m = Regex.Match(sql, @"FROM\s+[`]?(\w+)[`]?\s", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value : null;
    }

    private async void BtnEditRow_Click(object sender, RoutedEventArgs e)
    {
        if (DataGridResult.SelectedItem is not DataRowView row)
        {
            MessageBox.Show("Select a row first.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var tableName = TableName ?? TryParseTableNameFromSql(TxtQuery.Text ?? "");
        if (string.IsNullOrEmpty(tableName))
        {
            MessageBox.Show("Could not determine table name from query. Use a simple SELECT from one table (e.g. SELECT * FROM mytable).", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_getPrimaryKeyColumnsAsync == null)
        {
            MessageBox.Show("Edit is not available for this result window.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var cells = new ObservableCollection<EditableCell>();
        var table = row.Row.Table;
        foreach (DataColumn col in table.Columns)
        {
            var value = row[col.ColumnName];
            var display = value == DBNull.Value || value == null ? "" : value.ToString() ?? "";
            cells.Add(new EditableCell { ColumnName = col.ColumnName, OriginalValueRaw = value, OriginalValueDisplay = display, NewValue = display });
        }
        IReadOnlyList<string> primaryKeyColumns;
        try
        {
            primaryKeyColumns = await _getPrimaryKeyColumnsAsync(tableName);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load primary key: " + ex.Message, "DataLens", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var tableColumns = table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
        var dlg = new EditRowDialog(tableName, cells, tableColumns, primaryKeyColumns) { Owner = this };
        dlg.ShowDialog();
    }

    private void DataGridResult_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        e.Cancel = true;
    }
}
