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

    /// <summary>Tasks visible on a specific day: scheduled on that date, recurring active, or sticky.</summary>
    public async Task<List<TodoTask>> GetTasksForDayAsync(DateOnly date)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var all = await db.Tasks
            .Where(t => t.DeletedAt == null)
            .Include(t => t.Subtasks.Where(s => s.DeletedAt == null))
            .Include(t => t.TaskCompletions)
            .AsNoTracking()
            .ToListAsync();

        // Return top-level tasks visible on this day + their subtasks
        var parentIds = all
            .Where(t => t.ParentId == null && IsVisibleOnDay(t, date))
            .Select(t => t.Id)
            .ToHashSet();

        return all
            .Where(t => t.ParentId == null ? IsVisibleOnDay(t, date) : parentIds.Contains(t.ParentId))
            .OrderBy(t => t.SortOrder)
            .ToList();
    }

    /// <summary>Tasks visible in a date range: scheduled in range, recurring active on any day, or sticky.</summary>
    public async Task<List<TodoTask>> GetTasksForRangeAsync(DateOnly from, DateOnly to)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var all = await db.Tasks
            .Where(t => t.DeletedAt == null)
            .Include(t => t.Subtasks.Where(s => s.DeletedAt == null))
            .Include(t => t.TaskCompletions)
            .AsNoTracking()
            .ToListAsync();

        return all.Where(t =>
        {
            if (t.ParentId != null) return false;
            if (t.RecurrenceMask != 0)
            {
                for (var d = from; d <= to; d = d.AddDays(1))
                    if (RecurrenceDays.IsActiveOn(t.RecurrenceMask, d)) return true;
                return false;
            }
            if (t.ScheduledDate >= from && t.ScheduledDate <= to) return true;
            return IsStickyInRange(t, from, to);
        }).OrderBy(t => t.SortOrder).ToList();
    }

    /// <summary>Epics with embedded tasks: active (not missed), recurring active today, sticky in period, or scheduled today/future. No completed tasks excluded.</summary>
    public async Task<List<Epic>> GetEpicsWithTasksAsync(DateOnly today)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var epics = await db.Epics.AsNoTracking().ToListAsync();
        var allTasks = await db.Tasks
            .Where(t => t.DeletedAt == null && t.EpicId != null && !t.IsMissed)
            .Include(t => t.TaskCompletions)
            .AsNoTracking()
            .ToListAsync();

        foreach (var epic in epics)
        {
            epic.Tasks = allTasks.Where(t => t.EpicId == epic.Id && (
                (t.RecurrenceMask != 0 && RecurrenceDays.IsActiveOn(t.RecurrenceMask, today)) ||
                IsSticky(t, today) ||
                t.ScheduledDate >= today
            )).ToList();
        }
        return epics;
    }

    // ── Visibility helpers ────────────────────────────────────────────────────

    private static bool IsVisibleOnDay(TodoTask t, DateOnly date)
    {
        if (t.RecurrenceMask != 0) return RecurrenceDays.IsActiveOn(t.RecurrenceMask, date);
        if (t.ScheduledDate == date) return true;
        return IsSticky(t, date);
    }

    private static bool IsSticky(TodoTask t, DateOnly date)
    {
        if (t.RecurrenceMask != 0) return false;
        if (t.Level == TaskLevel.Daily) return false;
        if (t.IsMissed) return false;
        if (t.ScheduledDate >= date) return false;

        var (start, end) = GetPeriodBounds(t.Level, date);
        if (t.ScheduledDate < start || t.ScheduledDate > end) return false;

        return !t.TaskCompletions.Any(c => c.Date >= start && c.Date <= end);
    }

    private static bool IsStickyInRange(TodoTask t, DateOnly from, DateOnly to)
    {
        if (t.RecurrenceMask != 0) return false;
        if (t.Level == TaskLevel.Daily) return false;
        if (t.IsMissed) return false;
        for (var d = from; d <= to; d = d.AddDays(1))
            if (IsSticky(t, d)) return true;
        return false;
    }

    private static (DateOnly start, DateOnly end) GetPeriodBounds(TaskLevel level, DateOnly date) =>
        level switch
        {
            TaskLevel.Weekly  => (GetMonday(date), GetMonday(date).AddDays(6)),
            TaskLevel.Monthly => (new DateOnly(date.Year, date.Month, 1),
                                  new DateOnly(date.Year, date.Month, 1).AddMonths(1).AddDays(-1)),
            TaskLevel.Yearly  => (new DateOnly(date.Year, 1, 1), new DateOnly(date.Year, 12, 31)),
            _                 => (date, date)
        };

    private static DateOnly GetMonday(DateOnly date)
    {
        int days = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-days);
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
