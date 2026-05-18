using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using BudgetApi.Data;
using BudgetApi.Models;

namespace BudgetApi.Services;

public static class ProjectResolver
{
    public static async Task<Project?> ResolveAsync(BudgetContext db, JsonElement args, string propName = "project")
    {
        if (args.ValueKind != JsonValueKind.Object) return null;
        if (!args.TryGetProperty(propName, out var v)) return null;

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var id))
            return await db.Projects.FindAsync(id);

        if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (int.TryParse(s, out var idStr)) return await db.Projects.FindAsync(idStr);
            return await db.Projects.FirstOrDefaultAsync(p =>
                EF.Functions.Like(p.Name, s));
        }
        return null;
    }
}
