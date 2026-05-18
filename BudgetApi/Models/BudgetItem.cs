using System.ComponentModel.DataAnnotations;

namespace BudgetApi.Models;

public class BudgetItem
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    [Required] public string Category { get; set; } = "";
    [Required] public string Description { get; set; } = "";
    public decimal Budget { get; set; }
    public decimal Actual { get; set; }
    public DateTime? Date { get; set; }
    public string? Vendor { get; set; }
    public string? Status { get; set; }
    public int? ReceiptFileBlobId { get; set; }
    public int? BudgetLineId { get; set; }
    public int PercentComplete { get; set; }
}
