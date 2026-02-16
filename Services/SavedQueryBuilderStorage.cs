using System.IO;
using System.Text.Json;
using DataLens.Models;

namespace DataLens.Services;

public class SavedQueryBuilderStorage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;

    public SavedQueryBuilderStorage()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "DataLens");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "saved_query_builders.json");
    }

    public List<SavedQueryBuilder> Load()
    {
        if (!File.Exists(_filePath))
            return new List<SavedQueryBuilder>();
        try
        {
            var json = File.ReadAllText(_filePath);
            var list = JsonSerializer.Deserialize<List<SavedQueryBuilder>>(json, Options);
            return list ?? new List<SavedQueryBuilder>();
        }
        catch
        {
            return new List<SavedQueryBuilder>();
        }
    }

    public void Save(List<SavedQueryBuilder> list)
    {
        var json = JsonSerializer.Serialize(list, Options);
        File.WriteAllText(_filePath, json);
    }
}
