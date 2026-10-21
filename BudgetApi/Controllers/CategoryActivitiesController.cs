using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Models;

namespace BudgetApi.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class CategoryActivitiesController : ControllerBase
    {
        private readonly BudgetContext _context;
        public CategoryActivitiesController(BudgetContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<CategoryActivity>>> GetActivities([FromQuery] int? projectId)
        {
            var q = _context.CategoryActivities.AsQueryable();
            if (projectId.HasValue) q = q.Where(a => a.ProjectId == projectId.Value);
            return await q.ToListAsync();
        }

        [HttpGet("byCategory/{categoryValue}")]
        public async Task<ActionResult<IEnumerable<CategoryActivity>>> GetActivitiesByCategory(string categoryValue)
        {
            return await _context.CategoryActivities.Where(a => a.CategoryValue == categoryValue).ToListAsync();
        }

        [HttpPost]
        public async Task<ActionResult<CategoryActivity>> PostActivity(CategoryActivity activity)
        {
            _context.CategoryActivities.Add(activity);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetActivities), new { id = activity.Id }, activity);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutActivity(int id, CategoryActivity activity)
        {
            if (id != activity.Id) return BadRequest();
            _context.Entry(activity).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteActivity(int id)
        {
            var activity = await _context.CategoryActivities.FindAsync(id);
            if (activity == null) return NotFound();
            _context.CategoryActivities.Remove(activity);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
