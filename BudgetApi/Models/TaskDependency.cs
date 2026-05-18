namespace BudgetApi.Models;

public class TaskDependency
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public int DependsOnTaskId { get; set; }
}
