namespace DataLens.Models;

public class SavedQueryBuilder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string TableName { get; set; } = "";
    public List<SavedCondition> Conditions { get; set; } = new();
    public List<SavedJoin> Joins { get; set; } = new();
    public bool UseAllColumns { get; set; } = true;
    public List<string> SelectedColumnNames { get; set; } = new();
    public string OrderByColumn { get; set; } = "";
    public string OrderByDirection { get; set; } = "ASC";
    public string GroupByColumn { get; set; } = "";
    public string Having { get; set; } = "";
    public string UnionTail { get; set; } = "";
    public string Limit { get; set; } = "100";
}

public class SavedCondition
{
    public string Column { get; set; } = "";
    public string Operator { get; set; } = "=";
    public string Value { get; set; } = "";
}

public class SavedJoin
{
    public string JoinType { get; set; } = "LEFT JOIN";
    public string RelatedTableName { get; set; } = "";
    public string LeftTableRef { get; set; } = "";
    public string LeftColumn { get; set; } = "";
    public string RightColumn { get; set; } = "";
}
