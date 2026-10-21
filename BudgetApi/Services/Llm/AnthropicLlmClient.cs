using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Foundry;
using Anthropic.Models.Messages;
using Azure.Core;
using Azure.Identity;

namespace BudgetApi.Services.Llm;

/// <summary>
/// Claude through the official Anthropic SDK. Two backends, same Messages API:
///   Llm:Provider = anthropic  -> api.anthropic.com with an API key
///   Llm:Provider = foundry    -> Claude deployed in Microsoft Foundry (Azure), signed in with
///                                Entra ID (managed identity in Azure, `az login` on a laptop): no key
/// </summary>
public class AnthropicLlmClient : ILlmClient
{
    private readonly IAnthropicClient _client;
    private readonly string _model;
    private readonly int _maxTokens;
    private readonly string _effort;
    private readonly ILogger<AnthropicLlmClient> _log;

    public string ModelName => _model;

    public AnthropicLlmClient(IConfiguration cfg, ILogger<AnthropicLlmClient> log)
        : this(CreateClient(cfg), cfg, log) { }

    public AnthropicLlmClient(IAnthropicClient client, IConfiguration cfg, ILogger<AnthropicLlmClient> log)
    {
        _client = client;
        _log = log;
        _model = cfg["Anthropic:Model"] ?? "claude-opus-5-5";
        _maxTokens = int.TryParse(cfg["Anthropic:MaxTokens"], out var m) ? m : 16000;
        _effort = cfg["Anthropic:Effort"] ?? "medium";
    }

    public static IAnthropicClient CreateClient(IConfiguration cfg, TokenCredential? credential = null)
    {
        var timeout = TimeSpan.FromMinutes(5);
        if (string.Equals(cfg["Llm:Provider"], "foundry", StringComparison.OrdinalIgnoreCase))
        {
            var resource = cfg["Foundry:Resource"]
                ?? throw new InvalidOperationException("Foundry:Resource (the Foundry account name) is not configured.");
            // In Azure AZURE_CLIENT_ID selects the app's user-assigned identity; locally this falls back to `az login`.
            credential ??= new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = cfg["AZURE_CLIENT_ID"]
            });
            IAnthropicClient foundry = new AnthropicFoundryClient(new AnthropicFoundryIdentityTokenCredentials(credential, resource))
            {
                Timeout = timeout,
                MaxRetries = 2
            };
            // Optional override, e.g. a private endpoint or an API Management gateway in front of Foundry.
            var baseUrl = cfg["Foundry:BaseUrl"];
            return string.IsNullOrEmpty(baseUrl) ? foundry : foundry.WithOptions(o => o with { BaseUrl = baseUrl });
        }

        var apiKey = cfg["Anthropic:ApiKey"]
            ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException(
                "Anthropic API key not configured. Set ANTHROPIC_API_KEY env var or Anthropic:ApiKey in appsettings.json.");
        return new AnthropicClient
        {
            ApiKey = apiKey,
            BaseUrl = cfg["Anthropic:BaseUrl"] ?? "https://api.anthropic.com",
            Timeout = timeout,
            MaxRetries = 2
        };
    }

    public async Task<OllamaChatResponse> ChatAsync(
        List<OllamaMessage> messages,
        List<OllamaTool>? tools = null,
        CancellationToken ct = default)
    {
        var systemText = string.Join("\n\n",
            messages.Where(m => m.Role == "system").Select(m => m.Content));

        var parameters = new MessageCreateParams
        {
            Model = _model,
            MaxTokens = _maxTokens,
            OutputConfig = new OutputConfig { Effort = _effort },
            Messages = ToClaudeMessages(messages.Where(x => x.Role != "system")),
        };
        if (!string.IsNullOrEmpty(systemText))
        {
            // The system prompt and tool list are identical on every turn, so they are cached.
            parameters = parameters with
            {
                System = new List<TextBlockParam>
                {
                    new() { Text = systemText, CacheControl = new CacheControlEphemeral() }
                }
            };
        }
        if (tools is { Count: > 0 })
            parameters = parameters with { Tools = tools.Select(ToClaudeTool).ToList() };

        Message response;
        try
        {
            response = await _client.Messages.Create(parameters, ct);
        }
        catch (AnthropicRateLimitException e)
        {
            throw new LlmUnavailableException("Claude is rate limited right now; try again shortly.", e);
        }
        catch (Anthropic5xxException e)
        {
            throw new LlmUnavailableException("Claude is temporarily unavailable.", e);
        }
        catch (AnthropicIOException e)
        {
            throw new LlmUnavailableException("Claude could not be reached.", e);
        }
        catch (AuthenticationFailedException e)
        {
            throw new LlmUnavailableException("Could not sign in to Claude on Microsoft Foundry (managed identity / az login).", e);
        }

        _log.LogInformation(
            "Claude {Model}: stop={Stop} in={In} out={Out} cacheRead={CacheRead}",
            _model, response.StopReason, response.Usage.InputTokens, response.Usage.OutputTokens,
            response.Usage.CacheReadInputTokens);

        return FromClaudeMessage(response);
    }

    private static List<MessageParam> ToClaudeMessages(IEnumerable<OllamaMessage> messages)
    {
        var result = new List<MessageParam>();
        foreach (var m in messages)
        {
            if (m.Role == "assistant")
            {
                // Replay Claude's own blocks unchanged (thinking blocks included) when we have them;
                // plain-text history from the browser becomes a text turn.
                if (m.ProviderContent is List<ContentBlockParam> blocks)
                    result.Add(new() { Role = Role.Assistant, Content = blocks });
                else
                    result.Add(new() { Role = Role.Assistant, Content = m.Content });
                continue;
            }

            ContentBlockParam block = m.Role == "tool"
                ? new ToolResultBlockParam { ToolUseID = m.ToolUseId ?? "", Content = m.Content }
                : new TextBlockParam { Text = m.Content };

            // Tool results for one assistant turn must arrive in a single user message.
            if (result.Count > 0 && result[^1].Role == Role.User && result[^1].Content.TryPickContentBlockParams(out var prior))
                result[^1] = result[^1] with { Content = prior.Append(block).ToList() };
            else
                result.Add(new() { Role = Role.User, Content = new List<ContentBlockParam> { block } });
        }
        return result;
    }

    private static ToolUnion ToClaudeTool(OllamaTool t)
    {
        var schema = JsonSerializer.SerializeToElement(t.Function.Parameters);
        var properties = schema.TryGetProperty("properties", out var p)
            ? p.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone())
            : new Dictionary<string, JsonElement>();
        var required = schema.TryGetProperty("required", out var r)
            ? r.EnumerateArray().Select(x => x.GetString()!).ToList()
            : new List<string>();
        return new Tool
        {
            Name = t.Function.Name,
            Description = t.Function.Description,
            InputSchema = new() { Properties = properties, Required = required },
        };
    }

    private OllamaChatResponse FromClaudeMessage(Message response)
    {
        var text = new System.Text.StringBuilder();
        var toolCalls = new List<OllamaToolCall>();
        var echo = new List<ContentBlockParam>();

        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var t))
            {
                text.Append(t.Text);
                echo.Add(new TextBlockParam { Text = t.Text });
            }
            else if (block.TryPickThinking(out var thinking))
            {
                echo.Add(new ThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
            }
            else if (block.TryPickRedactedThinking(out var redacted))
            {
                echo.Add(new RedactedThinkingBlockParam { Data = redacted.Data });
            }
            else if (block.TryPickToolUse(out var toolUse))
            {
                echo.Add(new ToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
                toolCalls.Add(new OllamaToolCall
                {
                    Id = toolUse.ID,
                    Function = new OllamaFunctionCall
                    {
                        Name = toolUse.Name,
                        Arguments = JsonSerializer.SerializeToElement(toolUse.Input)
                    }
                });
            }
        }

        if (response.StopReason == StopReason.Refusal)
        {
            text.Clear().Append("I can't help with that request.");
            toolCalls.Clear();
        }

        return new OllamaChatResponse
        {
            Model = _model,
            Message = new OllamaMessage
            {
                Role = "assistant",
                Content = text.ToString(),
                ToolCalls = toolCalls.Count > 0 ? toolCalls : null,
                ProviderContent = toolCalls.Count > 0 ? echo : null
            },
            Done = true
        };
    }
}
