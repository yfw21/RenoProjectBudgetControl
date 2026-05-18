using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BudgetApi.Controllers;

[ApiController]
[Authorize]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly IConfiguration _cfg;
    public SettingsController(IConfiguration cfg) { _cfg = cfg; }

    [HttpGet]
    public IActionResult Get()
    {
        var provider = (_cfg["Llm:Provider"] ?? "ollama").ToLowerInvariant();
        var hasAnthropicKey = !string.IsNullOrWhiteSpace(_cfg["Anthropic:ApiKey"])
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
        return Ok(new
        {
            llmProvider = provider,
            ollamaModel = _cfg["Ollama:ChatModel"] ?? "llama3.1:8b",
            anthropicModel = _cfg["Anthropic:Model"] ?? "claude-sonnet-4-6",
            anthropicConfigured = hasAnthropicKey,
            briefingRunAt = _cfg["Briefing:RunAt"] ?? "07:00",
            currency = "USD"
        });
    }
}
