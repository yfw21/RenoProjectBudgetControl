using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services.Briefing;
using BudgetApi.Services.Llm;
using BudgetApi.Services.Materials;
using BudgetApi.Services.Rag;
using BudgetApi.Services.Scheduling;
using BudgetApi.Tools;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDbContext<BudgetContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "reno.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization();

var llmProvider = (builder.Configuration["Llm:Provider"] ?? "ollama").ToLowerInvariant();
if (llmProvider == "anthropic")
{
    builder.Services.AddHttpClient<AnthropicLlmClient>();
    builder.Services.AddScoped<ILlmClient>(sp => sp.GetRequiredService<AnthropicLlmClient>());
}
else
{
    builder.Services.AddHttpClient<OllamaClient>();
    builder.Services.AddScoped<ILlmClient>(sp => sp.GetRequiredService<OllamaClient>());
}
builder.Services.AddSingleton<ITool, ListProjectsTool>();
builder.Services.AddSingleton<ITool, AddProjectTool>();
builder.Services.AddSingleton<ITool, AddExpenseTool>();
builder.Services.AddSingleton<ITool, ListExpensesTool>();
builder.Services.AddSingleton<ITool, AddTaskTool>();
builder.Services.AddSingleton<ITool, ListTasksTool>();
builder.Services.AddSingleton<ITool, ProjectSummaryTool>();
builder.Services.AddSingleton<ITool, AddCrewTool>();
builder.Services.AddSingleton<ITool, AddEquipmentTool>();
builder.Services.AddSingleton<ITool, AssignResourceTool>();
builder.Services.AddSingleton<ITool, ListConflictsTool>();
builder.Services.AddSingleton<ITool, AddTaskDependencyTool>();
builder.Services.AddSingleton<ITool, AddMaterialTool>();
builder.Services.AddSingleton<ITool, CreatePurchaseOrderTool>();
builder.Services.AddSingleton<ITool, RecordDeliveryTool>();
builder.Services.AddSingleton<ITool, GetBurnRateTool>();
builder.Services.AddSingleton<ITool, SuggestReordersTool>();
builder.Services.AddSingleton<ITool, ListDeliveriesTool>();
builder.Services.AddSingleton<ITool, AddNoteTool>();
builder.Services.AddSingleton<ITool, SearchNotesTool>();
builder.Services.AddSingleton<ITool, RenderTableTool>();
builder.Services.AddSingleton<ITool, RenderGanttTool>();
builder.Services.AddSingleton<ITool, RenderConflictsTool>();
builder.Services.AddSingleton<ITool, RenderProjectSummaryTool>();
builder.Services.AddSingleton<ITool, GetTodaysBriefingTool>();
builder.Services.AddSingleton<BriefingService>();
builder.Services.AddHostedService<DailyBriefingHostedService>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<BudgetApi.Services.Receipts.TesseractOcr>();
builder.Services.AddScoped<BudgetApi.Services.Receipts.ReceiptExtractor>();
builder.Services.AddScoped<BudgetApi.Services.Receipts.BudgetLineSuggester>();
builder.Services.AddHttpClient<Embedder>();
builder.Services.AddScoped<ConflictDetector>();
builder.Services.AddScoped<Scheduler>();
builder.Services.AddScoped<BurnRateService>();
builder.Services.AddSingleton<ToolDispatcher>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:5173", "http://localhost:5174")
     .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BudgetContext>();
    db.Database.EnsureCreated();

    if (!db.Projects.Any())
    {
        db.Projects.Add(new Project { Name = "Default", Status = "active" });
        db.SaveChanges();
    }
    var defaultId = db.Projects.OrderBy(p => p.Id).First().Id;
    foreach (var bi in db.BudgetItems.Where(x => x.ProjectId == 0)) bi.ProjectId = defaultId;
    foreach (var a in db.CategoryActivities.Where(x => x.ProjectId == 0)) a.ProjectId = defaultId;
    db.SaveChanges();

    // Re-anchor FileBlob StoragePath if Files:Root has moved.
    var filesRoot = builder.Configuration["Files:Root"] ?? "uploads";
    if (Path.IsPathRooted(filesRoot))
    {
        Directory.CreateDirectory(filesRoot);
        foreach (var blob in db.FileBlobs.ToList())
        {
            var expected = Path.Combine(filesRoot, Path.GetFileName(blob.StoragePath));
            if (blob.StoragePath == expected) continue;
            if (!File.Exists(expected) && File.Exists(blob.StoragePath))
            {
                try { File.Move(blob.StoragePath, expected); }
                catch (Exception e) { app.Logger.LogWarning("Failed to move blob {Id}: {Msg}", blob.Id, e.Message); }
            }
            if (File.Exists(expected)) blob.StoragePath = expected;
        }
        db.SaveChanges();
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
