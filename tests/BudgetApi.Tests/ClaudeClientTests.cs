using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using BudgetApi.Services.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BudgetApi.Tests;

// Exercises the real Anthropic SDK against a stub HTTP handler: what goes over the wire, and how replies map back.
public class ClaudeClientTests
{
    private const string ToolUseReply = """
        {"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5",
         "content":[{"type":"thinking","thinking":"","signature":"sig-abc"},
                    {"type":"text","text":"Let me check."},
                    {"type":"tool_use","id":"toolu_1","name":"list_projects","input":{"status":"active"}}],
         "stop_reason":"tool_use","stop_sequence":null,
         "usage":{"input_tokens":900,"output_tokens":40,"cache_read_input_tokens":0,"cache_creation_input_tokens":850}}
        """;

    private const string FinalReply = """
        {"id":"msg_2","type":"message","role":"assistant","model":"claude-opus-5-5",
         "content":[{"type":"text","text":"You have 2 active projects."}],
         "stop_reason":"end_turn","stop_sequence":null,
         "usage":{"input_tokens":950,"output_tokens":12,"cache_read_input_tokens":850,"cache_creation_input_tokens":0}}
        """;

    private static IConfiguration Config(params (string, string)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Item1, v => (string?)v.Item2)).Build();

    private static (AnthropicLlmClient, StubHandler) Create(IConfiguration cfg, TokenCredential? credential = null, params string[] replies)
    {
        var stub = new StubHandler(replies);
        var sdk = AnthropicLlmClient.CreateClient(cfg, credential)
            .WithOptions(o => o with { HttpClient = new HttpClient(stub), MaxRetries = 0 });
        return (new AnthropicLlmClient(sdk, cfg, NullLogger<AnthropicLlmClient>.Instance), stub);
    }

    private static readonly List<OllamaTool> Tools =
    [
        new() { Function = new() { Name = "list_projects", Description = "List projects",
            Parameters = new { type = "object", properties = new { status = new { type = "string" } }, required = new[] { "status" } } } }
    ];

    [Fact]
    public async Task Foundry_uses_entra_bearer_token_and_no_api_key()
    {
        var cfg = Config(("Llm:Provider", "foundry"), ("Foundry:Resource", "ai-renobudget"));
        var (client, stub) = Create(cfg, new FakeCredential(), FinalReply);

        await client.ChatAsync([new() { Role = "user", Content = "hi" }]);

        var req = stub.Requests.Single();
        Assert.Equal("https://ai-renobudget.services.ai.azure.com/anthropic/v1/messages", req.Url);
        Assert.Equal("bearer fake-entra-token", req.Authorization, ignoreCase: true);
        Assert.False(req.Headers.ContainsKey("x-api-key"));
        Assert.Equal("https://ai.azure.com/.default", FakeCredential.LastScope);
    }

    [Fact]
    public async Task Direct_api_uses_api_key()
    {
        var cfg = Config(("Llm:Provider", "anthropic"), ("Anthropic:ApiKey", "sk-test"));
        var (client, stub) = Create(cfg, null, FinalReply);

        var resp = await client.ChatAsync([new() { Role = "user", Content = "hi" }]);

        Assert.Equal("https://api.anthropic.com/v1/messages", stub.Requests.Single().Url);
        Assert.Equal("sk-test", stub.Requests.Single().Headers["x-api-key"]);
        Assert.Equal("You have 2 active projects.", resp.Message.Content);
        Assert.Null(resp.Message.ToolCalls);
    }

    [Fact]
    public async Task Tool_loop_replays_thinking_and_batches_tool_results()
    {
        var cfg = Config(("Llm:Provider", "anthropic"), ("Anthropic:ApiKey", "sk-test"));
        var (client, stub) = Create(cfg, null, ToolUseReply, FinalReply);
        var messages = new List<OllamaMessage>
        {
            new() { Role = "system", Content = "You are a renovation assistant." },
            new() { Role = "user", Content = "How many active projects?" }
        };

        // Hop 1: Claude asks for a tool.
        var first = await client.ChatAsync(messages, Tools);
        var call = Assert.Single(first.Message.ToolCalls!);
        Assert.Equal("list_projects", call.Function.Name);
        Assert.Equal("active", call.Function.Arguments.GetProperty("status").GetString());

        // Hop 2: same append-only loop as ChatController.
        messages.Add(first.Message);
        messages.Add(new() { Role = "tool", Content = "[{\"id\":1},{\"id\":2}]", ToolUseId = call.Id });
        var second = await client.ChatAsync(messages, Tools);
        Assert.Equal("You have 2 active projects.", second.Message.Content);

        var body1 = stub.Requests[0].Body;
        Assert.Equal("claude-opus-5-5", body1.GetProperty("model").GetString());
        Assert.Equal("medium", body1.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.Equal("ephemeral", body1.GetProperty("system")[0].GetProperty("cache_control").GetProperty("type").GetString());
        Assert.Equal("status", body1.GetProperty("tools")[0].GetProperty("input_schema").GetProperty("required")[0].GetString());

        var msgs = stub.Requests[1].Body.GetProperty("messages");
        Assert.Equal(3, msgs.GetArrayLength());
        var assistant = msgs[1].GetProperty("content");
        Assert.Equal("thinking", assistant[0].GetProperty("type").GetString());
        Assert.Equal("sig-abc", assistant[0].GetProperty("signature").GetString());
        Assert.Equal("tool_use", assistant[2].GetProperty("type").GetString());
        Assert.Equal("toolu_1", assistant[2].GetProperty("id").GetString());
        var result = msgs[2].GetProperty("content")[0];
        Assert.Equal("tool_result", result.GetProperty("type").GetString());
        Assert.Equal("toolu_1", result.GetProperty("tool_use_id").GetString());
    }

    [Fact]
    public async Task Refusal_returns_a_polite_reply_without_tool_calls()
    {
        var refusal = FinalReply.Replace("\"end_turn\"", "\"refusal\"");
        var (client, _) = Create(Config(("Anthropic:ApiKey", "sk-test")), null, refusal);

        var resp = await client.ChatAsync([new() { Role = "user", Content = "x" }]);

        Assert.Equal("I can't help with that request.", resp.Message.Content);
        Assert.Null(resp.Message.ToolCalls);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Rate_limit_and_outage_become_LlmUnavailable(HttpStatusCode status)
    {
        var (client, stub) = Create(Config(("Anthropic:ApiKey", "sk-test")), null);
        stub.FailWith = status;

        await Assert.ThrowsAsync<LlmUnavailableException>(() =>
            client.ChatAsync([new() { Role = "user", Content = "x" }]));
    }

    private sealed class FakeCredential : TokenCredential
    {
        public static string? LastScope;
        public override AccessToken GetToken(TokenRequestContext ctx, CancellationToken ct)
        {
            LastScope = ctx.Scopes.Single();
            return new AccessToken("fake-entra-token", DateTimeOffset.UtcNow.AddHours(1));
        }
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext ctx, CancellationToken ct) =>
            new(GetToken(ctx, ct));
    }

    private sealed record Captured(string Url, string? Authorization, Dictionary<string, string> Headers, JsonElement Body);

    private sealed class StubHandler(string[] replies) : HttpMessageHandler
    {
        private int _next;
        public HttpStatusCode? FailWith;
        public List<Captured> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new Captured(
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase),
                JsonDocument.Parse(body).RootElement.Clone()));

            if (FailWith is { } status)
                return new HttpResponseMessage(status)
                {
                    Content = new StringContent("{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"busy\"}}", Encoding.UTF8, "application/json")
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(replies[_next++], Encoding.UTF8, "application/json")
            };
        }
    }
}
