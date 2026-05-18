using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services.Llm;

namespace BudgetApi.Tools;

public class AddProjectTool : ITool
{
    public string Name => "add_project";
    public string Description => "Create a new construction project (site). Use when the user wants to add or create a new project.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            name = new { type = "string", description = "Project name (required)" },
            address = new { type = "string", description = "Site address" },
            client = new { type = "string", description = "Client / owner name" }
        },
        required = new[] { "name" }
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var name = args.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString() : null;
        if (string.IsNullOrWhiteSpace(name)) return new { error = "name is required" };

        var db = ctx.Services.GetRequiredService<BudgetContext>();
        if (await db.Projects.AnyAsync(p => p.Name == name))
            return new { error = $"A project named '{name}' already exists" };

        var p = new Project
        {
            Name = name,
            Address = args.TryGetProperty("address", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null,
            Client = args.TryGetProperty("client", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null,
            Status = "active"
        };
        db.Projects.Add(p);
        await db.SaveChangesAsync();
        return new { id = p.Id, name = p.Name, status = p.Status };
    }
}
