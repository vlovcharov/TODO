using TodoApp.Models;

namespace TodoApp.Services;

public class TaskService
{
    private readonly DataStore _store;

    public TaskService(DataStore store)
    {
        _store = store;
    }

    public async Task<List<TodoTask>> GetAllTasksAsync() =>
        await _store.GetTasksAsync();

    public async Task<List<TodoTask>> GetTasksForDayAsync(DateOnly date) =>
        await _store.GetTasksForDayAsync(date);

    public async Task<List<TodoTask>> GetTasksForRangeAsync(DateOnly from, DateOnly to) =>
        await _store.GetTasksForRangeAsync(from, to);

    public async Task<List<Epic>> GetEpicsWithTasksAsync(DateOnly today) =>
        await _store.GetEpicsWithTasksAsync(today);

    public async Task<TodoTask> CreateTaskAsync(CreateTaskRequest req)
    {
        var task = new TodoTask
        {
            Title          = req.Title,
            Description    = req.Description,
            Level          = req.Level,
            Priority       = req.Priority,
            ScheduledDate  = req.ScheduledDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date),
            RecurrenceMask = req.RecurrenceMask,
            ParentId       = req.ParentId,
            EpicId         = req.EpicId,
            SortOrder      = await _store.GetNextSortOrderAsync(
                                 req.ScheduledDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date))
        };
        await _store.SaveTaskAsync(task);
        return await _store.GetTaskAsync(task.Id) ?? task;
    }

    public async Task<TodoTask?> UpdateTaskAsync(string id, UpdateTaskRequest req)
    {
        var task = await _store.GetTaskAsync(id);
        if (task == null) return null;

        if (req.Title != null)         task.Title         = req.Title;
        if (req.Description != null)   task.Description   = req.Description == "" ? null : req.Description;
        if (req.Level.HasValue)        task.Level         = req.Level.Value;
        if (req.Priority.HasValue)     task.Priority      = req.Priority.Value;
        if (req.ScheduledDate.HasValue) task.ScheduledDate = req.ScheduledDate.Value;
        if (req.SortOrder.HasValue)    task.SortOrder     = req.SortOrder.Value;
        if (req.RecurrenceMask.HasValue) task.RecurrenceMask = req.RecurrenceMask.Value;
        if (req.EpicId != null)        task.EpicId        = req.EpicId == "" ? null : req.EpicId;

        await _store.SaveTaskAsync(task);
        return await _store.GetTaskAsync(id);
    }

    public async Task<List<TodoTask>> ToggleCompleteAsync(string id, DateOnly? date = null)
    {
        var checkDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var task = await _store.GetTaskAsync(id);
        if (task == null) return await _store.GetTasksAsync();

        var isNowCompleted = !await _store.IsCompletedOnDateAsync(id, checkDate);

        if (isNowCompleted)
        {
            await _store.AddCompletionAsync(id, checkDate);
            await CompleteSubtasksForDateAsync(id, checkDate, true);
        }
        else
        {
            await _store.RemoveCompletionAsync(id, checkDate);
            await CompleteSubtasksForDateAsync(id, checkDate, false);
        }

        await UpdateIsCompletedCacheAsync(id, checkDate);

        if (task.ParentId != null)
            await SyncParentCompletionAsync(task.ParentId, checkDate);

        return await _store.GetTasksAsync();
    }

    private async Task CompleteSubtasksForDateAsync(string parentId, DateOnly date, bool complete)
    {
        var subtasks = await _store.GetSubtasksAsync(parentId);
        foreach (var sub in subtasks)
        {
            if (complete) await _store.AddCompletionAsync(sub.Id, date);
            else          await _store.RemoveCompletionAsync(sub.Id, date);
            await UpdateIsCompletedCacheAsync(sub.Id, date);
            await CompleteSubtasksForDateAsync(sub.Id, date, complete);
        }
    }

    private async Task UpdateIsCompletedCacheAsync(string taskId, DateOnly date)
    {
        var task = await _store.GetTaskAsync(taskId);
        if (task == null) return;
        var relevantDate = task.IsRecurring ? DateOnly.FromDateTime(DateTime.UtcNow.Date) : task.ScheduledDate;
        var done = await _store.IsCompletedOnDateAsync(taskId, relevantDate);
        if (task.IsCompleted != done)
        {
            task.IsCompleted = done;
            await _store.SaveTaskAsync(task);
        }
    }

    private async Task SyncParentCompletionAsync(string parentId, DateOnly date)
    {
        var parent   = await _store.GetTaskAsync(parentId);
        if (parent == null) return;
        var subtasks = await _store.GetSubtasksAsync(parentId);
        if (subtasks.Count == 0) return;

        var allDone = true;
        foreach (var sub in subtasks)
            if (!await _store.IsCompletedOnDateAsync(sub.Id, date)) { allDone = false; break; }

        var currentlyDone = await _store.IsCompletedOnDateAsync(parentId, date);
        if (currentlyDone != allDone)
        {
            if (allDone) await _store.AddCompletionAsync(parentId, date);
            else         await _store.RemoveCompletionAsync(parentId, date);
            await UpdateIsCompletedCacheAsync(parentId, date);
        }

        if (parent.ParentId != null)
            await SyncParentCompletionAsync(parent.ParentId, date);
    }

    public async Task<bool> DeleteTaskAsync(string id)
    {
        var task = await _store.GetTaskAsync(id);
        if (task == null) return false;
        await _store.DeleteTaskAsync(id);
        return true;
    }

    public async Task<bool> RestoreTaskAsync(string id)
    {
        await _store.RestoreTaskAsync(id);
        return true;
    }

    public async Task<TodoTask?> MoveTaskAsync(string id, MoveTaskRequest req)
    {
        var task = await _store.GetTaskAsync(id);
        if (task == null) return null;

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        if (task.IsMissed)
        {
            // Missed tasks: keep the original as-is (IsMissed=true on its date),
            // and create a new copy on the target date.
            // The copy is missed if the target is still in the past, active otherwise.
            var copy = new TodoTask
            {
                Title                 = task.Title,
                Description           = task.Description,
                Level                 = task.Level,
                Priority              = task.Priority,
                ScheduledDate         = req.NewDate,
                OriginalScheduledDate = task.OriginalScheduledDate ?? task.ScheduledDate,
                RolloverCount         = task.RolloverCount + 1,
                SortOrder             = task.SortOrder,
                RecurrenceMask        = task.RecurrenceMask,
                EpicId                = task.EpicId,
                IsMissed              = req.NewDate < today,
            };
            await _store.SaveTaskAsync(copy);
            return await _store.GetTaskAsync(copy.Id);
        }
        else
        {
            // Regular tasks: just move in place
            task.ScheduledDate = req.NewDate;
            if (req.NewSortOrder.HasValue) task.SortOrder = req.NewSortOrder.Value;
            await _store.SaveTaskAsync(task);
            return await _store.GetTaskAsync(id);
        }
    }

    public async Task ReorderTasksAsync(ReorderRequest req)
    {
        var tasks    = await _store.GetTasksAsync();
        var toUpdate = new List<TodoTask>();
        for (int i = 0; i < req.TaskIds.Count; i++)
        {
            var task = tasks.FirstOrDefault(t => t.Id == req.TaskIds[i]);
            if (task != null) { task.SortOrder = i; toUpdate.Add(task); }
        }
        await _store.SaveTasksAsync(toUpdate);
    }

    public async Task MoveToTopAsync(string id, DateOnly date)
    {
        var task = await _store.GetTaskAsync(id);
        if (task == null) return;

        var all = await _store.GetTasksAsync();
        // All top-level tasks visible on this day
        var siblings = all
            .Where(t => t.ParentId == null && t.Id != id && (
                t.ScheduledDate == date ||
                (t.RecurrenceMask != 0 && RecurrenceDays.IsActiveOn(t.RecurrenceMask, date))
            ))
            .OrderBy(t => t.SortOrder)
            .ToList();

        task.SortOrder = 0;
        await _store.SaveTaskAsync(task);

        for (int i = 0; i < siblings.Count; i++)
            siblings[i].SortOrder = i + 1;

        if (siblings.Count > 0)
            await _store.SaveTasksAsync(siblings);
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public record CreateTaskRequest(
    string Title,
    string? Description,
    TaskLevel Level,
    TaskPriority Priority,
    DateOnly? ScheduledDate,
    int RecurrenceMask,
    string? ParentId,
    string? EpicId
);

public record UpdateTaskRequest(
    string? Title,
    string? Description,
    TaskLevel? Level,
    TaskPriority? Priority,
    DateOnly? ScheduledDate,
    int? SortOrder,
    int? RecurrenceMask,
    string? EpicId
);

public record MoveTaskRequest(DateOnly NewDate, int? NewSortOrder);
public record ReorderRequest(List<string> TaskIds);
public record ToggleCompleteRequest(DateOnly? Date);
public record MoveToTopRequest(DateOnly Date);
