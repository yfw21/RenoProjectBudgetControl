using System.Text.Json;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services;
using BudgetApi.Services.Llm;

namespace BudgetApi.Tools;

public class AddExpenseTool : ITool
{
    public string Name => "add_expense";
    public string Description => "Record an expense (BudgetItem) against a project. Use when the user logs a cost, payment, or purchase.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            project = new { type = "string", description = "Project name or id (required)" },
            amount = new { type = "number", description = "Actual amount in USD (required)" },
            category = new { type = "string", description = "Category (e.g. Plumbing, Electrical, Materials). Created on demand." },
            description = new { type = "string", description = "Short description of what was bought / paid for (required)" },
            vendor = new { type = "string", description = "Vendor or supplier name" },
            date = new { type = "string", description = "ISO date (YYYY-MM-DD). Defaults to today." }
        },
        required = new[] { "project", "amount", "description" }
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        var project = await ProjectResolver.ResolveAsync(db, args);
        if (project is null) return new { error = "project not found" };

        if (!args.TryGetProperty("amount", out var amt) || amt.ValueKind != JsonValueKind.Number)
            return new { error = "amount is required" };
        var description = args.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString() : null;
        if (string.IsNullOrWhiteSpace(description)) return new { error = "description is required" };

        var category = args.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String
            ? (c.GetString() ?? "General") : "General";

        DateTime? date = null;
        if (args.TryGetProperty("date", out var dt) && dt.ValueKind == JsonValueKind.String
            && DateTime.TryParse(dt.GetString(), out var parsed))
            date = parsed;
        date ??= DateTime.UtcNow.Date;

        if (!db.Categories.Any(cat => cat.Value == category))
            db.Categories.Add(new Category { Value = category });

        var item = new BudgetItem
        {
            ProjectId = project.Id,
            Category = category,
            Description = description!,
            Actual = amt.GetDecimal(),
            Budget = 0,
            Date = date,
            Vendor = args.TryGetProperty("vendor", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null,
            Status = "paid"
        };
        db.BudgetItems.Add(item);
        await db.SaveChangesAsync();

        return new
        {
            id = item.Id,
            project = project.Name,
            amount_usd = item.Actual,
            item.Category,
            item.Description,
            item.Vendor,
            date = item.Date?.ToString("yyyy-MM-dd")
        };
    }
}
