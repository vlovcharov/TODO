using TodoApp.Models;

namespace TodoApp.Services;

public class RolloverService
{
    private readonly DataStore _store;
    private readonly ILogger<RolloverService> _logger;

    public RolloverService(DataStore store, ILogger<RolloverService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<(int rolled, int missed)> DoRolloverAsync()
    {
        var meta  = await _store.GetMetaAsync();
        var now   = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        if (meta.LastRolloverCheck.Date >= now.Date)
        {
            _logger.LogInformation("Rollover skipped — already ran today ({Date})", today);
            return (0, 0);
        }

        var lastChecked = DateOnly.FromDateTime(meta.LastRolloverCheck.Date);
        var gapDays     = today.DayNumber - lastChecked.DayNumber;

        _logger.LogInformation("Running catch-up rollover: {Days} day(s) since last check, up to {Date}", gapDays, today);

        var tasks      = await _store.GetTasksAsync();
        var toUpdate   = new List<TodoTask>();
        var toAdd      = new List<TodoTask>();
        int totalAdded = 0;

        foreach (var task in tasks)
        {
            if (task.IsRecurring)      continue;
            if (task.IsCompleted)      continue;
            if (task.IsMissed)         continue;
            if (task.ParentId != null) continue;
            if (task.ScheduledDate >= today) continue;

            if (task.Level != TaskLevel.Daily && IsCompletedInPeriod(task, today))
                continue;

            task.IsMissed = true;
            toUpdate.Add(task);

            var prevDate   = task.ScheduledDate;
            var originDate = task.OriginalScheduledDate ?? task.ScheduledDate;
            var rollCount  = task.RolloverCount;

            while (true)
            {
                var nextDate = GetNextPeriodDate(task.Level, prevDate, prevDate.AddDays(1));
                if (nextDate == prevDate) break;
                if (nextDate > today) nextDate = today;

                rollCount++;
                var isLast = nextDate >= today;

                var copy = new TodoTask
                {
                    Title                 = task.Title,
                    Description           = task.Description,
                    Level                 = task.Level,
                    Priority              = task.Priority,
                    ScheduledDate         = isLast ? today : nextDate,
                    OriginalScheduledDate = originDate,
                    RolloverCount         = rollCount,
                    SortOrder             = task.SortOrder,
                    RecurrenceMask        = task.RecurrenceMask,
                    EpicId                = task.EpicId,
                    IsMissed              = !isLast,
                };
                toAdd.Add(copy);
                totalAdded++;

                if (isLast) break;
                prevDate = nextDate;
            }
        }

        if (toUpdate.Count > 0)
            await _store.SaveTasksAsync(toUpdate);

        foreach (var copy in toAdd)
            await _store.SaveTaskAsync(copy);

        if (totalAdded > 0)
            _logger.LogInformation("Catch-up complete: {Count} task copies created", totalAdded);

        await _store.UpdateLastRolloverAsync(now);
        return (totalAdded, toUpdate.Count);
    }

    // Given a task's current scheduled date, returns the next date it should appear on.
    // Called with processingDay = scheduledDate + 1 day, so Daily advances one day at a time.
    private static DateOnly GetNextPeriodDate(TaskLevel level, DateOnly scheduled, DateOnly processingDay) =>
        level switch
        {
            TaskLevel.Daily   => processingDay,                                                        // next calendar day
            TaskLevel.Weekly  => GetNextMonday(scheduled),                                             // next Monday after scheduled
            TaskLevel.Monthly => new DateOnly(scheduled.Year, scheduled.Month, 1).AddMonths(1),        // first of next month
            TaskLevel.Yearly  => new DateOnly(scheduled.Year + 1, 1, 1),                               // first of next year
            _                 => scheduled
        };

    // Returns true if the task has a completion record within its current period.
    // Weekly: within the same Mon–Sun week as scheduledDate
    // Monthly: within the same calendar month
    // Yearly: within the same calendar year
    private static bool IsCompletedInPeriod(TodoTask task, DateOnly today)
    {
        if (task.TaskCompletions == null || task.TaskCompletions.Count == 0) return false;

        var scheduled = task.ScheduledDate;
        (DateOnly start, DateOnly end) = task.Level switch
        {
            TaskLevel.Weekly  => (GetMonday(scheduled), GetMonday(scheduled).AddDays(6)),
            TaskLevel.Monthly => (new DateOnly(scheduled.Year, scheduled.Month, 1),
                                  new DateOnly(scheduled.Year, scheduled.Month, 1).AddMonths(1).AddDays(-1)),
            TaskLevel.Yearly  => (new DateOnly(scheduled.Year, 1, 1),
                                  new DateOnly(scheduled.Year, 12, 31)),
            _                 => (scheduled, scheduled)
        };

        return task.TaskCompletions.Any(c => c.Date >= start && c.Date <= end);
    }

    private static DateOnly GetMonday(DateOnly date)
    {
        int days = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-days);
    }

    private static DateOnly GetNextMonday(DateOnly date)
    {
        int days = ((int)DayOfWeek.Monday - (int)date.DayOfWeek + 7) % 7;
        return date.AddDays(days == 0 ? 7 : days);
    }
}
