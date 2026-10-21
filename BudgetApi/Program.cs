using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services.Briefing;
using BudgetApi.Services.Llm;
using BudgetApi.Services.Materials;
using BudgetApi.Services.Rag;
using BudgetApi.Services.Scheduling;
using BudgetApi.Services.Storage;
using BudgetApi.Tools;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
// Database: SQLite on a laptop, Azure SQL in the cloud. Same EF model, chosen by config.
var dbProvider = (builder.Configuration["Database:Provider"] ?? "sqlite").ToLowerInvariant();
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<BudgetContext>(o =>
{
    if (dbProvider == "sqlserver")
        // Azure SQL serverless auto-pauses; the first connection after a pause can fail transiently.
        o.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 6));
    else
        o.UseSqlite(connectionString);
});

// File storage: local disk or Azure Blob Storage.
var storageProvider = (builder.Configuration["Storage:Provider"] ?? "local").ToLowerInvariant();
if (storageProvider == "azure")
    builder.Services.AddSingleton<IFileStore, AzureBlobFileStore>();
else
    builder.Services.AddSingleton<IFileStore, LocalFileStore>();

// Auth cookies are encrypted with Data Protection keys. In a container those keys would be lost
// on every restart/redeploy (logging everyone out), so in the cloud they live in blob storage.
if (storageProvider == "azure")
{
    var dpBlob = builder.Configuration["Storage:ConnectionString"] is { Length: > 0 } dpConn
        ? new BlobContainerClient(dpConn, "dataprotection").GetBlobClient("keys.xml")
        : new BlobClient(new Uri($"{builder.Configuration["Storage:BlobEndpoint"]!.TrimEnd('/')}/dataprotection/keys.xml"),
            new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = builder.Configuration["AZURE_CLIENT_ID"] }));
    dpBlob.GetParentBlobContainerClient().CreateIfNotExists();
    builder.Services.AddDataProtection().SetApplicationName("RenoBudget").PersistKeysToAzureBlobStorage(dpBlob);
}

// Observability: traces, metrics and logs to Application Insights when it's configured.
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<BudgetContext>("database", tags: new[] { "ready" });

// Behind Azure Container Apps ingress, TLS terminates at the proxy; trust its X-Forwarded-* headers.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});
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
if (llmProvider is "anthropic" or "foundry")
{
    // One SDK client for the app's lifetime (it pools connections and caches the Entra token).
    builder.Services.AddSingleton<AnthropicLlmClient>();
    builder.Services.AddSingleton<ILlmClient>(sp => sp.GetRequiredService<AnthropicLlmClient>());
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
builder.Services.AddSingleton<BudgetApi.Services.Receipts.DocumentIntelligenceExtractor>();
builder.Services.AddScoped<BudgetApi.Services.Receipts.ReceiptExtractor>();
builder.Services.AddScoped<BudgetApi.Services.Receipts.BudgetLineSuggester>();
builder.Services.AddScoped<BudgetApi.Services.Receipts.InvoiceLineExtractor>();
builder.Services.AddHttpClient<Embedder>();
builder.Services.AddScoped<ConflictDetector>();
builder.Services.AddScoped<Scheduler>();
builder.Services.AddScoped<BurnRateService>();
builder.Services.AddSingleton<ToolDispatcher>();

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? new[] { "http://localhost:5173", "http://localhost:5174" };
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(corsOrigins)
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

    // Re-anchor FileBlob StoragePath if Files:Root has moved (local disk storage only).
    var filesRoot = builder.Configuration["Files:Root"] ?? "uploads";
    if (storageProvider == "local" && Path.IsPathRooted(filesRoot))
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

app.UseForwardedHeaders();

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
// Liveness: the process is up. Readiness: it can reach its database.
app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
app.MapFallbackToFile("index.html");

app.Run();
