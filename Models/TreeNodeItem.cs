using System.Collections.ObjectModel;

namespace DataLens;

/// <summary>
/// Item for the database objects tree (Tables / Stored Procedures and their children).
/// </summary>
public class TreeNodeItem
{
    public string DisplayText { get; set; } = "";
    public string Kind { get; set; } = ""; // "folder", "table", "procedure"
    public string Name { get; set; } = "";
    public ObservableCollection<TreeNodeItem> Children { get; set; } = new();
}
