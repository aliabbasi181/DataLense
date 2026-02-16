namespace DataLens.Models;

public class SavedFilter
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string FilterText { get; set; } = "";
}
