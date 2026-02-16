using System.IO;
using System.Text.Json;
using DataLens.Models;

namespace DataLens.Services;

/// <summary>
/// Saves and loads workspaces to a JSON file in the user's app data folder.
/// </summary>
public class WorkspaceStorage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;

    public WorkspaceStorage()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "DataLens");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "workspaces.json");
    }

    public List<Workspace> Load()
    {
        if (!File.Exists(_filePath))
            return new List<Workspace>();

        try
        {
            var json = File.ReadAllText(_filePath);
            var list = JsonSerializer.Deserialize<List<Workspace>>(json, Options);
            return list ?? new List<Workspace>();
        }
        catch
        {
            return new List<Workspace>();
        }
    }

    public void Save(List<Workspace> workspaces)
    {
        var json = JsonSerializer.Serialize(workspaces, Options);
        File.WriteAllText(_filePath, json);
    }
}
