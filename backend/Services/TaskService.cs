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

    public async Task<List<TodoTask>> GetTasksForRangeAsync(DateOnly from, DateOnly to)
    {
        var tasks = await _store.GetTasksAsync();
        return tasks.Where(t =>
        {
            if (t.IsRecurring) return true;
            return t.ScheduledDate >= from && t.ScheduledDate <= to;
        }).OrderBy(t => t.SortOrder).ToList();
    }

    public async Task<TodoTask> CreateTaskAsync(CreateTaskRequest req)
    {
        var task = new TodoTask
        {
            Title         = req.Title,
            Description   = req.Description,
            Level         = req.Level,
            Priority      = req.Priority,
            ScheduledDate = req.ScheduledDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date),
            RecurrenceMask = req.RecurrenceMask,
            ParentId      = req.ParentId,
            SortOrder     = await _store.GetNextSortOrderAsync(
                req.ScheduledDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date))
        };

        await _store.SaveTaskAsync(task);
        return await _store.GetTaskAsync(task.Id) ?? task;
    }

    public async Task<TodoTask?> UpdateTaskAsync(string id, UpdateTaskRequest req)
    {
        var task = await _store.GetTaskAsync(id);
        if (task == null) return null;

        if (req.Title != null) task.Title = req.Title;
        if (req.Description != null) task.Description = req.Description == "" ? null : req.Description;
        if (req.Level.HasValue) task.Level = req.Level.Value;
        if (req.Priority.HasValue) task.Priority = req.Priority.Value;
        if (req.ScheduledDate.HasValue) task.ScheduledDate = req.ScheduledDate.Value;
        if (req.SortOrder.HasValue) task.SortOrder = req.SortOrder.Value;
        if (req.RecurrenceMask.HasValue)
        {
            task.RecurrenceMask = req.RecurrenceMask.Value;
            if (!task.IsRecurring && task.ScheduledDateStr == "")
                task.ScheduledDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        }

        await _store.SaveTaskAsync(task);
        return await _store.GetTaskAsync(id);
    }

    public async Task<TodoTask?> ToggleCompleteAsync(string id, DateOnly? date = null)
    {
        var task = await _store.GetTaskAsync(id);
        if (task == null) return null;

        if (task.IsRecurring)
        {
            var checkDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
            if (task.CompletedDates.Contains(checkDate))
                await _store.RemoveRecurringCompletionAsync(id, checkDate);
            else
                await _store.AddRecurringCompletionAsync(id, checkDate);
        }
        else
        {
            var nowCompleted = !task.IsCompleted;
            task.IsCompleted = nowCompleted;
            await _store.SaveTaskAsync(task);

            if (nowCompleted)
                await CompleteAllSubtasksAsync(id);

            if (task.ParentId != null)
                await SyncParentCompletionAsync(task.ParentId);
        }

        return await _store.GetTaskAsync(id);
    }

    private async Task CompleteAllSubtasksAsync(string parentId)
    {
        var subtasks = await _store.GetSubtasksAsync(parentId);
        foreach (var sub in subtasks)
        {
            if (!sub.IsCompleted) { sub.IsCompleted = true; await _store.SaveTaskAsync(sub); }
            await CompleteAllSubtasksAsync(sub.Id);
        }
    }

    private async Task SyncParentCompletionAsync(string parentId)
    {
        var parent = await _store.GetTaskAsync(parentId);
        if (parent == null) return;

        var siblings = await _store.GetSubtasksAsync(parentId);
        var allDone  = siblings.Count > 0 && siblings.All(s => s.IsCompleted);

        if (parent.IsCompleted != allDone)
        {
            parent.IsCompleted = allDone;
            await _store.SaveTaskAsync(parent);
            if (parent.ParentId != null)
                await SyncParentCompletionAsync(parent.ParentId);
        }
    }

    public async Task<bool> DeleteTaskAsync(string id)
    {
        var task = await _store.GetTaskAsync(id);
        if (task == null) return false;
        await _store.DeleteTaskAsync(id);
        return true;
    }

    public async Task<TodoTask?> MoveTaskAsync(string id, MoveTaskRequest req)
    {
        var task = await _store.GetTaskAsync(id);
        if (task == null) return null;

        task.ScheduledDate = req.NewDate;
        if (req.NewSortOrder.HasValue) task.SortOrder = req.NewSortOrder.Value;

        await _store.SaveTaskAsync(task);
        return await _store.GetTaskAsync(id);
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
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public record CreateTaskRequest(
    string Title,
    string? Description,
    TaskLevel Level,
    TaskPriority Priority,
    DateOnly? ScheduledDate,
    int RecurrenceMask,
    string? ParentId
);

public record UpdateTaskRequest(
    string? Title,
    string? Description,
    TaskLevel? Level,
    TaskPriority? Priority,
    DateOnly? ScheduledDate,
    int? SortOrder,
    int? RecurrenceMask
);

public record MoveTaskRequest(DateOnly NewDate, int? NewSortOrder);
public record ReorderRequest(List<string> TaskIds);
public record ToggleCompleteRequest(DateOnly? Date);
