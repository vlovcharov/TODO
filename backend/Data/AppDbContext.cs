using Microsoft.EntityFrameworkCore;
using TodoApp.Models;

namespace TodoApp.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<TodoTask> Tasks => Set<TodoTask>();
    public DbSet<TaskCompletion> TaskCompletions => Set<TaskCompletion>();
    public DbSet<AppMeta> AppMeta => Set<AppMeta>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<TodoTask>(e =>
        {
            e.Property(t => t.Level).HasConversion<string>();
            e.Property(t => t.Priority).HasConversion<string>();

            e.HasMany(t => t.Subtasks)
             .WithOne(t => t.Parent)
             .HasForeignKey(t => t.ParentId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(t => t.TaskCompletions)
             .WithOne(r => r.Task)
             .HasForeignKey(r => r.TaskId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TaskCompletion>(e =>
        {
            e.HasIndex(r => new { r.TaskId, r.Date }).IsUnique();
        });
    }
}
