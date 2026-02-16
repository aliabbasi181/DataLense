using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DataLens.Models;
using DataLens.Services;
using Microsoft.Win32;

namespace DataLens;

public partial class MainWindow : Window
{
    private readonly WorkspaceStorage _storage = new();
    private readonly SavedQueryFilterStorage _savedQueryFilterStorage = new();
    private readonly SavedQueryBuilderStorage _savedQueryBuilderStorage = new();
    private readonly MySqlService _mysql = new();
    private List<Workspace> _workspaces = new();
    private List<SavedQuery> _savedQueries = new();
    private List<SavedFilter> _savedFilters = new();
    private List<SavedQueryBuilder> _savedQueryBuilders = new();
    private Workspace? _currentWorkspace;
    private readonly ObservableCollection<TreeNodeItem> _treeRoots = new();
    private TreeNodeItem? _tablesNode;
    private TreeNodeItem? _proceduresNode;
    private bool _updatingSavedCombo;
    public ObservableCollection<string> QueryTabTableNames { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        LoadWorkspaces();
        LoadSavedQueriesAndFilters();
        LoadSavedQueryBuilders();
        BuildTreeRoots();
        TreeObjects.ItemsSource = _treeRoots;
        QueryTablesItemsControl.ItemsSource = QueryTabTableNames;
        UpdateObjectsConnectionInfo();
    }

    private void UpdateObjectsConnectionInfo()
    {
        if (_mysql.IsConnected && _currentWorkspace != null)
        {
            TxtObjectsConnectionInfo.Text = $"{_currentWorkspace.Database} @ {_currentWorkspace.Host}:{_currentWorkspace.Port}";
            BtnRefreshObjects.IsEnabled = true;
            BtnReconnect.IsEnabled = true;
        }
        else
        {
            TxtObjectsConnectionInfo.Text = "Not connected";
            BtnRefreshObjects.IsEnabled = false;
            BtnReconnect.IsEnabled = false;
        }
    }

    private void LoadSavedQueryBuilders()
    {
        _savedQueryBuilders = _savedQueryBuilderStorage.Load();
        SavedQueryBuildersListBox.ItemsSource = null;
        SavedQueryBuildersListBox.ItemsSource = _savedQueryBuilders;
    }

    private void LoadWorkspaces()
    {
        _workspaces = _storage.Load();
        CboWorkspace.ItemsSource = null;
        CboWorkspace.ItemsSource = _workspaces;
        CboWorkspace.DisplayMemberPath = "DisplayName";
        if (_workspaces.Count > 0)
            CboWorkspace.SelectedIndex = 0;
    }

    private void LoadSavedQueriesAndFilters()
    {
        (_savedQueries, _savedFilters) = _savedQueryFilterStorage.Load();
        _updatingSavedCombo = true;
        try
        {
            CboSavedQuery.ItemsSource = null;
            CboSavedQuery.ItemsSource = _savedQueries;
            CboSavedFilter.ItemsSource = null;
            CboSavedFilter.ItemsSource = _savedFilters;
        }
        finally
        {
            _updatingSavedCombo = false;
        }
    }

    private void SaveQueriesAndFilters()
    {
        _savedQueryFilterStorage.Save(_savedQueries, _savedFilters);
    }

    private void BuildTreeRoots()
    {
        _treeRoots.Clear();
        _tablesNode = new TreeNodeItem { DisplayText = "Tables", Kind = "folder", Name = "" };
        _proceduresNode = new TreeNodeItem { DisplayText = "Stored Procedures", Kind = "folder", Name = "" };
        _treeRoots.Add(_tablesNode);
        _treeRoots.Add(_proceduresNode);
    }

    private void CboWorkspace_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _currentWorkspace = CboWorkspace.SelectedItem as Workspace;
    }

    private async void BtnConnect_OnClick(object sender, RoutedEventArgs e)
    {
        var ws = CboWorkspace.SelectedItem as Workspace;
        if (ws == null)
        {
            MessageBox.Show("Please select or create a workspace first.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BtnConnect.IsEnabled = false;
        ShowStatus("Connecting...");
        try
        {
            await _mysql.ConnectAsync(ws);
            _currentWorkspace = ws;
            BtnConnect.IsEnabled = false;
            BtnDisconnect.IsEnabled = true;
            CboWorkspace.IsEnabled = false;
            UpdateObjectsConnectionInfo();
            await LoadDatabaseObjectsAsync();
            ShowStatus($"Connected to {ws.Database} on {ws.Host}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Connection failed: {ex.Message}", "DataLens", MessageBoxButton.OK, MessageBoxImage.Error);
            ShowStatus("");
            BtnConnect.IsEnabled = true;
        }
    }

    private async void BtnDisconnect_OnClick(object sender, RoutedEventArgs e)
    {
        await _mysql.DisconnectAsync();
        BtnConnect.IsEnabled = true;
        BtnDisconnect.IsEnabled = false;
        CboWorkspace.IsEnabled = true;
        _currentWorkspace = null;
        ClearTreeChildren();
        QueryTabTableNames.Clear();
        QueryBuilderTabs.Items.Clear();
        QueryBuilderTabs.Visibility = Visibility.Collapsed;
        TxtPlaceholder.Visibility = Visibility.Visible;
        UpdateObjectsConnectionInfo();
        ShowStatus("");
    }

    private async void BtnRefreshObjects_Click(object sender, RoutedEventArgs e)
    {
        if (!_mysql.IsConnected) return;
        ShowStatus("Refreshing...");
        await LoadDatabaseObjectsAsync();
        ShowStatus("Objects refreshed.");
    }

    private async void BtnReconnect_Click(object sender, RoutedEventArgs e)
    {
        var ws = _currentWorkspace;
        if (ws == null || !_mysql.IsConnected) return;
        await _mysql.DisconnectAsync();
        BtnConnect.IsEnabled = false;
        BtnDisconnect.IsEnabled = false;
        BtnReconnect.IsEnabled = false;
        BtnRefreshObjects.IsEnabled = false;
        ClearTreeChildren();
        QueryTabTableNames.Clear();
        TxtObjectsConnectionInfo.Text = "Reconnecting...";
        try
        {
            await _mysql.ConnectAsync(ws);
            _currentWorkspace = ws;
            BtnDisconnect.IsEnabled = true;
            UpdateObjectsConnectionInfo();
            await LoadDatabaseObjectsAsync();
            ShowStatus($"Reconnected to {ws.Database} on {ws.Host}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Reconnect failed: {ex.Message}", "DataLens", MessageBoxButton.OK, MessageBoxImage.Error);
            _currentWorkspace = null;
            UpdateObjectsConnectionInfo();
            BtnConnect.IsEnabled = true;
            CboWorkspace.IsEnabled = true;
        }
    }

    private async Task LoadDatabaseObjectsAsync()
    {
        ClearTreeChildren();
        try
        {
            var tables = await _mysql.GetTableNamesAsync();
            var sps = await _mysql.GetStoredProcedureNamesAsync();

            await Dispatcher.InvokeAsync(() =>
            {
                QueryTabTableNames.Clear();
                foreach (var t in tables.OrderBy(x => x))
                {
                    _tablesNode!.Children.Add(new TreeNodeItem { DisplayText = t, Kind = "table", Name = t });
                    QueryTabTableNames.Add(t);
                }
                foreach (var s in sps)
                    _proceduresNode!.Children.Add(new TreeNodeItem { DisplayText = s, Kind = "procedure", Name = s });
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to load objects: {ex.Message}", "DataLens", MessageBoxButton.OK, MessageBoxImage.Warning));
        }
    }

    private void ClearTreeChildren()
    {
        _tablesNode?.Children.Clear();
        _proceduresNode?.Children.Clear();
    }

    private async void TreeObjects_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TreeObjects.SelectedItem is not TreeNodeItem node || node.Kind != "table")
            return;
        if (!_mysql.IsConnected)
        {
            MessageBox.Show("Please connect to a workspace first.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ShowStatus("Loading columns...");
        try
        {
            var columns = await _mysql.GetTableColumnsAsync(node.Name);
            await Dispatcher.InvokeAsync(() => AddQueryBuilderTab(node.Name, columns));
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show($"Failed to load columns: {ex.Message}", "DataLens", MessageBoxButton.OK, MessageBoxImage.Warning);
                ShowStatus("");
            });
        }
    }

    private async void TreeObjects_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object?> e)
    {
        if (e.NewValue is not TreeNodeItem node || string.IsNullOrEmpty(node.Kind))
            return;
        if (node.Kind == "folder")
            return;

        if (node.Kind == "table")
        {
            // Single-click always opens a new query builder tab (same as double-click)
            ShowStatus("Loading columns...");
            try
            {
                var columns = await _mysql.GetTableColumnsAsync(node.Name);
                await Dispatcher.InvokeAsync(() => AddQueryBuilderTab(node.Name, columns));
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Failed to load columns: {ex.Message}", "DataLens", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ShowStatus("");
                });
            }
            return;
        }

        if (node.Kind == "procedure")
        {
            var key = "SP:" + node.Name;
            var existing = FindTabByKey(key);
            if (existing != null)
            {
                QueryBuilderTabs.SelectedItem = existing;
                return;
            }
            ShowStatus("Loading procedure definition...");
            try
            {
                var def = await _mysql.GetStoredProcedureDefinitionAsync(node.Name);
                await Dispatcher.InvokeAsync(() => AddProcedureTab(node.Name, def));
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Failed to load procedure: {ex.Message}", "DataLens", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ShowStatus("");
                });
            }
        }
    }

    private TabItem? FindTabByKey(string key)
    {
        foreach (TabItem tab in QueryBuilderTabs.Items)
        {
            if (tab.Tag is string k && k == key)
                return tab;
        }
        return null;
    }

    private void AddQueryBuilderTab(string tableName, IReadOnlyList<string> columns)
    {
        var allTableNames = _tablesNode?.Children.Select(c => c.Name).ToList() ?? new List<string>();
        var content = new QueryBuilderTabContent(tableName, columns, allTableNames, tbl => _mysql.GetTableColumnsAsync(tbl));
        content.ExecuteQueryAsync = sql => _mysql.ExecuteQueryAsync(sql);
        content.GetPrimaryKeyColumnsAsync = tbl => _mysql.GetPrimaryKeyColumnsAsync(tbl);
        content.OnStatus = ShowStatus;

        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        headerPanel.Children.Add(new TextBlock { Text = tableName, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var closeBtn = new Button
        {
            Content = "×",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            FontSize = 14,
            ToolTip = "Close tab"
        };
        var tabItem = new TabItem
        {
            Header = headerPanel,
            Content = content,
            Tag = "T:" + tableName
        };
        headerPanel.Children.Add(closeBtn);
        closeBtn.Click += (_, _) =>
        {
            QueryBuilderTabs.Items.Remove(tabItem);
            if (QueryBuilderTabs.Items.Count == 0)
            {
                QueryBuilderTabs.Visibility = Visibility.Collapsed;
                TxtPlaceholder.Visibility = Visibility.Visible;
            }
        };

        QueryBuilderTabs.Items.Add(tabItem);
        QueryBuilderTabs.SelectedItem = tabItem;
        QueryBuilderTabs.Visibility = Visibility.Visible;
        TxtPlaceholder.Visibility = Visibility.Collapsed;
        ShowStatus($"Table {tableName}: build your query and click Run");
    }

    private void AddProcedureTab(string procedureName, string definition)
    {
        var textBox = new System.Windows.Controls.TextBox
        {
            Text = definition,
            IsReadOnly = true,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 13,
            Padding = new Thickness(12),
            TextWrapping = TextWrapping.NoWrap,
            AcceptsReturn = true,
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent
        };
        var scroll = new ScrollViewer { Content = textBox, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };

        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        headerPanel.Children.Add(new TextBlock { Text = "SP: " + procedureName, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var closeBtn = new Button { Content = "×", Width = 22, Height = 22, Padding = new Thickness(0), FontSize = 14, ToolTip = "Close tab" };
        var tabItem = new TabItem { Header = headerPanel, Content = scroll, Tag = "SP:" + procedureName };
        headerPanel.Children.Add(closeBtn);
        closeBtn.Click += (_, _) =>
        {
            QueryBuilderTabs.Items.Remove(tabItem);
            if (QueryBuilderTabs.Items.Count == 0)
            {
                QueryBuilderTabs.Visibility = Visibility.Collapsed;
                TxtPlaceholder.Visibility = Visibility.Visible;
            }
        };

        QueryBuilderTabs.Items.Add(tabItem);
        QueryBuilderTabs.SelectedItem = tabItem;
        QueryBuilderTabs.Visibility = Visibility.Visible;
        TxtPlaceholder.Visibility = Visibility.Collapsed;
        ShowStatus($"Procedure: {procedureName}");
    }

    private void BtnSaveQueryBuilder_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_mysql.IsConnected)
        {
            MessageBox.Show("Connect first, then open a query builder tab to save.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (QueryBuilderTabs.SelectedItem is not TabItem tab || tab.Content is not QueryBuilderTabContent content)
        {
            MessageBox.Show("Select a query builder tab to save.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var state = content.GetState();
        var dlg = new InputDialog("Save query builder", "Name:", state.TableName + " builder") { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value))
            return;
        var name = dlg.Value.Trim();
        var existing = _savedQueryBuilders.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.TableName = state.TableName;
            existing.Conditions = state.Conditions;
            existing.Joins = state.Joins;
            existing.UseAllColumns = state.UseAllColumns;
            existing.SelectedColumnNames = state.SelectedColumnNames;
            existing.OrderByColumn = state.OrderByColumn;
            existing.OrderByDirection = state.OrderByDirection;
            existing.Limit = state.Limit;
            _savedQueryBuilderStorage.Save(_savedQueryBuilders);
            LoadSavedQueryBuilders();
            MessageBox.Show($"Query builder \"{name}\" updated.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        state.Name = name;
        _savedQueryBuilders.Add(state);
        _savedQueryBuilderStorage.Save(_savedQueryBuilders);
        LoadSavedQueryBuilders();
        ShowStatus($"Saved query builder \"{name}\"");
    }

    private void SavedQueryBuildersListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void BtnDuplicateSavedQueryBuilder_Click(object sender, RoutedEventArgs e)
    {
        if (SavedQueryBuildersListBox.SelectedItem is not SavedQueryBuilder saved)
        {
            MessageBox.Show("Select a saved query builder to duplicate.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var copy = new SavedQueryBuilder
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Copy of " + saved.Name,
            TableName = saved.TableName,
            UseAllColumns = saved.UseAllColumns,
            SelectedColumnNames = saved.SelectedColumnNames.ToList(),
            OrderByColumn = saved.OrderByColumn,
            OrderByDirection = saved.OrderByDirection,
            Limit = saved.Limit,
            Conditions = saved.Conditions.Select(c => new SavedCondition { Column = c.Column, Operator = c.Operator, Value = c.Value }).ToList(),
            Joins = saved.Joins.Select(j => new SavedJoin { JoinType = j.JoinType, RelatedTableName = j.RelatedTableName, LeftTableRef = j.LeftTableRef, LeftColumn = j.LeftColumn, RightColumn = j.RightColumn }).ToList()
        };
        _savedQueryBuilders.Add(copy);
        _savedQueryBuilderStorage.Save(_savedQueryBuilders);
        LoadSavedQueryBuilders();
        ShowStatus($"Duplicated as \"{copy.Name}\"");
    }

    private void BtnDeleteSavedQueryBuilder_Click(object sender, RoutedEventArgs e)
    {
        if (SavedQueryBuildersListBox.SelectedItem is not SavedQueryBuilder saved)
        {
            MessageBox.Show("Select a saved query builder to delete.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"Delete saved query builder \"{saved.Name}\"?", "DataLens", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _savedQueryBuilders.Remove(saved);
        _savedQueryBuilderStorage.Save(_savedQueryBuilders);
        LoadSavedQueryBuilders();
        ShowStatus($"Deleted \"{saved.Name}\"");
    }

    private async void SavedQueryBuildersListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SavedQueryBuildersListBox.SelectedItem is not SavedQueryBuilder saved)
            return;
        await OpenSavedQueryBuilderAsync(saved);
    }

    private async Task OpenSavedQueryBuilderAsync(SavedQueryBuilder saved)
    {
        if (!_mysql.IsConnected)
        {
            MessageBox.Show("Please connect to a workspace first.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(saved.TableName))
        {
            MessageBox.Show("Saved query builder has no table name.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ShowStatus("Loading...");
        try
        {
            var columns = await _mysql.GetTableColumnsAsync(saved.TableName);
            var allTableNames = _tablesNode?.Children.Select(c => c.Name).ToList() ?? new List<string>();
            QueryBuilderTabContent? content = null;
            await Dispatcher.InvokeAsync(() =>
            {
                content = new QueryBuilderTabContent(saved.TableName, columns, allTableNames, tbl => _mysql.GetTableColumnsAsync(tbl));
                content.ExecuteQueryAsync = sql => _mysql.ExecuteQueryAsync(sql);
                content.GetPrimaryKeyColumnsAsync = tbl => _mysql.GetPrimaryKeyColumnsAsync(tbl);
                content.OnStatus = ShowStatus;
                content.LoadState(saved);

                var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
                headerPanel.Children.Add(new TextBlock { Text = saved.Name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                var closeBtn = new Button { Content = "×", Width = 22, Height = 22, Padding = new Thickness(0), FontSize = 14, ToolTip = "Close tab" };
                var tabItem = new TabItem { Header = headerPanel, Content = content, Tag = "QB:" + saved.Id };
                headerPanel.Children.Add(closeBtn);
                closeBtn.Click += (_, _) =>
                {
                    QueryBuilderTabs.Items.Remove(tabItem);
                    if (QueryBuilderTabs.Items.Count == 0)
                    {
                        QueryBuilderTabs.Visibility = Visibility.Collapsed;
                        TxtPlaceholder.Visibility = Visibility.Visible;
                    }
                };
                QueryBuilderTabs.Items.Add(tabItem);
                QueryBuilderTabs.SelectedItem = tabItem;
                QueryBuilderTabs.Visibility = Visibility.Visible;
                TxtPlaceholder.Visibility = Visibility.Collapsed;
            });
            if (content != null)
                await content.LoadJoinColumnsAsync();
            await Dispatcher.InvokeAsync(() => ShowStatus($"Loaded query builder \"{saved.Name}\""));
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load query builder: " + ex.Message, "DataLens", MessageBoxButton.OK, MessageBoxImage.Warning);
            ShowStatus("");
        }
    }

    private void DataGridQuery_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        e.Cancel = true; // Double-click is for selection/copy only; do not commit edits
    }

    private IEnumerable<DataRowView> GetQueryGridRowsToExport()
    {
        if (DataGridQuery.ItemsSource is not DataView dv)
            yield break;
        if (DataGridQuery.SelectedItems.Count > 0)
        {
            foreach (var item in DataGridQuery.SelectedItems)
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

    private void BtnExportQueryCsv_Click(object sender, RoutedEventArgs e)
    {
        if (DataGridQuery.ItemsSource is not DataView dv)
        {
            MessageBox.Show("Run a query first to export data.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var rows = GetQueryGridRowsToExport().ToList();
        if (rows.Count == 0)
        {
            MessageBox.Show("No rows to export.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*", DefaultExt = "csv", FileName = "query_result.csv" };
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
            ShowStatus($"Exported {rows.Count} row(s) to CSV");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Export failed: " + ex.Message, "DataLens", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnExportQueryJson_Click(object sender, RoutedEventArgs e)
    {
        if (DataGridQuery.ItemsSource is not DataView dv)
        {
            MessageBox.Show("Run a query first to export data.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var rows = GetQueryGridRowsToExport().ToList();
        if (rows.Count == 0)
        {
            MessageBox.Show("No rows to export.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new SaveFileDialog { Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*", DefaultExt = "json", FileName = "query_result.json" };
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
            ShowStatus($"Exported {rows.Count} row(s) to JSON");
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

    private async void BtnEditQueryRow_Click(object sender, RoutedEventArgs e)
    {
        if (DataGridQuery.SelectedItem is not DataRowView row)
        {
            MessageBox.Show("Select a row first.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var tableName = TryParseTableNameFromSql(TxtLastRunQuery.Text ?? "");
        if (string.IsNullOrEmpty(tableName))
        {
            MessageBox.Show("Could not determine table name from query. Use a simple SELECT from one table (e.g. SELECT * FROM mytable), or use the Query builder for Edit.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
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
            primaryKeyColumns = await _mysql.GetPrimaryKeyColumnsAsync(tableName);
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

    private void BtnCopyQueryTab_OnClick(object sender, RoutedEventArgs e)
    {
        CopyQueryToClipboard(TxtLastRunQuery.Text, "No query to copy. Run a query first.");
    }

    private void CopyQueryToClipboard(string? text, string emptyMessage)
    {
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t))
        {
            MessageBox.Show(emptyMessage, "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            Clipboard.SetText(t);
            ShowStatus("Query copied to clipboard");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not copy to clipboard: " + ex.Message, "DataLens", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowStatus(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            StatusBorder.Visibility = Visibility.Collapsed;
            return;
        }
        TxtStatus.Text = message;
        StatusBorder.Visibility = Visibility.Visible;
    }

    private void BtnNewWorkspace_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new WorkspaceEditWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result == null) return;
        _workspaces.Add(dlg.Result);
        _storage.Save(_workspaces);
        LoadWorkspaces();
        CboWorkspace.SelectedItem = dlg.Result;
    }

    private void BtnEditWorkspace_OnClick(object sender, RoutedEventArgs e)
    {
        var ws = CboWorkspace.SelectedItem as Workspace;
        if (ws == null)
        {
            MessageBox.Show("Please select a workspace to edit.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new WorkspaceEditWindow(ws) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result == null) return;
        var i = _workspaces.IndexOf(ws);
        if (i >= 0)
        {
            _workspaces[i] = dlg.Result;
            _storage.Save(_workspaces);
            LoadWorkspaces();
            CboWorkspace.SelectedItem = _workspaces[i];
        }
    }

    private void CboSavedQuery_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSavedCombo) return;
    }

    private void BtnLoadQuery_OnClick(object sender, RoutedEventArgs e)
    {
        if (CboSavedQuery.SelectedItem is not SavedQuery q) return;
        TxtQuery.Text = q.Sql;
        TxtQuery.Focus();
    }

    private void BtnSaveQuery_OnClick(object sender, RoutedEventArgs e)
    {
        var sql = TxtQuery.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(sql))
        {
            MessageBox.Show("Enter a query in the editor first.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (CboSavedQuery.SelectedItem is SavedQuery existing)
        {
            existing.Sql = sql;
            SaveQueriesAndFilters();
            MessageBox.Show($"Query \"{existing.Name}\" updated.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new InputDialog("Save query", "Query name:", "My query") { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value))
            return;
        var name = dlg.Value.Trim();
        if (_savedQueries.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("A query with this name already exists.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _savedQueries.Add(new SavedQuery { Name = name, Sql = sql });
        SaveQueriesAndFilters();
        LoadSavedQueriesAndFilters();
        CboSavedQuery.SelectedItem = _savedQueries.First(x => x.Name == name);
        MessageBox.Show("Query saved.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnDeleteQuery_OnClick(object sender, RoutedEventArgs e)
    {
        if (CboSavedQuery.SelectedItem is not SavedQuery q)
        {
            MessageBox.Show("Select a saved query to delete.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"Delete query \"{q.Name}\"?", "DataLens", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _savedQueries.Remove(q);
        SaveQueriesAndFilters();
        LoadSavedQueriesAndFilters();
    }

    private void BtnInsertFilter_OnClick(object sender, RoutedEventArgs e)
    {
        if (CboSavedFilter.SelectedItem is not SavedFilter f) return;
        InsertIntoQueryAtCursor(f.FilterText.Trim());
    }

    private void QueryTable_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tableName) return;
        InsertIntoQueryAtCursor(tableName);
    }

    private void InsertIntoQueryAtCursor(string insert)
    {
        var text = TxtQuery.Text ?? "";
        var start = TxtQuery.SelectionStart;
        var before = text[..start];
        var after = text[start..];
        TxtQuery.Text = before + insert + after;
        TxtQuery.SelectionStart = start + insert.Length;
        TxtQuery.SelectionLength = 0;
        TxtQuery.Focus();
    }

    private void BtnSaveFilter_OnClick(object sender, RoutedEventArgs e)
    {
        var text = TxtQuery.SelectedText?.Trim() ?? TxtQuery.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text))
        {
            MessageBox.Show("Select some text in the editor to save as a filter, or enter text first.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (CboSavedFilter.SelectedItem is SavedFilter existing)
        {
            existing.FilterText = text;
            SaveQueriesAndFilters();
            MessageBox.Show($"Filter \"{existing.Name}\" updated.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new InputDialog("Save filter", "Filter name:", "My filter") { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value))
            return;
        var name = dlg.Value.Trim();
        if (_savedFilters.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("A filter with this name already exists.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _savedFilters.Add(new SavedFilter { Name = name, FilterText = text });
        SaveQueriesAndFilters();
        LoadSavedQueriesAndFilters();
        CboSavedFilter.SelectedItem = _savedFilters.First(x => x.Name == name);
        MessageBox.Show("Filter saved.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnDeleteFilter_OnClick(object sender, RoutedEventArgs e)
    {
        if (CboSavedFilter.SelectedItem is not SavedFilter f)
        {
            MessageBox.Show("Select a saved filter to delete.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"Delete filter \"{f.Name}\"?", "DataLens", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _savedFilters.Remove(f);
        SaveQueriesAndFilters();
        LoadSavedQueriesAndFilters();
    }

    private void TxtQuery_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            RunQueryAsync();
        }
    }

    private void BtnRunQuery_OnClick(object sender, RoutedEventArgs e)
    {
        RunQueryAsync();
    }

    /// <summary>Split SQL into statements by semicolon. Trims and skips empty. Newlines allowed inside a statement.</summary>
    private static IReadOnlyList<string> SplitStatements(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return Array.Empty<string>();
        var list = new List<string>();
        foreach (var part in sql.Split(';'))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
                list.Add(trimmed);
        }
        if (list.Count == 0 && sql.Trim().Length > 0)
            list.Add(sql.Trim());
        return list;
    }

    /// <summary>Returns true if the statement is a SELECT (after trimming and skipping leading comments).</summary>
    private static bool IsSelectStatement(string statement)
    {
        var s = statement.TrimStart();
        while (s.Length > 0)
        {
            if (s.StartsWith("--", StringComparison.Ordinal))
            {
                var nl = s.IndexOf('\n');
                s = nl < 0 ? "" : s[(nl + 1)..].TrimStart();
                continue;
            }
            if (s.StartsWith("/*", StringComparison.Ordinal))
            {
                var end = s.IndexOf("*/", 2, StringComparison.Ordinal);
                s = end < 0 ? "" : s[(end + 2)..].TrimStart();
                continue;
            }
            break;
        }
        return s.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);
    }

    private async void RunQueryAsync()
    {
        if (!_mysql.IsConnected)
        {
            MessageBox.Show("Please connect to a workspace first.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Run selected text if any, otherwise run entire editor content
        var sql = TxtQuery.SelectionLength > 0
            ? (TxtQuery.SelectedText?.Trim() ?? "")
            : (TxtQuery.Text?.Trim() ?? "");
        if (string.IsNullOrEmpty(sql))
        {
            MessageBox.Show("Select some query text to run, or enter a SQL query.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var statements = SplitStatements(sql);
        if (statements.Count == 0)
        {
            MessageBox.Show("No valid statements (split by semicolon or newline).", "DataLens", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var nonSelect = statements.Where(st => !IsSelectStatement(st)).ToList();
        if (nonSelect.Count > 0)
        {
            MessageBox.Show(
                "Only SELECT queries are allowed in the Query panel. The query contains statement(s) that are not SELECT (e.g. INSERT, UPDATE, DELETE, DROP).",
                "DataLens",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        TxtLastRunQuery.Text = sql;
        BtnRunQuery.IsEnabled = false;
        TxtQueryPlaceholder.Visibility = Visibility.Collapsed;
        DataGridQuery.Visibility = Visibility.Collapsed;
        TxtQueryMessage.Visibility = Visibility.Collapsed;

        try
        {
            if (statements.Count == 1)
            {
                TxtQueryStatus.Text = "Running...";
                var (resultSet, message) = await _mysql.ExecuteQueryAsync(statements[0]);
                await Dispatcher.InvokeAsync(() =>
                {
                    TxtQueryStatus.Text = message;
                    TxtQueryResultTitle.Text = "Results";
                    if (resultSet != null)
                    {
                        TxtQueryResultTitle.Text = $"Results ({resultSet.Rows.Count} row(s))";
                        DataGridQuery.ItemsSource = resultSet.DefaultView;
                        DataGridQuery.Visibility = Visibility.Visible;
                        TxtQueryMessage.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        TxtQueryMessage.Text = message;
                        TxtQueryMessage.Visibility = Visibility.Visible;
                        DataGridQuery.Visibility = Visibility.Collapsed;
                    }
                });
            }
            else
            {
                TxtQueryStatus.Text = $"Running {statements.Count} query/queries...";
                for (var i = 0; i < statements.Count; i++)
                {
                    var index = i + 1;
                    await Dispatcher.InvokeAsync(() => { TxtQueryStatus.Text = $"Running query {index} of {statements.Count}..."; });
                    DataTable? resultSet = null;
                    string message;
                    var sqlForWindow = statements[i];
                    try
                    {
                        var (rs, msg) = await _mysql.ExecuteQueryAsync(sqlForWindow);
                        resultSet = rs;
                        message = msg;
                    }
                    catch (Exception ex)
                    {
                        message = "Error: " + ex.Message;
                    }
                    var title = $"Query {index} result";
                    var resultSetCopy = resultSet;
                    var msgCopy = message;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        var tableName = TryParseTableNameFromSql(sqlForWindow);
                        var win = new QueryResultWindow(
                            title,
                            sqlForWindow,
                            resultSetCopy,
                            msgCopy,
                            tableName,
                            t => _mysql.GetPrimaryKeyColumnsAsync(t),
                            ShowStatus)
                        {
                            Owner = this,
                            WindowStartupLocation = WindowStartupLocation.Manual,
                            Left = this.Left + 40 * index,
                            Top = this.Top + 40 * index
                        };
                        win.Show();
                    });
                }
                await Dispatcher.InvokeAsync(() =>
                {
                    TxtQueryStatus.Text = $"Completed {statements.Count} query/queries. See result windows.";
                    TxtQueryResultTitle.Text = "Results";
                    TxtQueryMessage.Text = $"Ran {statements.Count} query/queries. Each result is in a separate window.";
                    TxtQueryMessage.Visibility = Visibility.Visible;
                });
            }
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                TxtQueryStatus.Text = "Error";
                TxtQueryResultTitle.Text = "Results";
                TxtQueryMessage.Text = ex.Message;
                TxtQueryMessage.Visibility = Visibility.Visible;
                DataGridQuery.Visibility = Visibility.Collapsed;
                MessageBox.Show(ex.Message, "Query failed", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
        finally
        {
            BtnRunQuery.IsEnabled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _mysql.Dispose();
        base.OnClosed(e);
    }
}
