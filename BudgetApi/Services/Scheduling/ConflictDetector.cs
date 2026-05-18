using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Models;

namespace BudgetApi.Services.Scheduling;

public record Conflict(ResourceKind Kind, int ResourceId, string ResourceName, int TaskAId, string TaskAName, int TaskBId, string TaskBName, DateTime OverlapStart, DateTime OverlapEnd);

public class ConflictDetector
{
    private readonly BudgetContext _db;
    public ConflictDetector(BudgetContext db) { _db = db; }

    public async Task<List<Conflict>> DetectAsync(DateTime? from = null, DateTime? to = null)
    {
        var q = _db.Assignments.AsQueryable();
        if (from.HasValue) q = q.Where(a => a.End >= from);
        if (to.HasValue) q = q.Where(a => a.Start <= to);
        var assignments = await q.OrderBy(a => a.ResourceKind).ThenBy(a => a.ResourceId).ThenBy(a => a.Start).ToListAsync();
        var tasks = await _db.CategoryActivities.ToDictionaryAsync(t => t.Id);
        var crews = await _db.Crews.ToDictionaryAsync(c => c.Id, c => c.Name);
        var equip = await _db.Equipment.ToDictionaryAsync(e => e.Id, e => e.Name);

        var conflicts = new List<Conflict>();
        var grouped = assignments.GroupBy(a => (a.ResourceKind, a.ResourceId));
        foreach (var g in grouped)
        {
            var ordered = g.OrderBy(a => a.Start).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                for (var j = i + 1; j < ordered.Count; j++)
                {
                    var a = ordered[i]; var b = ordered[j];
                    if (b.Start > a.End) break;
                    var name = g.Key.ResourceKind == ResourceKind.Crew
                        ? (crews.TryGetValue(g.Key.ResourceId, out var cn) ? cn : $"crew#{g.Key.ResourceId}")
                        : (equip.TryGetValue(g.Key.ResourceId, out var en) ? en : $"equipment#{g.Key.ResourceId}");
                    var tA = tasks.TryGetValue(a.TaskId, out var ta) ? ta.Name : $"task#{a.TaskId}";
                    var tB = tasks.TryGetValue(b.TaskId, out var tb) ? tb.Name : $"task#{b.TaskId}";
                    conflicts.Add(new Conflict(g.Key.ResourceKind, g.Key.ResourceId, name, a.TaskId, tA, b.TaskId, tB,
                        b.Start, a.End < b.End ? a.End : b.End));
                }
            }
        }
        return conflicts;
    }
}
