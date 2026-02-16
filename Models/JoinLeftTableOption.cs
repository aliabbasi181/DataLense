namespace DataLens.Models;

/// <summary>Option for "left table" in a join ON clause: "" = main table, "j1" = first join, etc.</summary>
public class JoinLeftTableOption
{
    public string Ref { get; set; } = "";
    public string DisplayName { get; set; } = "";
}
