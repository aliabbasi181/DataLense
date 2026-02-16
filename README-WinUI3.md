# Using WinUI 3 for DataLens

You asked to use **WinUI 3** for the UI. The project is currently **WPF** so it builds and runs from the command line and in any environment.

## Why the app is still WPF

- **WinUI 3** (Windows App SDK) expects build tools that register **Windows-specific RIDs** (e.g. `win10-x64`). Those are installed with **Visual Studio 2022** and the **“Windows application development”** (or “WinUI”) workload.
- Building with only the **.NET SDK** (`dotnet build`) fails with errors like:  
  `The specified RuntimeIdentifier 'win10-x64' is not recognized`.

So a full WinUI 3 version only builds reliably **inside Visual Studio 2022** with the right workload.

## If you want WinUI 3

1. Install **Visual Studio 2022** with:
   - Workload: **“Windows application development”** or **“.NET desktop development”** (with Windows App SDK / WinUI options), or  
   - Component: **Windows App SDK C# Templates**.
2. Create a new **“Blank App, Packaged (WinUI in Desktop)”** or **“Blank App (Unpackaged)”** C# project.
3. Migrate the app logic into that project:
   - Keep **Models** and **Services** as they are (they are plain .NET and work in both WPF and WinUI 3).
   - Replace UI with WinUI 3:
     - `Window` → `Microsoft.UI.Xaml.Window`
     - `TabControl` → `TabView` / `TabViewItem`
     - `TreeView` → WinUI 3 `TreeView` with `TreeViewNode` and `RootNodes`
     - `DataGrid` → WinUI 3 control (e.g. **CommunityToolkit.WinUI.UI.Controls.DataGrid**) or `ListView` + columns
     - `MessageBox` → `ContentDialog`
     - Dialogs: use **ContentDialog** (e.g. for workspace edit and name input) and set `XamlRoot` before `ShowAsync()`.
   - Entry point: WinUI 3 uses `Program.Main()` → `Application.Start(_ => new App());` and `App.OnLaunched` to create the main window.

Functionality (workspaces, MySQL, queries, saved queries/filters) can stay the same; only the UI layer and project file need to target WinUI 3.

## Current project (WPF)

- **Framework:** .NET 9, WPF.
- **Build/run:**  
  `dotnet build` then `dotnet run` (or open the solution in Visual Studio and F5).
- All features (workspaces, MySQL connection, tables/SPs, query tab, saved queries and filters) work with the current WPF UI.

If you want to proceed with a full WinUI 3 version, the next step is to do it inside a new WinUI 3 project in Visual Studio 2022 and move the existing logic and UI into that structure.
