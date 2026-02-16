# DataLens

A modern Windows MySQL client built with WPF and .NET 9. Connect to MySQL, browse objects, build queries visually, run raw SQL, and export or edit result data.

---

## Download

**Latest release (Windows x64):**  
[**DBLens.zip**](https://github.com/aliabbasi181/DataLense/blob/master/bin/Release/net9.0-windows/win-x64/publish/DBLens.zip)

Extract the ZIP and run `DataLens.exe`. No separate .NET installation required (self-contained).

---

## Features

- **Connection**
  - Save workspaces (host, port, user, database) and reconnect quickly.
  - Objects tab shows connected database name and port, with **Refresh** and **Reconnect** buttons.

- **Visual query builder**
  - Pick table and columns, add **JOIN**s (left table, left/right columns).
  - **WHERE** conditions with column, operator, and value.
  - **SELECT DISTINCT**, **GROUP BY**, **HAVING**, and **UNION** (e.g. `UNION ALL SELECT ...`).
  - Save, load, duplicate, and delete saved query builders.
  - Run with **F5** or **Ctrl+Enter**; results open in a new window with export and edit.

- **Query tab (raw SQL)**
  - Multi-statement support; only **SELECT** statements are allowed (no DML/DDL).
  - Results in separate windows with export and edit.

- **Results**
  - **Export to CSV** and **Export to JSON** from result grids (Query tab and query builder result windows).
  - **Edit** generates safe **UPDATE** statements using primary keys; edit in a dialog and run or copy SQL.

- **UI**
  - App starts maximized; application icon and clean layout.

---

## Requirements

- **Windows** (x64).
- For the published app: no extra install (self-contained).
- To build from source: [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

---

## Build from source

```bash
git clone https://github.com/aliabbasi181/DataLense.git
cd DataLense
dotnet restore
dotnet build
```

Run:

```bash
dotnet run
```

Publish self-contained Windows x64:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
```

Output is under `bin/Release/net9.0-windows/win-x64/publish/`.

---

## Tech stack

- **.NET 9**, **WPF**
- **MySqlConnector** for MySQL

---

## License

See repository license file.
