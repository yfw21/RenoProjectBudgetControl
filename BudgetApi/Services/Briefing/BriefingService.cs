using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services.Llm;

namespace BudgetApi.Services.Briefing;

public class BriefingService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<BriefingService> _log;

    public BriefingService(IServiceProvider sp, ILogger<BriefingService> log) { _sp = sp; _log = log; }

    public async Task<string> GenerateAsync(int projectId, CancellationToken ct = default)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetContext>();
        var llm = scope.ServiceProvider.GetRequiredService<ILlmClient>();

        var project = await db.Projects.FindAsync(new object?[] { projectId }, ct);
        if (project == null) return "";

        var today = DateTime.UtcNow.Date;
        var tasksToday = await db.CategoryActivities.Where(t => t.ProjectId == projectId && t.StartDate <= today && t.EndDate >= today).ToListAsync(ct);
        var upcoming = await db.CategoryActivities.Where(t => t.ProjectId == projectId && t.StartDate > today && t.StartDate <= today.AddDays(7)).OrderBy(t => t.StartDate).Take(10).ToListAsync(ct);
        var deliveriesDue = await db.PurchaseOrders.Where(p => p.DestinationProjectId == projectId && p.ActualDate == null && p.ExpectedDate <= today.AddDays(7)).OrderBy(p => p.ExpectedDate).ToListAsync(ct);
        var overdue = await db.CategoryActivities.Where(t => t.ProjectId == projectId && t.EndDate < today).Take(10).ToListAsync(ct);

        var facts = $"""
            Project: {project.Name} ({project.Status})
            Date: {today:yyyy-MM-dd}

            Active today ({tasksToday.Count}):
            {string.Join("\n", tasksToday.Select(t => $"  - {t.Name} ({t.CategoryValue}) until {t.EndDate:yyyy-MM-dd}"))}

            Overdue ({overdue.Count}):
            {string.Join("\n", overdue.Select(t => $"  - {t.Name} (was due {t.EndDate:yyyy-MM-dd})"))}

            Upcoming in next 7 days ({upcoming.Count}):
            {string.Join("\n", upcoming.Select(t => $"  - {t.Name} starts {t.StartDate:yyyy-MM-dd}"))}

            Deliveries due in next 7 days ({deliveriesDue.Count}):
            {string.Join("\n", deliveriesDue.Select(p => $"  - {p.Vendor} expected {p.ExpectedDate:yyyy-MM-dd}"))}
            """;

        var messages = new List<OllamaMessage>
        {
            new() { Role = "system", Content = "Write a terse 1-paragraph daily site briefing for a construction foreman. Lead with what matters most (overdue, blockers). End with one specific action item." },
            new() { Role = "user", Content = facts }
        };
        try
        {
            var resp = await llm.ChatAsync(messages, null, ct);
            return resp.Message.Content;
        }
        catch (Exception e)
        {
            _log.LogWarning("Briefing LLM call failed: {Msg}", e.Message);
            return facts;
        }
    }

    public async Task GenerateAndStoreForAllProjectsAsync(CancellationToken ct = default)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetContext>();
        var today = DateTime.UtcNow.Date;
        var activeProjects = await db.Projects.Where(p => p.Status == "active").Select(p => p.Id).ToListAsync(ct);
        foreach (var pid in activeProjects)
        {
            if (await db.DailyBriefings.AnyAsync(d => d.ProjectId == pid && d.Date == today, ct)) continue;
            var text = await GenerateAsync(pid, ct);
            db.DailyBriefings.Add(new DailyBriefing { ProjectId = pid, Date = today, Text = text });
            await db.SaveChangesAsync(ct);
        }
    }
}

public class DailyBriefingHostedService : BackgroundService
{
    private readonly BriefingService _briefing;
    private readonly ILogger<DailyBriefingHostedService> _log;
    private readonly TimeOnly _runAt;

    public DailyBriefingHostedService(BriefingService briefing, IConfiguration cfg, ILogger<DailyBriefingHostedService> log)
    {
        _briefing = briefing;
        _log = log;
        var runStr = cfg["Briefing:RunAt"] ?? "07:00";
        _runAt = TimeOnly.Parse(runStr);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextRun = now.Date.Add(_runAt.ToTimeSpan());
            if (nextRun <= now) nextRun = nextRun.AddDays(1);
            var delay = nextRun - now;
            _log.LogInformation("Daily briefing next run: {Next}", nextRun);
            try { await Task.Delay(delay, stoppingToken); } catch { return; }
            try { await _briefing.GenerateAndStoreForAllProjectsAsync(stoppingToken); }
            catch (Exception e) { _log.LogError(e, "Briefing run failed"); }
        }
    }
}
