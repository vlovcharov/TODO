using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TodoApp.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaskLevel { Daily, Weekly, Monthly, Yearly, LifeGoal }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaskPriority { Low, Average, High }

/// <summary>
/// Weekday bitmask — bit 0 = Monday, bit 1 = Tuesday, ... bit 6 = Sunday.
/// </summary>
public static class RecurrenceDays
{
    public const int None        = 0;
    public const int Monday      = 1 << 0;
    public const int Tuesday     = 1 << 1;
    public const int Wednesday   = 1 << 2;
    public const int Thursday    = 1 << 3;
    public const int Friday      = 1 << 4;
    public const int Saturday    = 1 << 5;
    public const int Sunday      = 1 << 6;
    public const int WorkingDays = Monday | Tuesday | Wednesday | Thursday | Friday;
    public const int EveryDay    = 127;

    public static bool IsActiveOn(int mask, DateOnly date)
    {
        if (mask == None) return false;
        int bit = date.DayOfWeek switch
        {
            DayOfWeek.Monday    => Monday,
            DayOfWeek.Tuesday   => Tuesday,
            DayOfWeek.Wednesday => Wednesday,
            DayOfWeek.Thursday  => Thursday,
            DayOfWeek.Friday    => Friday,
            DayOfWeek.Saturday  => Saturday,
            DayOfWeek.Sunday    => Sunday,
            _                   => 0
        };
        return (mask & bit) != 0;
    }
}

public class TodoTask
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string Title { get; set; } = "";
    public string? Description { get; set; }

    public TaskLevel Level { get; set; } = TaskLevel.Daily;
    public TaskPriority Priority { get; set; } = TaskPriority.Average;

    /// <summary>
    /// Denormalised cache — true if completed today (or ever, for non-recurring one-offs).
    /// Source of truth is TaskCompletions table.
    /// </summary>
    public bool IsCompleted { get; set; } = false;

    // Proper Postgres date columns (no string workarounds needed)
    public DateOnly ScheduledDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.Date);
    public DateOnly? OriginalScheduledDate { get; set; }

    public int SortOrder { get; set; } = 0;
    public int RolloverCount { get; set; } = 0;

    public string? ParentId { get; set; }

    [ForeignKey(nameof(ParentId))]
    [JsonIgnore]
    public TodoTask? Parent { get; set; }

    [JsonIgnore]
    public ICollection<TodoTask> Subtasks { get; set; } = new List<TodoTask>();

    [NotMapped]
    public List<string> SubtaskIds => Subtasks.Select(s => s.Id).ToList();

    public int RecurrenceMask { get; set; } = RecurrenceDays.None;

    [NotMapped]
    [JsonIgnore]
    public bool IsRecurring => RecurrenceMask != RecurrenceDays.None;

    [JsonIgnore]
    public ICollection<TaskCompletion> TaskCompletions { get; set; } = new List<TaskCompletion>();

    /// <summary>List of dates this task was completed, sent to frontend.</summary>
    [NotMapped]
    public List<DateOnly> CompletedDates => TaskCompletions.Select(r => r.Date).ToList();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class TaskCompletion
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string TaskId { get; set; } = "";

    [ForeignKey(nameof(TaskId))]
    [JsonIgnore]
    public TodoTask? Task { get; set; }

    /// <summary>Which day this completion belongs to (for day-based queries).</summary>
    public DateOnly Date { get; set; }

    /// <summary>Exact moment the task was completed.</summary>
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}

public class AppMeta
{
    [Key]
    public int Id { get; set; } = 1;
    public DateTime LastRolloverCheck { get; set; } = DateTime.UtcNow;
    public bool ShowLifeGoal { get; set; } = true;
    public bool ShowYearly { get; set; } = true;
    public bool ShowMonthly { get; set; } = true;
    public bool ShowWeekly { get; set; } = true;
    public bool ShowDaily { get; set; } = true;
}

public class TaskLevelConfig
{
    public bool ShowLifeGoal { get; set; } = true;
    public bool ShowYearly   { get; set; } = true;
    public bool ShowMonthly  { get; set; } = true;
    public bool ShowWeekly   { get; set; } = true;
    public bool ShowDaily    { get; set; } = true;
}
