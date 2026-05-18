using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Services;
using BudgetApi.Services.Llm;
using BudgetApi.Services.Materials;

namespace BudgetApi.Tools;

public class GetBurnRateTool : ITool
{
    public string Name => "get_burn_rate";
    public string Description => "Estimate average daily consumption of a material on a project and when it will run out.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            project = new { type = "string" },
            material = new { type = "string" },
            lookback_days = new { type = "integer", description = "Days of history to use (default 30)" }
        },
        required = new[] { "project", "material" }
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        var svc = ctx.Services.GetRequiredService<BurnRateService>();
        var project = await ProjectResolver.ResolveAsync(db, args);
        if (project is null) return new { error = "project not found" };
        var matName = args.TryGetProperty("material", out var m) ? m.GetString() : null;
        if (string.IsNullOrWhiteSpace(matName)) return new { error = "material required" };
        var mat = await db.Materials.FirstOrDefaultAsync(x => x.Name == matName);
        if (mat is null) return new { error = "material not found" };
        var lookback = args.TryGetProperty("lookback_days", out var lb) && lb.ValueKind == JsonValueKind.Number ? lb.GetInt32() : 30;
        var burn = await svc.AvgDailyBurnAsync(project.Id, mat.Id, lookback);
        var runOut = await svc.EstimateRunOutDateAsync(project.Id, mat.Id, lookback);
        var stock = await db.StockLevels.Where(s => s.ProjectId == project.Id && s.MaterialId == mat.Id).Select(s => (decimal?)s.Quantity).FirstOrDefaultAsync() ?? 0m;
        return new
        {
            project = project.Name,
            material = mat.Name,
            unit = mat.Unit,
            current_stock = stock,
            avg_daily_burn = burn,
            run_out_date = runOut?.ToString("yyyy-MM-dd")
        };
    }
}
