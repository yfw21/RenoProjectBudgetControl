using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Services;
using BudgetApi.Services.Llm;
using BudgetApi.Services.Rag;

namespace BudgetApi.Tools;

public class SearchNotesTool : ITool
{
    public string Name => "search_notes";
    public string Description => "Semantic search over notes and uploaded text documents. Returns the top matches.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string" },
            project = new { type = "string", description = "Optional project filter" },
            top_k = new { type = "integer", description = "How many results (default 5)" }
        },
        required = new[] { "query" }
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        var embedder = ctx.Services.GetRequiredService<Embedder>();
        var query = args.TryGetProperty("query", out var q) ? q.GetString() : null;
        if (string.IsNullOrWhiteSpace(query)) return new { error = "query required" };
        var topK = args.TryGetProperty("top_k", out var k) && k.ValueKind == JsonValueKind.Number ? k.GetInt32() : 5;
        int? projectId = null;
        if (args.TryGetProperty("project", out _))
        {
            var p = await ProjectResolver.ResolveAsync(db, args);
            if (p is null) return new { error = "project not found" };
            projectId = p.Id;
        }

        var queryVec = await embedder.EmbedAsync(query!);
        var notes = await db.Notes
            .Where(n => n.Embedding != null && (projectId == null || n.ProjectId == projectId))
            .ToListAsync();

        var scored = notes
            .Select(n => new { n, score = Embedder.Cosine(queryVec, Embedder.Deserialize(n.Embedding!)) })
            .OrderByDescending(x => x.score)
            .Take(topK)
            .Select(x => new
            {
                id = x.n.Id,
                score = Math.Round(x.score, 3),
                project_id = x.n.ProjectId,
                source = x.n.Source,
                text = x.n.Text.Length > 400 ? x.n.Text[..400] + "…" : x.n.Text
            }).ToList();
        return new { count = scored.Count, matches = scored };
    }
}
