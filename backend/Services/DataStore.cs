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

    // ── Tasks ─────────────────────────────────────────────────────────────────

    public async Task<List<TodoTask>> GetTasksAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Tasks
            .Where(t => t.DeletedAt == null)
            .Include(t => t.Subtasks.Where(s => s.DeletedAt == null))
            .Include(t => t.TaskCompletions)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<TodoTask?> GetTaskAsync(string id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Tasks
            .Where(t => t.DeletedAt == null)
            .Include(t => t.Subtasks.Where(s => s.DeletedAt == null))
            .Include(t => t.TaskCompletions)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<List<TodoTask>> GetSubtasksAsync(string parentId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Tasks
            .Include(t => t.Subtasks)
            .Include(t => t.TaskCompletions)
            .AsNoTracking()
            .Where(t => t.ParentId == parentId)
            .ToListAsync();
    }

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
        var now = DateTime.UtcNow;
        // Soft-delete the task and all its subtasks
        var task = await db.Tasks.Include(t => t.Subtasks).FirstOrDefaultAsync(t => t.Id == id);
        if (task == null) return;
        task.DeletedAt = now;
        foreach (var sub in task.Subtasks)
            sub.DeletedAt = now;
        await db.SaveChangesAsync();
    }

    public async Task RestoreTaskAsync(string id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var task = await db.Tasks.Include(t => t.Subtasks).FirstOrDefaultAsync(t => t.Id == id);
        if (task == null) return;
        task.DeletedAt = null;
        foreach (var sub in task.Subtasks)
            sub.DeletedAt = null;
        await db.SaveChangesAsync();
    }

    // ── Completions ───────────────────────────────────────────────────────────

    public async Task AddCompletionAsync(string taskId, DateOnly date)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var already = await db.TaskCompletions.AnyAsync(r => r.TaskId == taskId && r.Date == date);
        if (!already)
        {
            db.TaskCompletions.Add(new TaskCompletion { TaskId = taskId, Date = date, CompletedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
    }

    public async Task RemoveCompletionAsync(string taskId, DateOnly date)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var row = await db.TaskCompletions.FirstOrDefaultAsync(r => r.TaskId == taskId && r.Date == date);
        if (row != null) { db.TaskCompletions.Remove(row); await db.SaveChangesAsync(); }
    }

    public async Task<bool> IsCompletedOnDateAsync(string taskId, DateOnly date)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.TaskCompletions.AnyAsync(r => r.TaskId == taskId && r.Date == date);
    }

    // ── Epics ─────────────────────────────────────────────────────────────────

    public async Task<List<Epic>> GetEpicsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Epics.AsNoTracking().ToListAsync();
    }

    public async Task<Epic?> GetEpicAsync(string id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Epics.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task SaveEpicAsync(Epic epic)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var exists = await db.Epics.AnyAsync(e => e.Id == epic.Id);
        if (exists) db.Epics.Update(epic);
        else db.Epics.Add(epic);
        await db.SaveChangesAsync();
    }

    public async Task DeleteEpicAsync(string id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var epic = await db.Epics.FindAsync(id);
        if (epic != null) { db.Epics.Remove(epic); await db.SaveChangesAsync(); }
    }

    // ── Day Schedule ──────────────────────────────────────────────────────────

    public async Task<List<DayScheduleBlock>> GetScheduleAsync(DateOnly date)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.DayScheduleBlocks
            .Where(b => b.Date == date)
            .AsNoTracking()
            .OrderBy(b => b.StartMinutes)
            .ToListAsync();
    }

    public async Task<DayScheduleBlock?> GetBlockAsync(string id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.DayScheduleBlocks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task SaveBlockAsync(DayScheduleBlock block)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var exists = await db.DayScheduleBlocks.AnyAsync(b => b.Id == block.Id);
        if (exists) db.DayScheduleBlocks.Update(block);
        else db.DayScheduleBlocks.Add(block);
        await db.SaveChangesAsync();
    }

    public async Task DeleteBlockAsync(string id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var block = await db.DayScheduleBlocks.FindAsync(id);
        if (block != null) { db.DayScheduleBlocks.Remove(block); await db.SaveChangesAsync(); }
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
            ShowYearly  = meta.ShowYearly,
            ShowMonthly = meta.ShowMonthly,
            ShowWeekly  = meta.ShowWeekly,
            ShowDaily   = meta.ShowDaily,
        };
    }

    public async Task SaveConfigAsync(TaskLevelConfig config)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var meta = await db.AppMeta.FirstAsync(m => m.Id == 1);
        meta.ShowYearly  = config.ShowYearly;
        meta.ShowMonthly = config.ShowMonthly;
        meta.ShowWeekly  = config.ShowWeekly;
        meta.ShowDaily   = config.ShowDaily;
        await db.SaveChangesAsync();
    }

    public async Task<int> GetNextSortOrderAsync(DateOnly date)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var max = await db.Tasks
            .Where(t => t.ScheduledDate == date)
            .Select(t => (int?)t.SortOrder)
            .MaxAsync();
        return (max ?? -1) + 1;
    }
}
