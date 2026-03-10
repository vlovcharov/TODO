using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Models;

namespace TodoApp.Services;

public class DataStore
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public DataStore(IDbContextFactory<AppDbContext> factory)
    {
        _factory = factory;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<List<TodoTask>> GetTasksAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Tasks
            .Include(t => t.Subtasks)
            .Include(t => t.RecurringCompletions)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<TodoTask?> GetTaskAsync(string id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Tasks
            .Include(t => t.Subtasks)
            .Include(t => t.RecurringCompletions)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public async Task SaveTaskAsync(TodoTask task)
    {
        await using var db = await _factory.CreateDbContextAsync();
        task.UpdatedAt = DateTime.UtcNow;
        var exists = await db.Tasks.AnyAsync(t => t.Id == task.Id);
        if (exists) db.Tasks.Update(task);
        else db.Tasks.Add(task);
        await db.SaveChangesAsync();
    }

    public async Task SaveTasksAsync(IEnumerable<TodoTask> tasks)
    {
        await using var db = await _factory.CreateDbContextAsync();
        foreach (var task in tasks)
        {
            task.UpdatedAt = DateTime.UtcNow;
            var exists = await db.Tasks.AnyAsync(t => t.Id == task.Id);
            if (exists) db.Tasks.Update(task);
            else db.Tasks.Add(task);
        }
        await db.SaveChangesAsync();
    }

    public async Task DeleteTaskAsync(string id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var task = await db.Tasks.FindAsync(id);
        if (task != null)
        {
            db.Tasks.Remove(task);
            await db.SaveChangesAsync();
        }
    }

    // ── Recurring completions ─────────────────────────────────────────────────

    public async Task AddRecurringCompletionAsync(string taskId, DateOnly date)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var dateStr = date.ToString("yyyy-MM-dd");
        var already = await db.RecurringCompletions
            .AnyAsync(r => r.TaskId == taskId && r.DateStr == dateStr);
        if (!already)
        {
            db.RecurringCompletions.Add(new RecurringCompletion
            {
                TaskId = taskId,
                DateStr = dateStr
            });
            await db.SaveChangesAsync();
        }
    }

    public async Task RemoveRecurringCompletionAsync(string taskId, DateOnly date)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var dateStr = date.ToString("yyyy-MM-dd");
        var row = await db.RecurringCompletions
            .FirstOrDefaultAsync(r => r.TaskId == taskId && r.DateStr == dateStr);
        if (row != null)
        {
            db.RecurringCompletions.Remove(row);
            await db.SaveChangesAsync();
        }
    }

    // ── App metadata ──────────────────────────────────────────────────────────

    public async Task<AppMeta> GetMetaAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.AppMeta.AsNoTracking().FirstAsync(m => m.Id == 1);
    }

    public async Task UpdateLastRolloverAsync(DateTime time)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var meta = await db.AppMeta.FirstAsync(m => m.Id == 1);
        meta.LastRolloverCheck = time;
        await db.SaveChangesAsync();
    }

    public async Task<TaskLevelConfig> GetConfigAsync()
    {
        var meta = await GetMetaAsync();
        return new TaskLevelConfig
        {
            ShowLifeGoal = meta.ShowLifeGoal,
            ShowYearly   = meta.ShowYearly,
            ShowMonthly  = meta.ShowMonthly,
            ShowWeekly   = meta.ShowWeekly,
            ShowDaily    = meta.ShowDaily,
        };
    }

    public async Task SaveConfigAsync(TaskLevelConfig config)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var meta = await db.AppMeta.FirstAsync(m => m.Id == 1);
        meta.ShowLifeGoal = config.ShowLifeGoal;
        meta.ShowYearly   = config.ShowYearly;
        meta.ShowMonthly  = config.ShowMonthly;
        meta.ShowWeekly   = config.ShowWeekly;
        meta.ShowDaily    = config.ShowDaily;
        await db.SaveChangesAsync();
    }

    public async Task<List<TodoTask>> GetSubtasksAsync(string parentId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Tasks
            .Include(t => t.Subtasks)
            .Include(t => t.RecurringCompletions)
            .AsNoTracking()
            .Where(t => t.ParentId == parentId)
            .ToListAsync();
    }

    // ── Sort order helper ─────────────────────────────────────────────────────

    public async Task<int> GetNextSortOrderAsync(DateOnly date)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var dateStr = date.ToString("yyyy-MM-dd");
        var max = await db.Tasks
            .Where(t => t.ScheduledDateStr == dateStr)
            .Select(t => (int?)t.SortOrder)
            .MaxAsync();
        return (max ?? -1) + 1;
    }
}
