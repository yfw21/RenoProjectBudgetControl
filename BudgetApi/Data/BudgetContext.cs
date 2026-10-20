using Microsoft.EntityFrameworkCore;
using BudgetApi.Models;

namespace BudgetApi.Data;

public class BudgetContext : DbContext
{
    public BudgetContext(DbContextOptions<BudgetContext> options) : base(options) { }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<User> Users => Set<User>();
    public DbSet<BudgetItem> BudgetItems => Set<BudgetItem>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<CategoryActivity> CategoryActivities => Set<CategoryActivity>();
    public DbSet<Crew> Crews => Set<Crew>();
    public DbSet<Equipment> Equipment => Set<Equipment>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<StockLevel> StockLevels => Set<StockLevel>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<VendorLeadTime> VendorLeadTimes => Set<VendorLeadTime>();
    public DbSet<FileBlob> FileBlobs => Set<FileBlob>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<DailyBriefing> DailyBriefings => Set<DailyBriefing>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    // SQLite ignores precision; SQL Server would otherwise silently truncate to (18,2) with a warning.
    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        c.Properties<decimal>().HavePrecision(18, 4);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.Username).IsUnique();
        b.Entity<Project>().HasIndex(p => p.Name).IsUnique();
        b.Entity<Category>().HasIndex(c => c.Value).IsUnique();
        b.Entity<BudgetItem>().HasIndex(x => x.ProjectId);
        b.Entity<BudgetItem>().HasIndex(x => x.BudgetLineId);
        b.Entity<CategoryActivity>().HasIndex(x => x.ProjectId);
        b.Entity<CategoryActivity>().HasIndex(x => x.BudgetLineId);
        b.Entity<Crew>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Equipment>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Assignment>().HasIndex(x => new { x.ResourceKind, x.ResourceId, x.Start });
        b.Entity<Assignment>().HasIndex(x => x.TaskId);
        b.Entity<TaskDependency>().HasIndex(x => x.TaskId);
        b.Entity<TaskDependency>().HasIndex(x => x.DependsOnTaskId);
        b.Entity<Material>().HasIndex(x => x.Name).IsUnique();
        b.Entity<StockLevel>().HasIndex(x => new { x.ProjectId, x.MaterialId }).IsUnique();
        b.Entity<PurchaseOrderItem>().HasIndex(x => x.PurchaseOrderId);
        b.Entity<VendorLeadTime>().HasIndex(x => new { x.Vendor, x.MaterialId }).IsUnique();
        b.Entity<FileBlob>().HasIndex(x => x.ProjectId);
        b.Entity<Note>().HasIndex(x => x.ProjectId);
        b.Entity<Note>().HasIndex(x => x.FileBlobId);
        b.Entity<DailyBriefing>().HasIndex(x => new { x.ProjectId, x.Date }).IsUnique();
        b.Entity<AuditLogEntry>().HasIndex(x => x.CreatedAt);
    }
}
