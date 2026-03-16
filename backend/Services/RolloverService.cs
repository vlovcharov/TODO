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

        // Build list of every day we missed, from the day after last check up to and including today
        var lastChecked = DateOnly.FromDateTime(meta.LastRolloverCheck.Date);
        var missedDays  = new List<DateOnly>();
        for (var d = lastChecked.AddDays(1); d <= today; d = d.AddDays(1))
            missedDays.Add(d);

        _logger.LogInformation("Running catch-up rollover for {Count} day(s) ending {Date}", missedDays.Count, today);

        int totalRolled = 0;

        // Process each missed day in order so daily tasks chain correctly:
        // Mon missed → copy to Tue, Tue missed → copy to Wed, ... → lands on today
        foreach (var day in missedDays)
        {
            var tasks    = await store.GetTasksAsync();   // re-fetch each day to pick up copies from previous iteration
            var toUpdate = new List<TodoTask>();
            var toAdd    = new List<TodoTask>();

            foreach (var task in tasks)
            {
                // Skip recurring, already completed, already missed, subtasks, future tasks
                if (task.IsRecurring)      continue;
                if (task.IsCompleted)      continue;
                if (task.IsMissed)         continue;
                if (task.ParentId != null) continue;
                if (task.ScheduledDate >= day) continue;

                var newDate = GetNextPeriodDate(task.Level, task.ScheduledDate, day);
                if (newDate == task.ScheduledDate) continue;

                // Mark original as missed on its date
                task.IsMissed = true;
                toUpdate.Add(task);

                var copy = new TodoTask
                {
                    Title                 = task.Title,
                    Description           = task.Description,
                    Level                 = task.Level,
                    Priority              = task.Priority,
                    ScheduledDate         = newDate,
                    OriginalScheduledDate = task.OriginalScheduledDate ?? task.ScheduledDate,
                    RolloverCount         = task.RolloverCount + 1,
                    SortOrder             = task.SortOrder,
                    RecurrenceMask        = task.RecurrenceMask,
                    EpicId                = task.EpicId,
                    // IsMissed on copy: missed if new date is still in the past relative to today
                    IsMissed              = newDate < today,
                };
                toAdd.Add(copy);
                totalRolled++;
            }

            if (toUpdate.Count > 0)
                await store.SaveTasksAsync(toUpdate);

            foreach (var copy in toAdd)
                await store.SaveTaskAsync(copy);
        }

        if (totalRolled > 0)
            _logger.LogInformation("Catch-up complete: rolled over {Count} task-days", totalRolled);

        await store.UpdateLastRolloverAsync(now);
    }

    // Returns the next scheduled date for a task given the current processing day.
    // For Daily: always the next day (so it chains one step at a time).
    // For Weekly/Monthly/Yearly: jump to the next period boundary.
    private static DateOnly GetNextPeriodDate(TaskLevel level, DateOnly scheduled, DateOnly processingDay) =>
        level switch
        {
            TaskLevel.Daily   => processingDay,                                                   // one day at a time
            TaskLevel.Weekly  => GetNextMonday(processingDay),
            TaskLevel.Monthly => new DateOnly(processingDay.Year, processingDay.Month, 1).AddMonths(1),
            TaskLevel.Yearly  => new DateOnly(processingDay.Year + 1, 1, 1),
            _                 => scheduled
        };

    private static DateOnly GetNextMonday(DateOnly date)
    {
        int days = ((int)DayOfWeek.Monday - (int)date.DayOfWeek + 7) % 7;
        return date.AddDays(days == 0 ? 7 : days);
    }
}
