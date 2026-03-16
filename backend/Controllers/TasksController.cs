using Microsoft.AspNetCore.Mvc;
using TodoApp.Models;
using TodoApp.Services;

namespace TodoApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TasksController : ControllerBase
{
    private readonly TaskService _taskService;

    public TasksController(TaskService taskService)
    {
        _taskService = taskService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tasks = await _taskService.GetAllTasksAsync();
        return Ok(tasks);
    }

    [HttpGet("range")]
    public async Task<IActionResult> GetRange(
        [FromQuery] string from,
        [FromQuery] string to)
    {
        if (!DateOnly.TryParse(from, out var fromDate) ||
            !DateOnly.TryParse(to, out var toDate))
            return BadRequest("Invalid date format. Use yyyy-MM-dd.");

        var tasks = await _taskService.GetTasksForRangeAsync(fromDate, toDate);
        return Ok(tasks);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTaskRequest req)
    {
        var task = await _taskService.CreateTaskAsync(req);
        return CreatedAtAction(nameof(GetAll), new { id = task.Id }, task);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateTaskRequest req)
    {
        var task = await _taskService.UpdateTaskAsync(id, req);
        if (task == null) return NotFound();
        return Ok(task);
    }

    [HttpPost("{id}/toggle")]
    public async Task<IActionResult> Toggle(string id, [FromBody] ToggleCompleteRequest? req = null)
    {
        var task = await _taskService.ToggleCompleteAsync(id, req?.Date);
        if (task == null) return NotFound();
        // Return all tasks — toggling can cascade to parent/subtasks
        var all = await _taskService.GetAllTasksAsync();
        return Ok(all);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var ok = await _taskService.DeleteTaskAsync(id);
        if (!ok) return NotFound();
        return NoContent();
    }

    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        await _taskService.RestoreTaskAsync(id);
        var all = await _taskService.GetAllTasksAsync();
        return Ok(all);
    }

    [HttpPost("{id}/move")]
    public async Task<IActionResult> Move(string id, [FromBody] MoveTaskRequest req)
    {
        var task = await _taskService.MoveTaskAsync(id, req);
        if (task == null) return NotFound();
        return Ok(task);
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> Reorder([FromBody] ReorderRequest req)
    {
        await _taskService.ReorderTasksAsync(req);
        return Ok();
    }
}

[ApiController]
[Route("api/[controller]")]
public class ConfigController : ControllerBase
{
    private readonly DataStore _store;

    public ConfigController(DataStore store)
    {
        _store = store;
    }

    [HttpGet]
    public async Task<IActionResult> GetConfig() =>
        Ok(await _store.GetConfigAsync());

    [HttpPut]
    public async Task<IActionResult> UpdateConfig([FromBody] TaskLevelConfig config)
    {
        await _store.SaveConfigAsync(config);
        return Ok(config);
    }
}

[ApiController]
[Route("api/tasks")]
public class BackupController : ControllerBase
{
    private readonly DataStore _store;
    public BackupController(DataStore store) { _store = store; }

    // ── Export ────────────────────────────────────────────────────────────────

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string format = "json")
    {
        var tasks = await _store.GetTasksAsync();

        if (format.ToLower() == "csv")
        {
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Id,Title,Description,Level,Priority,IsCompleted,ScheduledDate,RecurrenceMask,ParentId,RolloverCount,CreatedAt");
            foreach (var t in tasks)
            {
                csv.AppendLine(string.Join(",",
                    CsvEscape(t.Id),
                    CsvEscape(t.Title),
                    CsvEscape(t.Description ?? ""),
                    t.Level.ToString(),
                    t.Priority.ToString(),
                    t.IsCompleted ? "true" : "false",
                    t.ScheduledDate.ToString("yyyy-MM-dd"),
                    t.RecurrenceMask.ToString(),
                    CsvEscape(t.ParentId ?? ""),
                    t.RolloverCount.ToString(),
                    t.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss")
                ));
            }
            var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
            return File(bytes, "text/csv", $"taskflow-export-{DateTime.Today:yyyy-MM-dd}.csv");
        }
        else
        {
            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };
            var json = System.Text.Json.JsonSerializer.Serialize(tasks, options);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            return File(bytes, "application/json", $"taskflow-export-{DateTime.Today:yyyy-MM-dd}.json");
        }
    }

    // ── Import ────────────────────────────────────────────────────────────────

    [HttpPost("import")]
    public async Task<IActionResult> Import(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file provided.");

        var ext = Path.GetExtension(file.FileName).ToLower();
        List<TodoTask> incoming;

        try
        {
            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();

            if (ext == ".json")
            {
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                };
                incoming = System.Text.Json.JsonSerializer.Deserialize<List<TodoTask>>(content, options)
                    ?? new List<TodoTask>();
            }
            else if (ext == ".csv")
            {
                incoming = ParseCsv(content);
            }
            else
            {
                return BadRequest("Unsupported file type. Use .json or .csv");
            }
        }
        catch (Exception ex)
        {
            return BadRequest($"Failed to parse file: {ex.Message}");
        }

        // Upsert — insert new, skip existing (matched by Id)
        var existing = await _store.GetTasksAsync();
        var existingIds = existing.Select(t => t.Id).ToHashSet();
        var toInsert = incoming.Where(t => !existingIds.Contains(t.Id)).ToList();

        foreach (var task in toInsert)
            await _store.SaveTaskAsync(task);

        return Ok(new { imported = toInsert.Count, skipped = incoming.Count - toInsert.Count });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string CsvEscape(string val)
    {
        if (val.Contains(',') || val.Contains('"') || val.Contains('\n'))
            return $"\"{val.Replace("\"", "\"\"")}\"";
        return val;
    }

    private static List<TodoTask> ParseCsv(string content)
    {
        var tasks = new List<TodoTask>();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines.Skip(1)) // skip header
        {
            var cols = SplitCsvLine(line);
            if (cols.Count < 11) continue;
            tasks.Add(new TodoTask
            {
                Id              = cols[0],
                Title           = cols[1],
                Description     = string.IsNullOrEmpty(cols[2]) ? null : cols[2],
                Level           = Enum.Parse<TodoApp.Models.TaskLevel>(cols[3]),
                Priority        = Enum.Parse<TodoApp.Models.TaskPriority>(cols[4]),
                IsCompleted     = cols[5] == "true",
                ScheduledDate   = DateOnly.TryParse(cols[6], out var sd) ? sd : DateOnly.FromDateTime(DateTime.UtcNow.Date),
                RecurrenceMask  = int.TryParse(cols[7], out var rm) ? rm : 0,
                ParentId        = string.IsNullOrEmpty(cols[8]) ? null : cols[8],
                RolloverCount   = int.TryParse(cols[9], out var rc) ? rc : 0,
                CreatedAt       = DateTime.TryParse(cols[10], out var ca) ? ca : DateTime.UtcNow,
            });
        }
        return tasks;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                { current.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            { result.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        result.Add(current.ToString().TrimEnd('\r'));
        return result;
    }
}


[ApiController]
[Route("api/[controller]")]
public class EpicsController : ControllerBase
{
    private readonly DataStore _store;
    public EpicsController(DataStore store) { _store = store; }

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _store.GetEpicsAsync());

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] EpicRequest req)
    {
        var epic = new TodoApp.Models.Epic { Title = req.Title, Color = req.Color };
        await _store.SaveEpicAsync(epic);
        return CreatedAtAction(nameof(GetAll), new { id = epic.Id }, epic);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] EpicRequest req)
    {
        var epic = await _store.GetEpicAsync(id);
        if (epic == null) return NotFound();
        epic.Title = req.Title;
        epic.Color = req.Color;
        await _store.SaveEpicAsync(epic);
        return Ok(epic);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        await _store.DeleteEpicAsync(id);
        return NoContent();
    }
}

public record EpicRequest(string Title, string Color);

[ApiController]
[Route("api/schedule")]
public class DayScheduleController : ControllerBase
{
    private readonly DataStore _store;
    public DayScheduleController(DataStore store) { _store = store; }

    [HttpGet]
    public async Task<IActionResult> GetForDate([FromQuery] string date)
    {
        if (!DateOnly.TryParse(date, out var d)) return BadRequest("Invalid date");
        return Ok(await _store.GetScheduleAsync(d));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ScheduleBlockRequest req)
    {
        if (!DateOnly.TryParse(req.Date, out var d)) return BadRequest("Invalid date");
        var block = new TodoApp.Models.DayScheduleBlock
        {
            Date         = d,
            TaskId       = req.TaskId,
            Label        = req.Label,
            StartMinutes = req.StartMinutes,
            EndMinutes   = req.EndMinutes,
        };
        await _store.SaveBlockAsync(block);
        return CreatedAtAction(nameof(GetForDate), new { date = req.Date }, block);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] ScheduleBlockRequest req)
    {
        var block = await _store.GetBlockAsync(id);
        if (block == null) return NotFound();
        block.Label        = req.Label;
        block.StartMinutes = req.StartMinutes;
        block.EndMinutes   = req.EndMinutes;
        block.TaskId       = req.TaskId;
        await _store.SaveBlockAsync(block);
        return Ok(block);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        await _store.DeleteBlockAsync(id);
        return NoContent();
    }
}

public record ScheduleBlockRequest(
    string Date,
    string? TaskId,
    string Label,
    int StartMinutes,
    int EndMinutes
);
