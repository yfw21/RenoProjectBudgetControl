using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Services;
using BudgetApi.Services.Briefing;
using BudgetApi.Services.Llm;

namespace BudgetApi.Tools;

public class GetTodaysBriefingTool : ITool
{
    public string Name => "get_todays_briefing";
    public string Description => "Get today's site briefing for a project. If none exists yet, generates one on demand.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            project = new { type = "string" }
        },
        required = new[] { "project" }
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        var briefing = ctx.Services.GetRequiredService<BriefingService>();
        var project = await ProjectResolver.ResolveAsync(db, args);
        if (project is null) return new { error = "project not found" };
        var today = DateTime.UtcNow.Date;
        var existing = await db.DailyBriefings.FirstOrDefaultAsync(d => d.ProjectId == project.Id && d.Date == today);
        string text;
        if (existing != null) text = existing.Text;
        else
        {
            text = await briefing.GenerateAsync(project.Id);
            db.DailyBriefings.Add(new Models.DailyBriefing { ProjectId = project.Id, Date = today, Text = text });
            await db.SaveChangesAsync();
        }
        return new { project = project.Name, date = today.ToString("yyyy-MM-dd"), briefing = text };
    }
}
