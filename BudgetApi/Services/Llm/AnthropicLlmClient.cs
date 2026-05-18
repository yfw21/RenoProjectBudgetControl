using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BudgetApi.Services.Llm;

public class AnthropicLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly int _maxTokens;

    public string ModelName => _model;

    public AnthropicLlmClient(HttpClient http, IConfiguration cfg)
    {
        var baseUrl = cfg["Anthropic:BaseUrl"] ?? "https://api.anthropic.com";
        _model = cfg["Anthropic:Model"] ?? "claude-sonnet-4-6";
        _maxTokens = int.TryParse(cfg["Anthropic:MaxTokens"], out var m) ? m : 4096;

        var apiKey = cfg["Anthropic:ApiKey"]
            ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException(
                "Anthropic API key not configured. Set ANTHROPIC_API_KEY env var or Anthropic:ApiKey in appsettings.json.");

        _http = http;
        _http.BaseAddress = new Uri(baseUrl);
        _http.Timeout = TimeSpan.FromMinutes(5);
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    private class AnthropicRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("system"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? System { get; set; }
        [JsonPropertyName("tools"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Tools { get; set; }
        [JsonPropertyName("messages")] public List<object> Messages { get; set; } = new();
    }

    public async Task<OllamaChatResponse> ChatAsync(
        List<OllamaMessage> messages,
        List<OllamaTool>? tools = null,
        CancellationToken ct = default)
    {
        var systemText = string.Join("\n\n",
            messages.Where(m => m.Role == "system").Select(m => m.Content));

        var anthMessages = new List<object>();
        foreach (var m in messages.Where(x => x.Role != "system"))
        {
            if (m.Role == "user")
            {
                anthMessages.Add(new { role = "user", content = m.Content });
            }
            else if (m.Role == "assistant")
            {
                if (m.ToolCalls is null || m.ToolCalls.Count == 0)
                {
                    anthMessages.Add(new { role = "assistant", content = m.Content });
                }
                else
                {
                    var blocks = new List<object>();
                    if (!string.IsNullOrWhiteSpace(m.Content))
                        blocks.Add(new { type = "text", text = m.Content });
                    foreach (var tc in m.ToolCalls)
                    {
                        blocks.Add(new
                        {
                            type = "tool_use",
                            id = tc.Id ?? GenerateToolUseId(),
                            name = tc.Function.Name,
                            input = JsonSerializer.Deserialize<JsonElement>(tc.Function.Arguments.GetRawText())
                        });
                    }
                    anthMessages.Add(new { role = "assistant", content = blocks });
                }
            }
            else if (m.Role == "tool")
            {
                anthMessages.Add(new
                {
                    role = "user",
                    content = new[] {
                        new {
                            type = "tool_result",
                            tool_use_id = m.ToolUseId ?? "",
                            content = m.Content
                        }
                    }
                });
            }
        }

        anthMessages = MergeConsecutiveUserMessages(anthMessages);

        object? toolsObj = tools?.Select(t => (object)new
        {
            name = t.Function.Name,
            description = t.Function.Description,
            input_schema = t.Function.Parameters
        }).ToList();

        object? systemObj = null;
        if (!string.IsNullOrEmpty(systemText))
        {
            systemObj = new[] {
                new {
                    type = "text",
                    text = systemText,
                    cache_control = new { type = "ephemeral" }
                }
            };
        }

        var request = new AnthropicRequest
        {
            Model = _model,
            MaxTokens = _maxTokens,
            System = systemObj,
            Tools = toolsObj,
            Messages = anthMessages
        };

        var resp = await _http.PostAsJsonAsync("/v1/messages", request, ct);
        var respJson = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Anthropic API error {(int)resp.StatusCode}: {respJson}");

        using var doc = JsonDocument.Parse(respJson);
        var root = doc.RootElement;

        var text = new StringBuilder();
        var toolCalls = new List<OllamaToolCall>();

        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                var type = block.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type == "text" && block.TryGetProperty("text", out var tx))
                {
                    text.Append(tx.GetString());
                }
                else if (type == "tool_use")
                {
                    var id = block.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    var name = block.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                    var input = block.TryGetProperty("input", out var inEl) ? inEl : default;
                    toolCalls.Add(new OllamaToolCall
                    {
                        Id = id,
                        Function = new OllamaFunctionCall
                        {
                            Name = name ?? "",
                            Arguments = input.ValueKind == JsonValueKind.Undefined
                                ? JsonDocument.Parse("{}").RootElement.Clone()
                                : JsonDocument.Parse(input.GetRawText()).RootElement.Clone()
                        }
                    });
                }
            }
        }

        return new OllamaChatResponse
        {
            Model = _model,
            Message = new OllamaMessage
            {
                Role = "assistant",
                Content = text.ToString(),
                ToolCalls = toolCalls.Count > 0 ? toolCalls : null
            },
            Done = true
        };
    }

    private static List<object> MergeConsecutiveUserMessages(List<object> msgs)
    {
        if (msgs.Count < 2) return msgs;
        var merged = new List<object>();
        List<object>? pendingUserContent = null;

        void Flush()
        {
            if (pendingUserContent is not null)
            {
                merged.Add(new { role = "user", content = pendingUserContent });
                pendingUserContent = null;
            }
        }

        foreach (var m in msgs)
        {
            var json = JsonSerializer.SerializeToElement(m);
            var role = json.GetProperty("role").GetString();
            if (role == "user")
            {
                var c = json.GetProperty("content");
                pendingUserContent ??= new List<object>();
                if (c.ValueKind == JsonValueKind.String)
                {
                    pendingUserContent.Add(new { type = "text", text = c.GetString() });
                }
                else if (c.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in c.EnumerateArray())
                        pendingUserContent.Add(JsonSerializer.Deserialize<object>(item.GetRawText())!);
                }
            }
            else
            {
                Flush();
                merged.Add(m);
            }
        }
        Flush();
        return merged;
    }

    private static string GenerateToolUseId() =>
        "toolu_" + Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("/", "_").Replace("+", "-").TrimEnd('=').Substring(0, 22);
}
