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
/// Special values:
///   0   = None (not recurring)
///   31  = WorkingDays (Mon–Fri, bits 0–4)
///   127 = Every day (all 7 bits)
/// Any other combination = custom schedule.
/// </summary>
public static class RecurrenceDays
{
    public const int None        = 0;
    public const int Monday      = 1 << 0;  // 1
    public const int Tuesday     = 1 << 1;  // 2
    public const int Wednesday   = 1 << 2;  // 4
    public const int Thursday    = 1 << 3;  // 8
    public const int Friday      = 1 << 4;  // 16
    public const int Saturday    = 1 << 5;  // 32
    public const int Sunday      = 1 << 6;  // 64
    public const int WorkingDays = Monday | Tuesday | Wednesday | Thursday | Friday; // 31
    public const int EveryDay    = 127;     // all 7 bits

    public static bool IsActiveOn(int mask, DateOnly date)
    {
        if (mask == None) return false;
        // DayOfWeek: Sunday=0, Monday=1 ... Saturday=6
        // Our bit order: Monday=bit0 ... Sunday=bit6
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
    public bool IsCompleted { get; set; } = false;

    // Dates stored as "yyyy-MM-dd" strings — avoids all DateOnly/SQLite issues
    public string ScheduledDateStr { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.Date).ToString("yyyy-MM-dd");
    public string? OriginalScheduledDateStr { get; set; }

    [NotMapped]
    [JsonPropertyName("scheduledDate")]
    public DateOnly ScheduledDate
    {
        get => DateOnly.Parse(ScheduledDateStr);
        set => ScheduledDateStr = value.ToString("yyyy-MM-dd");
    }

    [NotMapped]
    [JsonPropertyName("originalScheduledDate")]
    public DateOnly? OriginalScheduledDate
    {
        get => OriginalScheduledDateStr != null ? DateOnly.Parse(OriginalScheduledDateStr) : null;
        set => OriginalScheduledDateStr = value?.ToString("yyyy-MM-dd");
    }

    public int SortOrder { get; set; } = 0;
    public int RolloverCount { get; set; } = 0;

    // Self-referencing parent/child
    public string? ParentId { get; set; }

    [ForeignKey(nameof(ParentId))]
    [JsonIgnore]
    public TodoTask? Parent { get; set; }

    [JsonIgnore]
    public ICollection<TodoTask> Subtasks { get; set; } = new List<TodoTask>();

    [NotMapped]
    public List<string> SubtaskIds => Subtasks.Select(s => s.Id).ToList();

    /// <summary>Weekday bitmask. 0 = not recurring. See RecurrenceDays constants.</summary>
    public int RecurrenceMask { get; set; } = RecurrenceDays.None;

    [NotMapped]
    [JsonIgnore]
    public bool IsRecurring => RecurrenceMask != RecurrenceDays.None;

    [JsonIgnore]
    public ICollection<RecurringCompletion> RecurringCompletions { get; set; } = new List<RecurringCompletion>();

    [NotMapped]
    public List<DateOnly> CompletedDates => RecurringCompletions.Select(r => r.Date).ToList();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

}

public class RecurringCompletion
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string TaskId { get; set; } = "";

    [ForeignKey(nameof(TaskId))]
    [JsonIgnore]
    public TodoTask? Task { get; set; }

    public string DateStr { get; set; } = "";

    [NotMapped]
    [JsonIgnore]
    public DateOnly Date
    {
        get => DateOnly.Parse(DateStr);
        set => DateStr = value.ToString("yyyy-MM-dd");
    }
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
