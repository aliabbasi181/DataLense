namespace DataLens.Models;

public class SavedQuery
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Sql { get; set; } = "";
}
