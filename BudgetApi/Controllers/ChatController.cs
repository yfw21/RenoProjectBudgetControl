using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using BudgetApi.Services.Llm;

namespace BudgetApi.Controllers;

[ApiController]
[Authorize]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly ILlmClient _llm;
    private readonly ToolDispatcher _tools;
    private readonly IServiceProvider _sp;
    private readonly ILogger<ChatController> _log;
    private const int MaxToolHops = 6;

    public ChatController(ILlmClient llm, ToolDispatcher tools, IServiceProvider sp, ILogger<ChatController> log)
    {
        _llm = llm;
        _tools = tools;
        _sp = sp;
        _log = log;
    }

    public record TurnMessage(string Role, string Content);
    public record ChatRequest(string Message, List<TurnMessage>? History);

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] ChatRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest(new { error = "message required" });

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
        var username = User.Identity?.Name ?? "user";
        var ctx = new ToolContext(userId, username, _sp);

        var messages = new List<OllamaMessage>
        {
            new() { Role = "system", Content = SystemPrompt.Default }
        };
        if (req.History != null)
            foreach (var m in req.History)
                messages.Add(new OllamaMessage { Role = m.Role, Content = m.Content });
        messages.Add(new OllamaMessage { Role = "user", Content = req.Message });

        var toolDefs = _tools.AsOllamaTools();
        var actions = new List<object>();

        for (var hop = 0; hop < MaxToolHops; hop++)
        {
            OllamaChatResponse resp;
            try { resp = await _llm.ChatAsync(messages, toolDefs, ct); }
            catch (HttpRequestException e)
            {
                _log.LogWarning("LLM unreachable: {Msg}", e.Message);
                return StatusCode(503, new { error = "AI assistant is unavailable (no language model is configured or reachable)." });
            }
            catch (LlmUnavailableException e)
            {
                _log.LogWarning(e, "LLM unavailable");
                return StatusCode(503, new { error = e.Message });
            }
            messages.Add(resp.Message);

            if (resp.Message.ToolCalls is null || resp.Message.ToolCalls.Count == 0)
                return Ok(new { reply = resp.Message.Content, actions });

            foreach (var call in resp.Message.ToolCalls)
            {
                _log.LogInformation("Tool call: {Name} args={Args}", call.Function.Name, call.Function.Arguments.GetRawText());
                var result = await _tools.ExecuteAsync(call.Function.Name, call.Function.Arguments, ctx);
                var resultJson = JsonSerializer.Serialize(result);
                actions.Add(new { tool = call.Function.Name, result });
                messages.Add(new OllamaMessage { Role = "tool", Content = resultJson, ToolUseId = call.Id });
            }
        }

        return Ok(new { reply = "(stopped: tool-call limit reached)", actions });
    }
}
