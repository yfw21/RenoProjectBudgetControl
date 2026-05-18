using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Services.Llm;

namespace BudgetApi.Tools;

public class ListProjectsTool : ITool
{
    public string Name => "list_projects";
    public string Description => "List the user's construction projects. Optionally filter by status.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            status = new { type = "string", description = "Optional filter: active, paused, complete, archived" }
        },
        required = Array.Empty<string>()
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        string? status = null;
        if (args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty("status", out var s) &&
            s.ValueKind == JsonValueKind.String)
            status = s.GetString();

        var query = db.Projects.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(p => p.Status == status);

        var items = await query.OrderByDescending(p => p.Id)
            .Select(p => new { p.Id, p.Name, p.Status, p.Address, p.Client })
            .ToListAsync();

        return new { projects = items, count = items.Count };
    }
}
