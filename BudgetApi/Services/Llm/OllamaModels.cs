using System.Text.Json;
using System.Text.Json.Serialization;

namespace BudgetApi.Services.Llm;

public class OllamaChatRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("messages")] public List<OllamaMessage> Messages { get; set; } = new();
    [JsonPropertyName("tools")] public List<OllamaTool>? Tools { get; set; }
    [JsonPropertyName("stream")] public bool Stream { get; set; } = false;
    [JsonPropertyName("options")] public Dictionary<string, object>? Options { get; set; }
}

public class OllamaMessage
{
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
    [JsonPropertyName("tool_calls"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OllamaToolCall>? ToolCalls { get; set; }
    [JsonIgnore] public string? ToolUseId { get; set; }
}

public class OllamaTool
{
    [JsonPropertyName("type")] public string Type { get; set; } = "function";
    [JsonPropertyName("function")] public OllamaFunctionSpec Function { get; set; } = new();
}

public class OllamaFunctionSpec
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("parameters")] public object Parameters { get; set; } = new { };
}

public class OllamaToolCall
{
    [JsonIgnore] public string? Id { get; set; }
    [JsonPropertyName("function")] public OllamaFunctionCall Function { get; set; } = new();
}

public class OllamaFunctionCall
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("arguments")] public JsonElement Arguments { get; set; }
}

public class OllamaChatResponse
{
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("message")] public OllamaMessage Message { get; set; } = new();
    [JsonPropertyName("done")] public bool Done { get; set; }
}
