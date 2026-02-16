namespace DataLens.Models;

/// <summary>
/// A workspace represents a saved MySQL connection (one database per workspace).
/// </summary>
public class Workspace
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Workspace";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3306;
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string Database { get; set; } = "";

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "(Unnamed)" : Name;
}
