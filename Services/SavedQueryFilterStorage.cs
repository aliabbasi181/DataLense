using System.IO;
using System.Text.Json;
using DataLens.Models;

namespace DataLens.Services;

/// <summary>
/// Saves and loads saved queries and filters to a JSON file in app data.
/// </summary>
public class SavedQueryFilterStorage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;

    public SavedQueryFilterStorage()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "DataLens");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "saved_queries_filters.json");
    }

    public (List<SavedQuery> Queries, List<SavedFilter> Filters) Load()
    {
        if (!File.Exists(_filePath))
            return (new List<SavedQuery>(), new List<SavedFilter>());

        try
        {
            var json = File.ReadAllText(_filePath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var queries = new List<SavedQuery>();
            var filters = new List<SavedFilter>();
            if (root.TryGetProperty("queries", out var qArr))
                queries = JsonSerializer.Deserialize<List<SavedQuery>>(qArr.GetRawText(), Options) ?? new List<SavedQuery>();
            if (root.TryGetProperty("filters", out var fArr))
                filters = JsonSerializer.Deserialize<List<SavedFilter>>(fArr.GetRawText(), Options) ?? new List<SavedFilter>();
            return (queries, filters);
        }
        catch
        {
            return (new List<SavedQuery>(), new List<SavedFilter>());
        }
    }

    public void Save(List<SavedQuery> queries, List<SavedFilter> filters)
    {
        var obj = new { queries, filters };
        var json = JsonSerializer.Serialize(obj, Options);
        File.WriteAllText(_filePath, json);
    }
}
