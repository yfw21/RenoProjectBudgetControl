using System.Text.Json;

namespace BudgetApi.Services.Llm;

public interface ITool
{
    string Name { get; }
    string Description { get; }
    object ParametersSchema { get; }
    Task<object> ExecuteAsync(JsonElement args, ToolContext ctx);
}

public record ToolContext(int UserId, string Username, IServiceProvider Services);
