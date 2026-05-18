using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Models;

namespace BudgetApi.Services.Scheduling;

public record ScheduledTask(int Id, string Name, DateTime EarliestStart, DateTime EarliestEnd);

public class Scheduler
{
    private readonly BudgetContext _db;
    public Scheduler(BudgetContext db) { _db = db; }

    public async Task<List<ScheduledTask>> ForwardPassAsync(int projectId)
    {
        var tasks = await _db.CategoryActivities.Where(t => t.ProjectId == projectId).ToListAsync();
        if (tasks.Count == 0) return new();
        var deps = await _db.TaskDependencies.Where(d => tasks.Select(t => t.Id).Contains(d.TaskId)).ToListAsync();
        var byId = tasks.ToDictionary(t => t.Id);
        var incoming = tasks.ToDictionary(t => t.Id, _ => new List<int>());
        var outgoing = tasks.ToDictionary(t => t.Id, _ => new List<int>());
        foreach (var d in deps)
        {
            if (incoming.ContainsKey(d.TaskId)) incoming[d.TaskId].Add(d.DependsOnTaskId);
            if (outgoing.ContainsKey(d.DependsOnTaskId)) outgoing[d.DependsOnTaskId].Add(d.TaskId);
        }
        var order = new List<int>();
        var degree = incoming.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
        var queue = new Queue<int>(degree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        while (queue.TryDequeue(out var id))
        {
            order.Add(id);
            foreach (var nxt in outgoing[id])
                if (--degree[nxt] == 0) queue.Enqueue(nxt);
        }
        var earliest = new Dictionary<int, DateTime>();
        foreach (var id in order)
        {
            var t = byId[id];
            var start = t.StartDate;
            foreach (var p in incoming[id])
                if (earliest.TryGetValue(p, out var pEnd) && pEnd > start)
                    start = pEnd;
            var duration = (t.EndDate - t.StartDate).TotalDays;
            var end = start.AddDays(duration);
            earliest[id] = end;
        }
        return order.Select(id =>
        {
            var t = byId[id];
            var end = earliest[id];
            var duration = (t.EndDate - t.StartDate).TotalDays;
            var start = end.AddDays(-duration);
            return new ScheduledTask(id, t.Name, start, end);
        }).ToList();
    }
}
