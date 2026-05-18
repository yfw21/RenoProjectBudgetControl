using System.ComponentModel.DataAnnotations;

namespace BudgetApi.Models;

public class PurchaseOrder
{
    public int Id { get; set; }
    [Required] public string Vendor { get; set; } = "";
    public int DestinationProjectId { get; set; }
    public DateTime ExpectedDate { get; set; }
    public DateTime? ActualDate { get; set; }
    public string Status { get; set; } = "ordered";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
}
