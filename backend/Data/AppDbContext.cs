using Microsoft.EntityFrameworkCore;
using TodoApp.Models;

namespace TodoApp.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Epic>             Epics             => Set<Epic>();
    public DbSet<TodoTask>         Tasks             => Set<TodoTask>();
    public DbSet<TaskCompletion>   TaskCompletions   => Set<TaskCompletion>();
    public DbSet<DayScheduleBlock> DayScheduleBlocks => Set<DayScheduleBlock>();
    public DbSet<AppMeta>          AppMeta           => Set<AppMeta>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<TodoTask>(e =>
        {
            e.Property(t => t.Level).HasConversion<string>();
            e.Property(t => t.Priority).HasConversion<string>();

            e.HasOne(t => t.Parent)
             .WithMany(t => t.Subtasks)
             .HasForeignKey(t => t.ParentId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(t => t.Epic)
             .WithMany(ep => ep.Tasks)
             .HasForeignKey(t => t.EpicId)
             .OnDelete(DeleteBehavior.SetNull);

            e.HasMany(t => t.TaskCompletions)
             .WithOne(r => r.Task)
             .HasForeignKey(r => r.TaskId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TaskCompletion>(e =>
        {
            e.HasIndex(r => new { r.TaskId, r.Date }).IsUnique();
        });

        b.Entity<DayScheduleBlock>(e =>
        {
            e.HasOne(d => d.Task)
             .WithMany()
             .HasForeignKey(d => d.TaskId)
             .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(d => d.Date);
        });
    }
}
