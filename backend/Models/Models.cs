using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TodoApp.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaskLevel { Daily, Weekly, Monthly, Yearly }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaskPriority { Low, Average, High }

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

public class Epic
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string Title { get; set; } = "";

    /// <summary>Hex color e.g. "#6366f1"</summary>
    public string Color { get; set; } = "#6366f1";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TodoTask> Tasks { get; set; } = new List<TodoTask>();
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

    /// <summary>Denormalised cache — source of truth is TaskCompletions.</summary>
    public bool IsCompleted { get; set; } = false;

    /// <summary>True when this is a rollover copy whose original was not done on its day.</summary>
    public bool IsMissed { get; set; } = false;

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

    /// <summary>Optional link to an Epic.</summary>
    public string? EpicId { get; set; }

    [ForeignKey(nameof(EpicId))]
    [JsonIgnore]
    public Epic? Epic { get; set; }

    [JsonIgnore]
    public ICollection<TaskCompletion> TaskCompletions { get; set; } = new List<TaskCompletion>();

    [NotMapped]
    public List<DateOnly> CompletedDates => TaskCompletions.Select(r => r.Date).ToList();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Null = active. Set = soft-deleted, hidden from all queries.</summary>
    public DateTime? DeletedAt { get; set; } = null;
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

    public DateOnly Date { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}

public class DayScheduleBlock
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public DateOnly Date { get; set; }

    /// <summary>Optional link to a task. Null for freeform blocks.</summary>
    public string? TaskId { get; set; }

    [ForeignKey(nameof(TaskId))]
    [JsonIgnore]
    public TodoTask? Task { get; set; }

    /// <summary>Display label — defaults to task title if linked, otherwise freeform text.</summary>
    public string Label { get; set; } = "";

    /// <summary>Minutes from midnight, e.g. 540 = 09:00.</summary>
    public int StartMinutes { get; set; }

    /// <summary>Minutes from midnight, e.g. 600 = 10:00.</summary>
    public int EndMinutes { get; set; }
}

public class AppMeta
{
    [Key]
    public int Id { get; set; } = 1;
    public DateTime LastRolloverCheck { get; set; } = DateTime.UtcNow;
    public bool ShowYearly  { get; set; } = true;
    public bool ShowMonthly { get; set; } = true;
    public bool ShowWeekly  { get; set; } = true;
    public bool ShowDaily   { get; set; } = true;
}

public class TaskLevelConfig
{
    public bool ShowYearly  { get; set; } = true;
    public bool ShowMonthly { get; set; } = true;
    public bool ShowWeekly  { get; set; } = true;
    public bool ShowDaily   { get; set; } = true;
}
