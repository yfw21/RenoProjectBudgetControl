using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BudgetApi.Services.Scheduling;

namespace BudgetApi.Controllers;

[ApiController]
[Authorize]
[Route("api/conflicts")]
public class ConflictsController : ControllerBase
{
    private readonly ConflictDetector _detector;
    public ConflictsController(ConflictDetector detector) { _detector = detector; }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var conflicts = await _detector.DetectAsync(from, to);
        return Ok(conflicts.Select(c => new
        {
            kind = c.Kind.ToString().ToLower(),
            resource = c.ResourceName,
            taskA = c.TaskAName,
            taskB = c.TaskBName,
            overlapStart = c.OverlapStart,
            overlapEnd = c.OverlapEnd
        }));
    }
}
