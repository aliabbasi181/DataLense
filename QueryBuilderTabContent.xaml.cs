using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DataLens.Models;
using Microsoft.Win32;

namespace DataLens;

public partial class QueryBuilderTabContent : UserControl
{
    public string TableName { get; }
    public ObservableCollection<string> TableColumns { get; } = new();
    public ObservableCollection<QueryConditionRow> QueryConditions { get; } = new();
    public ObservableCollection<JoinRow> Joins { get; } = new();
    public ObservableCollection<SelectableColumn> SelectableColumns { get; } = new();
    public ObservableCollection<string> AllTableNames { get; } = new();
    /// <summary>Left table ref options for join ON: ("", main table), ("j1", first join table), ...</summary>
    public ObservableCollection<JoinLeftTableOption> LeftTableRefOptions { get; } = new();

    public Func<string, Task<(System.Data.DataTable? ResultSet, string Message)>>? ExecuteQueryAsync { get; set; }
    public Func<string, Task<IReadOnlyList<string>>>? GetTableColumnsAsync { get; set; }
    public Func<string, Task<IReadOnlyList<string>>>? GetPrimaryKeyColumnsAsync { get; set; }
    public Action<string>? OnStatus { get; set; }

    private bool _useAllColumns = true;
    public bool UseAllColumns
    {
        get => _useAllColumns;
        set { _useAllColumns = value; OnPropertyChanged(nameof(UseAllColumns)); }
    }

    private bool _useDistinct;
    public bool UseDistinct
    {
        get => _useDistinct;
        set { _useDistinct = value; OnPropertyChanged(nameof(UseDistinct)); }
    }

    public QueryBuilderTabContent(string tableName, IReadOnlyList<string> columns,
        IReadOnlyList<string>? allTableNames = null,
        Func<string, Task<IReadOnlyList<string>>>? getTableColumnsAsync = null)
    {
        InitializeComponent();
        DataContext = this;
        TableName = tableName;
        TxtTableName.Text = "Table: " + tableName;
        foreach (var c in columns)
            TableColumns.Add(c);
        if (allTableNames != null)
        {
            foreach (var t in allTableNames.Where(x => !string.Equals(x, tableName, StringComparison.OrdinalIgnoreCase)))
                AllTableNames.Add(t);
        }
        GetTableColumnsAsync = getTableColumnsAsync;

        QueryConditions.Add(new QueryConditionRow());
        ConditionsItemsControl.ItemsSource = QueryConditions;
        JoinsItemsControl.ItemsSource = Joins;
        SelectableColumnsListBox.ItemsSource = SelectableColumns;
        CboOrderByColumn.ItemsSource = OrderByColumnOptions;
        CboGroupByColumn.ItemsSource = OrderByColumnOptions;
        CboOrderByDirection.SelectedIndex = 0;

        RefreshSelectableColumns();
        RefreshLeftTableRefOptions();

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.F5 || (e.Key == Key.Return && Keyboard.Modifiers == ModifierKeys.Control))
            {
                e.Handled = true;
                BtnRunQuery_OnClick(s, e);
            }
        };
    }

    private void OnPropertyChanged(string name) { }

    /// <summary>Options for ORDER BY: qualified column names from main + joined tables.</summary>
    private ObservableCollection<string> OrderByColumnOptions { get; } = new();

    public SavedQueryBuilder GetState()
    {
        var conditions = QueryConditions
            .Where(c => !string.IsNullOrWhiteSpace(c.Column) || !string.IsNullOrWhiteSpace(c.Value))
            .Select(c => new SavedCondition { Column = c.Column ?? "", Operator = c.Operator ?? "=", Value = c.Value ?? "" })
            .ToList();
        if (conditions.Count == 0)
            conditions.Add(new SavedCondition());

        var joins = Joins
            .Where(j => !string.IsNullOrWhiteSpace(j.RelatedTableName))
            .Select(j => new SavedJoin
            {
                JoinType = j.JoinType ?? "LEFT JOIN",
                RelatedTableName = j.RelatedTableName ?? "",
                LeftTableRef = j.LeftTableRef ?? "",
                LeftColumn = j.LeftColumn ?? "",
                RightColumn = j.RightColumn ?? ""
            })
            .ToList();

        var selectedNames = UseAllColumns
            ? new List<string>()
            : SelectableColumns.Where(sc => sc.IsSelected).Select(sc => sc.DisplayLabel).ToList();

        var orderCol = CboOrderByColumn.SelectedItem as string ?? "";
        var orderDir = CboOrderByDirection.SelectedIndex == 1 ? "DESC" : "ASC";
        var groupCol = CboGroupByColumn.SelectedItem as string ?? "";
        var having = TxtHaving?.Text?.Trim() ?? "";
        var unionTail = TxtUnionTail?.Text?.Trim() ?? "";

        return new SavedQueryBuilder
        {
            TableName = TableName,
            Conditions = conditions,
            Joins = joins,
            UseAllColumns = UseAllColumns,
            SelectedColumnNames = selectedNames,
            OrderByColumn = orderCol,
            OrderByDirection = orderDir,
            GroupByColumn = groupCol,
            Having = having,
            UnionTail = unionTail,
            Limit = TxtLimit?.Text?.Trim() ?? "100"
        };
    }

    public void LoadState(SavedQueryBuilder state)
    {
        _pendingConditions = state.Conditions != null && state.Conditions.Count > 0
            ? state.Conditions.Select(c => new SavedCondition { Column = c.Column ?? "", Operator = c.Operator ?? "=", Value = c.Value ?? "" }).ToList()
            : new List<SavedCondition>();

        QueryConditions.Clear();
        if (_pendingConditions.Count > 0)
        {
            foreach (var c in _pendingConditions)
                QueryConditions.Add(new QueryConditionRow { Column = c.Column, Operator = c.Operator, Value = c.Value });
        }
        else
            QueryConditions.Add(new QueryConditionRow());

        _pendingJoins = state.Joins != null
            ? state.Joins.Select(j => new SavedJoin { JoinType = j.JoinType ?? "LEFT JOIN", RelatedTableName = j.RelatedTableName ?? "", LeftTableRef = j.LeftTableRef ?? "", LeftColumn = j.LeftColumn ?? "", RightColumn = j.RightColumn ?? "" }).ToList()
            : new List<SavedJoin>();

        Joins.Clear();
        foreach (var j in _pendingJoins)
        {
            var row = new JoinRow
            {
                JoinType = j.JoinType,
                RelatedTableName = j.RelatedTableName,
                LeftTableRef = j.LeftTableRef,
                LeftColumn = j.LeftColumn,
                RightColumn = j.RightColumn
            };
            Joins.Add(row);
        }

        UseAllColumns = state.UseAllColumns;
        ChkUseAllColumns.IsChecked = state.UseAllColumns;
        if (UseAllColumns)
            SelectableColumnsListBox.Visibility = Visibility.Collapsed;
        else
            SelectableColumnsListBox.Visibility = Visibility.Visible;

        TxtLimit.Text = state.Limit ?? "100";
        TxtHaving.Text = state.Having ?? "";
        TxtUnionTail.Text = state.UnionTail ?? "";
        CboOrderByDirection.SelectedIndex = string.Equals(state.OrderByDirection, "DESC", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _pendingSelectedColumnNames = state.SelectedColumnNames ?? new List<string>();
        _pendingOrderByColumn = state.OrderByColumn ?? "";
        _pendingGroupByColumn = state.GroupByColumn ?? "";
    }

    private List<string> _pendingSelectedColumnNames = new();
    private string _pendingOrderByColumn = "";
    private string _pendingGroupByColumn = "";
    private List<SavedCondition> _pendingConditions = new();
    private List<SavedJoin> _pendingJoins = new();

    public async Task LoadJoinColumnsAsync()
    {
        if (GetTableColumnsAsync == null) return;
        foreach (var j in Joins)
        {
            if (string.IsNullOrWhiteSpace(j.RelatedTableName)) continue;
            try
            {
                var cols = await GetTableColumnsAsync(j.RelatedTableName);
                await Dispatcher.InvokeAsync(() =>
                {
                    j.RelatedTableColumns.Clear();
                    foreach (var c in cols) j.RelatedTableColumns.Add(c);
                });
            }
            catch { /* ignore */ }
        }
        await Dispatcher.InvokeAsync(() =>
        {
            RefreshLeftTableRefOptions();
            // Re-apply LeftTableRef immediately so the "ON table" dropdown has options and value in sync
            for (var i = 0; i < Joins.Count && i < _pendingJoins.Count; i++)
            {
                var refVal = _pendingJoins[i].LeftTableRef ?? "";
                var option = LeftTableRefOptions.FirstOrDefault(o => string.Equals(o.Ref, refVal, StringComparison.Ordinal));
                Joins[i].LeftTableRef = option != null ? option.Ref : refVal;
            }
            for (var i = 0; i < Joins.Count; i++)
            {
                var row = Joins[i];
                row.LeftColumnOptions.Clear();
                if (string.IsNullOrEmpty(row.LeftTableRef) || !row.LeftTableRef.StartsWith("j", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var c in TableColumns) row.LeftColumnOptions.Add(c);
                }
                else if (row.LeftTableRef.Length > 1 && int.TryParse(row.LeftTableRef.Substring(1), System.Globalization.NumberStyles.None, null, out var idx))
                {
                    idx--;
                    if (idx >= 0 && idx < Joins.Count)
                        foreach (var c in Joins[idx].RelatedTableColumns)
                            row.LeftColumnOptions.Add(c);
                }
            }
            RefreshSelectableColumns();
            foreach (var sc in SelectableColumns)
            {
                if (_pendingSelectedColumnNames.Contains(sc.DisplayLabel, StringComparer.OrdinalIgnoreCase))
                    sc.IsSelected = true;
            }
            _pendingSelectedColumnNames.Clear();
            if (!string.IsNullOrEmpty(_pendingOrderByColumn) && OrderByColumnOptions.Contains(_pendingOrderByColumn))
                CboOrderByColumn.SelectedItem = _pendingOrderByColumn;
            _pendingOrderByColumn = "";
            if (!string.IsNullOrEmpty(_pendingGroupByColumn) && OrderByColumnOptions.Contains(_pendingGroupByColumn))
                CboGroupByColumn.SelectedItem = _pendingGroupByColumn;
            _pendingGroupByColumn = "";

            // Re-apply where conditions after TableColumns is available so column dropdowns show correctly
            if (_pendingConditions.Count > 0)
            {
                QueryConditions.Clear();
                foreach (var c in _pendingConditions)
                    QueryConditions.Add(new QueryConditionRow { Column = c.Column ?? "", Operator = c.Operator ?? "=", Value = c.Value ?? "" });
            }
            if (QueryConditions.Count == 0)
                QueryConditions.Add(new QueryConditionRow());
            _pendingConditions.Clear();

            // Re-apply join ON values after options are populated; defer so ComboBox items are rendered first
            var pendingJoinsCopy = _pendingJoins.Select(p => new SavedJoin
            {
                JoinType = p.JoinType,
                RelatedTableName = p.RelatedTableName,
                LeftTableRef = p.LeftTableRef ?? "",
                LeftColumn = p.LeftColumn ?? "",
                RightColumn = p.RightColumn ?? ""
            }).ToList();
            _pendingJoins.Clear();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                for (var i = 0; i < Joins.Count && i < pendingJoinsCopy.Count; i++)
                {
                    var p = pendingJoinsCopy[i];
                    var row = Joins[i];
                    // Use Ref from LeftTableRefOptions so "ON table" ComboBox (SelectedValue) matches
                    var refOpt = LeftTableRefOptions.FirstOrDefault(o => string.Equals(o.Ref, p.LeftTableRef ?? "", StringComparison.Ordinal));
                    row.LeftTableRef = refOpt != null ? refOpt.Ref : (p.LeftTableRef ?? "");
                    // Use actual item from collection so ComboBox SelectedItem matches (handles case)
                    var leftVal = p.LeftColumn;
                    if (!string.IsNullOrEmpty(leftVal))
                    {
                        var leftMatch = row.LeftColumnOptions.FirstOrDefault(c => string.Equals(c, leftVal, StringComparison.OrdinalIgnoreCase));
                        row.LeftColumn = leftMatch ?? leftVal;
                    }
                    else
                        row.LeftColumn = "";
                    var rightVal = p.RightColumn;
                    if (!string.IsNullOrEmpty(rightVal))
                    {
                        var rightMatch = row.RelatedTableColumns.FirstOrDefault(c => string.Equals(c, rightVal, StringComparison.OrdinalIgnoreCase));
                        row.RightColumn = rightMatch ?? rightVal;
                    }
                    else
                        row.RightColumn = "";
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        });
    }

    private void RefreshLeftTableRefOptions()
    {
        LeftTableRefOptions.Clear();
        LeftTableRefOptions.Add(new JoinLeftTableOption { Ref = "", DisplayName = TableName });
        for (var i = 0; i < Joins.Count; i++)
            LeftTableRefOptions.Add(new JoinLeftTableOption { Ref = "j" + (i + 1), DisplayName = Joins[i].RelatedTableName });
    }

    private void RefreshSelectableColumns()
    {
        SelectableColumns.Clear();
        foreach (var c in TableColumns)
            SelectableColumns.Add(new SelectableColumn { TableRef = "", TableDisplayName = TableName, ColumnName = c });
        foreach (var j in Joins)
        {
            var alias = "j" + (Joins.IndexOf(j) + 1);
            foreach (var c in j.RelatedTableColumns)
                SelectableColumns.Add(new SelectableColumn { TableRef = alias, TableDisplayName = j.RelatedTableName, ColumnName = c });
        }
        RefreshOrderByColumns();
    }

    private void RefreshOrderByColumns()
    {
        OrderByColumnOptions.Clear();
        foreach (var sc in SelectableColumns)
            OrderByColumnOptions.Add(string.IsNullOrEmpty(sc.TableRef) ? sc.ColumnName : $"{sc.TableDisplayName}.{sc.ColumnName}");
    }

    private async void JoinRelatedTable_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb || cb.DataContext is not JoinRow row || GetTableColumnsAsync == null) return;
        var table = cb.SelectedItem as string;
        row.RelatedTableColumns.Clear();
        if (string.IsNullOrEmpty(table)) return;
        try
        {
            var cols = await GetTableColumnsAsync(table);
            await Dispatcher.InvokeAsync(() =>
            {
                row.RelatedTableColumns.Clear();
                foreach (var c in cols) row.RelatedTableColumns.Add(c);
            });
        }
        catch { /* ignore */ }
        RefreshSelectableColumns();
        RefreshLeftTableRefOptions();
    }

    private void JoinLeftTableRef_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb || cb.DataContext is not JoinRow row) return;
        var opt = cb.SelectedItem as JoinLeftTableOption;
        row.LeftColumnOptions.Clear();
        if (opt == null) return;
        if (string.IsNullOrEmpty(opt.Ref))
        {
            foreach (var c in TableColumns) row.LeftColumnOptions.Add(c);
            return;
        }
        var idx = int.Parse(opt.Ref.Substring(1), System.Globalization.CultureInfo.InvariantCulture) - 1;
        if (idx >= 0 && idx < Joins.Count)
            foreach (var c in Joins[idx].RelatedTableColumns)
                row.LeftColumnOptions.Add(c);
    }

    private void BtnAddJoin_OnClick(object sender, RoutedEventArgs e)
    {
        var row = new JoinRow { LeftTableRef = "" };
        foreach (var c in TableColumns)
            row.LeftColumnOptions.Add(c);
        Joins.Add(row);
        RefreshLeftTableRefOptions();
    }

    private void RemoveJoin_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is JoinRow row)
        {
            Joins.Remove(row);
            RefreshSelectableColumns();
            RefreshLeftTableRefOptions();
        }
    }

    private void ChkUseAllColumns_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkUseAllColumns.IsChecked == true)
        {
            UseAllColumns = true;
            SelectableColumnsListBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            UseAllColumns = false;
            SelectableColumnsListBox.Visibility = Visibility.Visible;
        }
    }

    private void BtnAddCondition_OnClick(object sender, RoutedEventArgs e)
    {
        QueryConditions.Add(new QueryConditionRow());
    }

    private void RemoveCondition_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is QueryConditionRow row)
            QueryConditions.Remove(row);
    }

    private void BtnCopyQuery_OnClick(object sender, RoutedEventArgs e)
    {
        var text = TxtRunningQuery.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            MessageBox.Show("No query to copy. Run a query first.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            Clipboard.SetText(text);
            OnStatus?.Invoke("Query copied to clipboard");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not copy: " + ex.Message, "DataLens", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void BtnRunQuery_OnClick(object sender, RoutedEventArgs e)
    {
        if (ExecuteQueryAsync == null)
        {
            MessageBox.Show("Not connected.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sql = BuildSelectSql();
        if (sql == null)
        {
            MessageBox.Show("Invalid query (e.g. check limit is a number).", "DataLens", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TxtRunningQuery.Text = sql;
        BtnRunQuery.IsEnabled = false;
        TxtStatus.Text = "Running...";
        try
        {
            var (resultSet, message) = await ExecuteQueryAsync(sql);
            await Dispatcher.InvokeAsync(() =>
            {
                TxtStatus.Text = message;
                OnStatus?.Invoke(message);
                if (resultSet != null)
                {
                    var tableColumnsList = TableColumns.ToList();
                    var win = new QueryBuilderResultWindow(
                        "Query result - " + TableName, sql, resultSet,
                        TableName, tableColumnsList, GetPrimaryKeyColumnsAsync, OnStatus)
                    {
                        Owner = Window.GetWindow(this)
                    };
                    win.Show();
                }
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                TxtStatus.Text = "Error: " + ex.Message;
                MessageBox.Show(ex.Message, "Query failed", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
        finally
        {
            BtnRunQuery.IsEnabled = true;
        }
    }

    private string? BuildSelectSql()
    {
        var safeTable = SanitizeIdentifier(TableName);
        var sb = new System.Text.StringBuilder();

        // SELECT clause: all columns or selected only
        var selectedCols = new List<string>();
        var selectPrefix = UseDistinct ? "SELECT DISTINCT " : "SELECT ";
        if (!UseAllColumns && SelectableColumns.Any(x => x.IsSelected))
        {
            foreach (var sc in SelectableColumns.Where(x => x.IsSelected))
            {
                var alias = string.IsNullOrEmpty(sc.TableRef) ? (Joins.Count > 0 ? "t0" : null) : "t" + (LeftTableRefToIndex(sc.TableRef) + 1);
                if (alias != null)
                    selectedCols.Add("`" + alias + "`.`" + SanitizeIdentifier(sc.ColumnName) + "`");
                else
                    selectedCols.Add("`" + SanitizeIdentifier(sc.ColumnName) + "`");
            }
            sb.Append(selectPrefix).Append(string.Join(", ", selectedCols));
        }
        else
            sb.Append(selectPrefix).Append("*");

        // FROM and main table alias when we have joins
        var mainAlias = Joins.Count > 0 ? " t0" : "";
        sb.Append(" FROM `").Append(safeTable).Append("`").Append(mainAlias);

        // JOINs
        for (var i = 0; i < Joins.Count; i++)
        {
            var j = Joins[i];
            var relTable = SanitizeIdentifier(j.RelatedTableName);
            if (string.IsNullOrEmpty(relTable)) continue;
            var alias = " t" + (i + 1);
            var joinType = (j.JoinType ?? "LEFT JOIN").Replace(" JOIN", " JOIN", StringComparison.OrdinalIgnoreCase);
            if (!joinType.EndsWith(" JOIN", StringComparison.OrdinalIgnoreCase)) joinType = "LEFT JOIN";
            sb.Append(" ").Append(joinType).Append(" `").Append(relTable).Append("`").Append(alias);

            var leftRef = string.IsNullOrEmpty(j.LeftTableRef) ? "t0" : "t" + (LeftTableRefToIndex(j.LeftTableRef) + 1);
            var rightAlias = "t" + (i + 1);
            var leftCol = SanitizeIdentifier(j.LeftColumn);
            var rightCol = SanitizeIdentifier(j.RightColumn);
            if (!string.IsNullOrEmpty(leftCol) && !string.IsNullOrEmpty(rightCol))
                sb.Append(" ON `").Append(leftRef).Append("`.`").Append(leftCol).Append("` = `").Append(rightAlias).Append("`.`").Append(rightCol).Append("`");
        }

        // WHERE (qualify with t0 when we have joins)
        var wherePrefix = Joins.Count > 0 ? "`t0`." : "";
        var whereParts = new List<string>();
        foreach (var c in QueryConditions)
        {
            if (string.IsNullOrWhiteSpace(c.Column)) continue;
            var safeCol = SanitizeIdentifier(c.Column);
            if (string.IsNullOrEmpty(safeCol)) continue;
            var qualCol = wherePrefix + "`" + safeCol + "`";

            if (c.Operator == "IS NULL")
                whereParts.Add(qualCol + " IS NULL");
            else if (c.Operator == "IS NOT NULL")
                whereParts.Add(qualCol + " IS NOT NULL");
            else if (c.Operator == "IN")
            {
                var vals = c.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var quoted = vals.Select(v => QuoteValue(v.Trim())).ToList();
                if (quoted.Count > 0)
                    whereParts.Add(qualCol + " IN (" + string.Join(", ", quoted) + ")");
            }
            else
                whereParts.Add(qualCol + " " + c.Operator + " " + QuoteValue(c.Value));
        }
        if (whereParts.Count > 0)
            sb.Append(" WHERE ").Append(string.Join(" AND ", whereParts));

        // GROUP BY
        if (CboGroupByColumn.SelectedItem is string groupCol && !string.IsNullOrWhiteSpace(groupCol))
        {
            var qualified = QualifyOrderByColumn(groupCol);
            if (!string.IsNullOrEmpty(qualified))
                sb.Append(" GROUP BY ").Append(qualified);
        }

        // HAVING (raw expression, e.g. COUNT(*) > 1)
        var havingText = TxtHaving?.Text?.Trim() ?? "";
        if (havingText.Length > 0)
            sb.Append(" HAVING ").Append(havingText);

        // UNION / UNION ALL (user types e.g. "UNION ALL SELECT ... FROM other_table")
        var unionTail = TxtUnionTail?.Text?.Trim() ?? "";
        if (unionTail.Length > 0)
            sb.Append(" ").Append(unionTail);

        // ORDER BY (may be qualified e.g. "orders.id")
        if (CboOrderByColumn.SelectedItem is string orderCol && !string.IsNullOrWhiteSpace(orderCol))
        {
            var qualified = QualifyOrderByColumn(orderCol);
            if (!string.IsNullOrEmpty(qualified))
            {
                var dir = CboOrderByDirection.SelectedIndex == 1 ? "DESC" : "ASC";
                sb.Append(" ORDER BY ").Append(qualified).Append(" ").Append(dir);
            }
        }

        if (int.TryParse(TxtLimit.Text?.Trim(), out var limit) && limit > 0)
            sb.Append(" LIMIT ").Append(Math.Min(limit, 10000));

        return sb.ToString();
    }

    private static string SanitizeIdentifier(string value)
    {
        return string.Join("", (value ?? "").Where(c => char.IsLetterOrDigit(c) || c == '_'));
    }

    private int LeftTableRefToIndex(string refKey)
    {
        if (string.IsNullOrEmpty(refKey)) return -1;
        if (refKey.StartsWith("j", StringComparison.OrdinalIgnoreCase) && refKey.Length > 1
            && int.TryParse(refKey.Substring(1), System.Globalization.NumberStyles.None, null, out var i))
            return i - 1;
        return -1;
    }

    private string QualifyOrderByColumn(string orderCol)
    {
        var parts = orderCol.Split('.');
        if (parts.Length == 1)
        {
            var safe = SanitizeIdentifier(parts[0]);
            return Joins.Count > 0 ? "`t0`.`" + safe + "`" : "`" + safe + "`";
        }
        if (parts.Length == 2)
        {
            var tablePart = parts[0].Trim();
            var colPart = SanitizeIdentifier(parts[1]);
            if (string.IsNullOrEmpty(colPart)) return "";
            if (string.Equals(tablePart, TableName, StringComparison.OrdinalIgnoreCase))
                return Joins.Count > 0 ? "`t0`.`" + colPart + "`" : "`" + colPart + "`";
            for (var i = 0; i < Joins.Count; i++)
            {
                if (string.Equals(Joins[i].RelatedTableName, tablePart, StringComparison.OrdinalIgnoreCase))
                    return "`t" + (i + 1) + "`.`" + colPart + "`";
            }
            return "`" + SanitizeIdentifier(tablePart) + "`.`" + colPart + "`";
        }
        return "";
    }

    private static string QuoteValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return "NULL";
        if (long.TryParse(value, out _) || double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
            return value;
        return "'" + value.Replace("\\", "\\\\").Replace("'", "''") + "'";
    }

}
