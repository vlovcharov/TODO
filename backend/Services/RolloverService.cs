using TodoApp.Models;

namespace TodoApp.Services;

public class RolloverService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<RolloverService> _logger;

    public RolloverService(IServiceProvider services, ILogger<RolloverService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await DoRolloverAsync();
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            await DoRolloverAsync();
        }
    }

    private async Task DoRolloverAsync()
    {
        using var scope = _services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<DataStore>();

        var meta  = await store.GetMetaAsync();
        var now   = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        if (meta.LastRolloverCheck.Date >= now.Date) return;

        var lastChecked = DateOnly.FromDateTime(meta.LastRolloverCheck.Date);
        var gapDays     = today.DayNumber - lastChecked.DayNumber;

        _logger.LogInformation("Running catch-up rollover: {Days} day(s) since last check, up to {Date}", gapDays, today);

        var tasks      = await store.GetTasksAsync();
        var toUpdate   = new List<TodoTask>();
        var toAdd      = new List<TodoTask>();
        int totalAdded = 0;

        foreach (var task in tasks)
        {
            // Skip recurring, already completed, already missed, subtasks, future/today tasks
            if (task.IsRecurring)      continue;
            if (task.IsCompleted)      continue;
            if (task.IsMissed)         continue;
            if (task.ParentId != null) continue;
            if (task.ScheduledDate >= today) continue;

            // For weekly/monthly/yearly: if completed anywhere within the period, don't roll over
            if (task.Level != TaskLevel.Daily && IsCompletedInPeriod(task, today))
                continue;

            // Walk forward from the task's scheduled date one period at a time,
            // creating a missed copy for each intermediate day/period,
            // and a final active (non-missed) copy when we reach today.
            task.IsMissed = true;
            toUpdate.Add(task);

            var prevDate   = task.ScheduledDate;
            var originDate = task.OriginalScheduledDate ?? task.ScheduledDate;
            var rollCount  = task.RolloverCount;

            while (true)
            {
                var nextDate = GetNextPeriodDate(task.Level, prevDate, prevDate.AddDays(1));
                if (nextDate == prevDate) break; // safety: no progress

                // Never schedule a copy beyond today
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
                    IsMissed              = !isLast,   // intermediate copies are missed; final copy is active (today)
                };
                toAdd.Add(copy);
                totalAdded++;

                if (isLast) break;
                prevDate = nextDate;
            }
        }

        if (toUpdate.Count > 0)
            await store.SaveTasksAsync(toUpdate);

        foreach (var copy in toAdd)
            await store.SaveTaskAsync(copy);

        if (totalAdded > 0)
            _logger.LogInformation("Catch-up complete: created {Count} task copies ({Missed} missed + 1 active per chain)",
                totalAdded, totalAdded - toUpdate.Count);

        await store.UpdateLastRolloverAsync(now);
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
