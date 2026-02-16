using System.Data;
using DataLens.Models;
using MySqlConnector;

namespace DataLens.Services;

/// <summary>
/// Connects to MySQL and fetches tables, stored procedures, and data.
/// </summary>
public class MySqlService : IDisposable
{
    private MySqlConnection? _connection;

    public bool IsConnected => _connection?.State == ConnectionState.Open;

    public string BuildConnectionString(Workspace ws)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = ws.Host,
            Port = (uint)ws.Port,
            UserID = ws.UserName,
            Password = ws.Password,
            Database = ws.Database
        };
        return builder.ConnectionString;
    }

    public async Task ConnectAsync(Workspace workspace, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync().ConfigureAwait(false);
        var cs = BuildConnectionString(workspace);
        _connection = new MySqlConnection(cs);
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DisconnectAsync()
    {
        if (_connection == null) return;
        await _connection.CloseAsync().ConfigureAwait(false);
        _connection.Dispose();
        _connection = null;
    }

    public async Task<IReadOnlyList<string>> GetTableNamesAsync(CancellationToken cancellationToken = default)
    {
        if (_connection == null) throw new InvalidOperationException("Not connected.");
        var list = new List<string>();
        await using var cmd = new MySqlCommand("SHOW FULL TABLES WHERE Table_type = 'BASE TABLE'", _connection);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(reader.GetString(0));
        return list;
    }

    public async Task<IReadOnlyList<string>> GetTableColumnsAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (_connection == null) throw new InvalidOperationException("Not connected.");
        var list = new List<string>();
        var sql = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tbl ORDER BY ORDINAL_POSITION";
        await using var cmd = new MySqlCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@tbl", tableName);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(reader.GetString(0));
        return list;
    }

    /// <summary>Returns primary key column names in order for the table.</summary>
    public async Task<IReadOnlyList<string>> GetPrimaryKeyColumnsAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (_connection == null) throw new InvalidOperationException("Not connected.");
        var list = new List<string>();
        var sql = """
            SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tbl AND CONSTRAINT_NAME = 'PRIMARY'
            ORDER BY ORDINAL_POSITION
            """;
        await using var cmd = new MySqlCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@tbl", tableName);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(reader.GetString(0));
        return list;
    }

    public async Task<IReadOnlyList<string>> GetStoredProcedureNamesAsync(CancellationToken cancellationToken = default)
    {
        if (_connection == null) throw new InvalidOperationException("Not connected.");
        var list = new List<string>();
        var sql = """
            SELECT ROUTINE_NAME
            FROM INFORMATION_SCHEMA.ROUTINES
            WHERE ROUTINE_SCHEMA = DATABASE()
              AND ROUTINE_TYPE = 'PROCEDURE'
            ORDER BY ROUTINE_NAME
            """;
        await using var cmd = new MySqlCommand(sql, _connection);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(reader.GetString(0));
        return list;
    }

    public async Task<DataTable> GetTableDataAsync(string tableName, int maxRows = 1000, CancellationToken cancellationToken = default)
    {
        if (_connection == null) throw new InvalidOperationException("Not connected.");
        var safeName = string.Join("", tableName.Where(c => char.IsLetterOrDigit(c) || c == '_'));
        var sql = $"SELECT * FROM `{safeName}` LIMIT {maxRows}";
        await using var cmd = new MySqlCommand(sql, _connection);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var dt = new DataTable();
        dt.Load(reader);
        return dt;
    }

    public async Task<string> GetStoredProcedureDefinitionAsync(string procedureName, CancellationToken cancellationToken = default)
    {
        if (_connection == null) throw new InvalidOperationException("Not connected.");
        var escaped = procedureName.Replace("`", "``");
        var sql = $"SHOW CREATE PROCEDURE `{escaped}`";
        await using var cmd = new MySqlCommand(sql, _connection);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return "";
        // Column is typically "Create Procedure" (index 11 in 0-based)
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (reader.GetName(i).Equals("Create Procedure", StringComparison.OrdinalIgnoreCase))
                return reader.IsDBNull(i) ? "" : reader.GetString(i);
        }
        return reader.FieldCount > 11 ? reader.GetString(11) : "";
    }

    /// <summary>
    /// Execute arbitrary SQL. Returns result set for SELECT/SHOW, or a message for other statements.
    /// </summary>
    public async Task<(DataTable? ResultSet, string Message)> ExecuteQueryAsync(string sql, CancellationToken cancellationToken = default)
    {
        if (_connection == null) throw new InvalidOperationException("Not connected.");
        if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL cannot be empty.", nameof(sql));

        await using var cmd = new MySqlCommand(sql.Trim(), _connection);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (reader.FieldCount > 0)
        {
            var dt = new DataTable();
            dt.Load(reader);
            return (dt, $"Returned {dt.Rows.Count} row(s).");
        }

        return (null, "Executed successfully.");
    }

    public void Dispose() => _connection?.Dispose();
}
