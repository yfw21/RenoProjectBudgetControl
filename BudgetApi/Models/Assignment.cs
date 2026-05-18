namespace BudgetApi.Models;

public enum ResourceKind { Crew = 1, Equipment = 2 }

public class Assignment
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public ResourceKind ResourceKind { get; set; }
    public int ResourceId { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
}
