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

        var meta = await store.GetMetaAsync();
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        if (meta.LastRolloverCheck.Date >= now.Date) return;

        _logger.LogInformation("Running daily rollover for {Date}", today);

        var tasks = await store.GetTasksAsync();
        var toUpdate = new List<TodoTask>();

        foreach (var task in tasks)
        {
            if (task.IsRecurring) continue;
            if (task.IsCompleted) continue;
            if (task.ScheduledDate >= today) continue;

            var newDate = GetNextPeriodDate(task.Level, task.ScheduledDate, today);
            if (newDate != task.ScheduledDate)
            {
                task.OriginalScheduledDate ??= task.ScheduledDate;
                task.ScheduledDate = newDate;
                task.RolloverCount++;
                toUpdate.Add(task);
            }
        }

        if (toUpdate.Count > 0)
        {
            await store.SaveTasksAsync(toUpdate);
            _logger.LogInformation("Rolled over {Count} tasks", toUpdate.Count);
        }

        await store.UpdateLastRolloverAsync(now);
    }

    private static DateOnly GetNextPeriodDate(TaskLevel level, DateOnly scheduled, DateOnly today) =>
        level switch
        {
            TaskLevel.Daily   => today,
            TaskLevel.Weekly  => GetNextMonday(today),
            TaskLevel.Monthly => new DateOnly(today.Year, today.Month, 1).AddMonths(1),
            TaskLevel.Yearly  => new DateOnly(today.Year + 1, 1, 1),
            _                 => scheduled
        };

    private static DateOnly GetNextMonday(DateOnly date)
    {
        int days = ((int)DayOfWeek.Monday - (int)date.DayOfWeek + 7) % 7;
        return date.AddDays(days == 0 ? 7 : days);
    }
}
