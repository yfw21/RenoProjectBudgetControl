using System.Text.Json;
using BudgetApi.Data;
using BudgetApi.Models;

namespace BudgetApi.Services.Llm;

public class ToolDispatcher
{
    private readonly Dictionary<string, ITool> _tools;
    private static readonly string[] WritePrefixes = { "add_", "create_", "assign_", "record_", "update_", "delete_" };

    public ToolDispatcher(IEnumerable<ITool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    public List<OllamaTool> AsOllamaTools() => _tools.Values.Select(t => new OllamaTool
    {
        Function = new OllamaFunctionSpec
        {
            Name = t.Name,
            Description = t.Description,
            Parameters = t.ParametersSchema
        }
    }).ToList();

    public static bool IsWriteTool(string name) =>
        WritePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    public async Task<object> ExecuteAsync(string name, JsonElement args, ToolContext ctx)
    {
        if (!_tools.TryGetValue(name, out var tool))
            return new { error = $"Unknown tool: {name}" };

        if (IsWriteTool(name))
        {
            var confirmed = args.ValueKind == JsonValueKind.Object
                && args.TryGetProperty("confirm", out var c)
                && c.ValueKind == JsonValueKind.True;
            if (!confirmed)
            {
                var summary = BuildSummary(name, args);
                return new
                {
                    pending = true,
                    summary,
                    note = "Tool will execute when called again with confirm: true",
                    args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(args.GetRawText())
                };
            }
        }

        try
        {
            var result = await tool.ExecuteAsync(args, ctx);
            if (IsWriteTool(name))
            {
                try
                {
                    var db = ctx.Services.GetRequiredService<BudgetContext>();
                    db.AuditLog.Add(new AuditLogEntry
                    {
                        UserId = ctx.UserId,
                        Tool = name,
                        ArgsJson = args.GetRawText(),
                        ResultJson = JsonSerializer.Serialize(result)
                    });
                    await db.SaveChangesAsync();
                }
                catch { }
            }
            return result;
        }
        catch (Exception e)
        {
            return new { error = e.Message };
        }
    }

    private static string BuildSummary(string name, JsonElement args)
    {
        var verb = name.Replace('_', ' ');
        if (args.ValueKind != JsonValueKind.Object) return $"About to {verb}.";
        var parts = new List<string>();
        foreach (var prop in args.EnumerateObject())
        {
            if (prop.Name == "confirm") continue;
            var val = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.GetRawText(),
                JsonValueKind.Array => $"[{prop.Value.GetArrayLength()} items]",
                _ => prop.Value.GetRawText()
            };
            parts.Add($"{prop.Name}={val}");
        }
        return $"About to {verb} ({string.Join(", ", parts)}).";
    }
}
