using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;

namespace BudgetApi.Controllers;

[ApiController]
[Authorize]
[Route("api/exports")]
public class ExportsController : ControllerBase
{
    private readonly BudgetContext _db;
    public ExportsController(BudgetContext db) { _db = db; }

    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet("expenses.xlsx")]
    public async Task<IActionResult> ExportExpenses([FromQuery] int? projectId)
    {
        var q = _db.BudgetItems.AsQueryable();
        if (projectId.HasValue) q = q.Where(b => b.ProjectId == projectId);
        var items = await q.OrderByDescending(b => b.Date).ToListAsync();
        var projects = await _db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Expenses");
        ws.Cell(1, 1).Value = "Date";
        ws.Cell(1, 2).Value = "Project";
        ws.Cell(1, 3).Value = "Category";
        ws.Cell(1, 4).Value = "Description";
        ws.Cell(1, 5).Value = "Vendor";
        ws.Cell(1, 6).Value = "Amount (USD)";
        ws.Cell(1, 7).Value = "Budget (USD)";
        ws.Cell(1, 8).Value = "Status";
        ws.Row(1).Style.Font.Bold = true;

        for (var i = 0; i < items.Count; i++)
        {
            var b = items[i];
            var row = i + 2;
            ws.Cell(row, 1).Value = b.Date?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(row, 2).Value = projects.TryGetValue(b.ProjectId, out var pn) ? pn : "";
            ws.Cell(row, 3).Value = b.Category;
            ws.Cell(row, 4).Value = b.Description;
            ws.Cell(row, 5).Value = b.Vendor ?? "";
            ws.Cell(row, 6).Value = (double)b.Actual;
            ws.Cell(row, 7).Value = (double)b.Budget;
            ws.Cell(row, 8).Value = b.Status ?? "";
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), XlsxMime, $"expenses-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpGet("tasks.xlsx")]
    public async Task<IActionResult> ExportTasks([FromQuery] int? projectId)
    {
        var q = _db.CategoryActivities.AsQueryable();
        if (projectId.HasValue) q = q.Where(t => t.ProjectId == projectId);
        var items = await q.OrderBy(t => t.StartDate).ToListAsync();
        var projects = await _db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Tasks");
        ws.Cell(1, 1).Value = "Project";
        ws.Cell(1, 2).Value = "Name";
        ws.Cell(1, 3).Value = "Category";
        ws.Cell(1, 4).Value = "Start";
        ws.Cell(1, 5).Value = "End";
        ws.Cell(1, 6).Value = "Work Hours";
        ws.Row(1).Style.Font.Bold = true;

        for (var i = 0; i < items.Count; i++)
        {
            var t = items[i];
            var row = i + 2;
            ws.Cell(row, 1).Value = projects.TryGetValue(t.ProjectId, out var pn) ? pn : "";
            ws.Cell(row, 2).Value = t.Name;
            ws.Cell(row, 3).Value = t.CategoryValue;
            ws.Cell(row, 4).Value = t.StartDate.ToString("yyyy-MM-dd");
            ws.Cell(row, 5).Value = t.EndDate.ToString("yyyy-MM-dd");
            ws.Cell(row, 6).Value = t.WorkHours;
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), XlsxMime, $"tasks-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpGet("projects.xlsx")]
    public async Task<IActionResult> ExportProjects()
    {
        var items = await _db.Projects.OrderBy(p => p.Id).ToListAsync();
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Projects");
        ws.Cell(1, 1).Value = "Name";
        ws.Cell(1, 2).Value = "Status";
        ws.Cell(1, 3).Value = "Client";
        ws.Cell(1, 4).Value = "Address";
        ws.Cell(1, 5).Value = "Start";
        ws.Cell(1, 6).Value = "End";
        ws.Row(1).Style.Font.Bold = true;

        for (var i = 0; i < items.Count; i++)
        {
            var p = items[i];
            var row = i + 2;
            ws.Cell(row, 1).Value = p.Name;
            ws.Cell(row, 2).Value = p.Status;
            ws.Cell(row, 3).Value = p.Client ?? "";
            ws.Cell(row, 4).Value = p.Address ?? "";
            ws.Cell(row, 5).Value = p.StartDate?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(row, 6).Value = p.EndDate?.ToString("yyyy-MM-dd") ?? "";
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), XlsxMime, $"projects-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpGet("template/expenses.xlsx")]
    public IActionResult ExpensesTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Expenses");
        var headers = new[] { "Date", "Category", "Description", "Vendor", "Amount (USD)", "Budget (USD)", "Status" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Row(1).Style.Font.Bold = true;
        ws.Cell(2, 1).Value = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        ws.Cell(2, 2).Value = "Materials";
        ws.Cell(2, 3).Value = "Cement 50kg bags";
        ws.Cell(2, 4).Value = "Vendor name";
        ws.Cell(2, 5).Value = 1200;
        ws.Cell(2, 6).Value = 1500;
        ws.Cell(2, 7).Value = "paid";
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), XlsxMime, "expenses-template.xlsx");
    }

    [HttpGet("template/tasks.xlsx")]
    public IActionResult TasksTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Tasks");
        var headers = new[] { "Name", "Category", "Start", "End", "Work Hours" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Row(1).Style.Font.Bold = true;
        ws.Cell(2, 1).Value = "Pour foundation";
        ws.Cell(2, 2).Value = "Concrete";
        ws.Cell(2, 3).Value = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        ws.Cell(2, 4).Value = DateTime.UtcNow.Date.AddDays(3).ToString("yyyy-MM-dd");
        ws.Cell(2, 5).Value = 24;
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), XlsxMime, "tasks-template.xlsx");
    }
}
