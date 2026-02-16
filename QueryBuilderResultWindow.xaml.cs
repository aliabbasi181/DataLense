using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DataLens.Models;
using Microsoft.Win32;

namespace DataLens;

public partial class QueryBuilderResultWindow : Window
{
    private readonly string _tableName;
    private readonly IReadOnlyList<string> _tableColumns;
    private readonly Func<string, Task<IReadOnlyList<string>>>? _getPrimaryKeyColumnsAsync;
    private readonly Action<string>? _onStatus;

    public QueryBuilderResultWindow(string title, string queryText, DataTable? resultSet,
        string tableName, IReadOnlyList<string> tableColumns,
        Func<string, Task<IReadOnlyList<string>>>? getPrimaryKeyColumnsAsync,
        Action<string>? onStatus)
    {
        InitializeComponent();
        Title = title;
        TxtTitle.Text = title;
        TxtQuery.Text = queryText;
        _tableName = tableName;
        _tableColumns = tableColumns ?? Array.Empty<string>();
        _getPrimaryKeyColumnsAsync = getPrimaryKeyColumnsAsync;
        _onStatus = onStatus;

        if (resultSet != null)
        {
            DataGridResult.ItemsSource = resultSet.DefaultView;
            TxtTitle.Text = title + $" ({resultSet.Rows.Count} row(s))";
        }
    }

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

        var dlg = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = "csv",
            FileName = _tableName + ".csv"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var sb = new StringBuilder();
            var table = dv.Table ?? throw new InvalidOperationException("No table");
            var columns = table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
            sb.AppendLine(string.Join(",", columns.Select(EscapeCsvField)));
            foreach (var rowView in rows)
            {
                var values = columns.Select(col =>
                {
                    var v = rowView[col];
                    return EscapeCsvField(v == DBNull.Value ? "" : v?.ToString() ?? "");
                });
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

        var dlg = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = "json",
            FileName = _tableName + ".json"
        };
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
                {
                    var v = rowView[col];
                    dict[col] = v == DBNull.Value ? null : v;
                }
                list.Add(dict);
            }
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json, Encoding.UTF8);
            _onStatus?.Invoke($"Exported {rows.Count} row(s) to JSON");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Export failed: " + ex.Message, "DataLens", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnEditRow_Click(object sender, RoutedEventArgs e)
    {
        if (DataGridResult.SelectedItem is not DataRowView row)
        {
            MessageBox.Show("Select a row first.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_getPrimaryKeyColumnsAsync == null)
        {
            MessageBox.Show("Not connected.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var cells = new ObservableCollection<EditableCell>();
        var table = row.Row.Table;
        foreach (DataColumn col in table.Columns)
        {
            var value = row[col.ColumnName];
            var display = value == DBNull.Value || value == null ? "" : value.ToString() ?? "";
            cells.Add(new EditableCell
            {
                ColumnName = col.ColumnName,
                OriginalValueRaw = value,
                OriginalValueDisplay = display,
                NewValue = display
            });
        }

        IReadOnlyList<string> primaryKeyColumns;
        try
        {
            primaryKeyColumns = await _getPrimaryKeyColumnsAsync(_tableName);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load primary key: " + ex.Message, "DataLens", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new EditRowDialog(_tableName, cells, _tableColumns.ToList(), primaryKeyColumns)
        {
            Owner = this
        };
        dlg.ShowDialog();
    }

    private void DataGridResult_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        e.Cancel = true;
    }
}
