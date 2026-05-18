using System.Text.Json;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services;
using BudgetApi.Services.Llm;
using BudgetApi.Services.Rag;

namespace BudgetApi.Tools;

public class AddNoteTool : ITool
{
    public string Name => "add_note";
    public string Description => "Save a free-text note (optionally pinned to a project). The note will be searchable later via search_notes.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            text = new { type = "string" },
            project = new { type = "string", description = "Optional project name or id" }
        },
        required = new[] { "text" }
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        var embedder = ctx.Services.GetRequiredService<Embedder>();
        var text = args.TryGetProperty("text", out var t) ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(text)) return new { error = "text required" };
        int? projectId = null;
        if (args.TryGetProperty("project", out _))
        {
            var p = await ProjectResolver.ResolveAsync(db, args);
            if (p is null) return new { error = "project not found" };
            projectId = p.Id;
        }
        var note = new Note { ProjectId = projectId, Text = text!, Source = "user" };
        try { note.Embedding = Embedder.Serialize(await embedder.EmbedAsync(text!)); }
        catch { }
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        return new { id = note.Id, indexed = note.Embedding != null };
    }
}
