namespace BudgetApi.Models;

public class VendorLeadTime
{
    public int Id { get; set; }
    public string Vendor { get; set; } = "";
    public int MaterialId { get; set; }
    public int AvgLeadDays { get; set; }
}
