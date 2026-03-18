using Microsoft.AspNetCore.Mvc;
using TodoApp.Models;
using TodoApp.Services;

namespace TodoApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TasksController : ControllerBase
{
    private readonly TaskService _taskService;
    private readonly ILogger<TasksController> _logger;

    public TasksController(TaskService taskService, ILogger<TasksController> logger)
    {
        _taskService = taskService;
        _logger = logger;
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
        _logger.LogInformation("Task created: [{Level}] \"{Title}\" (id={Id}, date={Date})",
            task.Level, task.Title, task.Id, task.ScheduledDate);
        return CreatedAtAction(nameof(GetAll), new { id = task.Id }, task);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateTaskRequest req)
    {
        var task = await _taskService.UpdateTaskAsync(id, req);
        if (task == null) return NotFound();
        _logger.LogInformation("Task updated: \"{Title}\" (id={Id})", task.Title, task.Id);
        return Ok(task);
    }

    [HttpPost("{id}/toggle")]
    public async Task<IActionResult> Toggle(string id, [FromBody] ToggleCompleteRequest? req = null)
    {
        var all = await _taskService.ToggleCompleteAsync(id, req?.Date);
        if (all == null) return NotFound();
        _logger.LogInformation("Task toggled (id={Id}, date={Date})", id, req?.Date?.ToString() ?? "own date");
        return Ok(all);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var ok = await _taskService.DeleteTaskAsync(id);
        if (!ok) return NotFound();
        _logger.LogInformation("Task soft-deleted (id={Id})", id);
        return NoContent();
    }

    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        await _taskService.RestoreTaskAsync(id);
        _logger.LogInformation("Task restored (id={Id})", id);
        var all = await _taskService.GetAllTasksAsync();
        return Ok(all);
    }

    [HttpPost("{id}/move")]
    public async Task<IActionResult> Move(string id, [FromBody] MoveTaskRequest req)
    {
        var task = await _taskService.MoveTaskAsync(id, req);
        if (task == null) return NotFound();
        _logger.LogInformation("Task moved: \"{Title}\" → {NewDate} (id={Id})", task.Title, req.NewDate, task.Id);
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
    private readonly ILogger<BackupController> _logger;

    public BackupController(DataStore store, ILogger<BackupController> logger)
    {
        _store = store;
        _logger = logger;
    }

    // ── Export ────────────────────────────────────────────────────────────────

    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        var tasks = await _store.GetTasksAsync();
        var epics = await _store.GetEpicsAsync();

        var payload = new { version = 2, epics, tasks };

        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        var json  = System.Text.Json.JsonSerializer.Serialize(payload, options);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        _logger.LogInformation("Export: {TaskCount} tasks, {EpicCount} epics", tasks.Count, epics.Count);
        return File(bytes, "application/json", $"taskflow-export-{DateTime.Today:yyyy-MM-dd}.json");
    }

    // ── Import ────────────────────────────────────────────────────────────────

    [HttpPost("import")]
    public async Task<IActionResult> Import(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file provided.");

        if (!Path.GetExtension(file.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only .json files are supported.");

        string content;
        try
        {
            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream);
            content = await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Import failed: could not read file {FileName}", file.FileName);
            return BadRequest($"Could not read file: {ex.Message}");
        }

        var jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        List<TodoApp.Models.Epic> incomingEpics = new();
        List<TodoTask> incomingTasks;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                // v1 — plain array of tasks, no epics
                incomingTasks = System.Text.Json.JsonSerializer.Deserialize<List<TodoTask>>(content, jsonOptions)
                    ?? new();
                // Clear epicId — referenced epics don't exist in this DB
                foreach (var t in incomingTasks) t.EpicId = null;
                _logger.LogInformation("Import: detected v1 format (no epics)");
            }
            else
            {
                // v2 — { version, epics, tasks }
                incomingEpics = root.TryGetProperty("epics", out var ep)
                    ? System.Text.Json.JsonSerializer.Deserialize<List<TodoApp.Models.Epic>>(ep.GetRawText(), jsonOptions) ?? new()
                    : new();
                incomingTasks = root.TryGetProperty("tasks", out var tk)
                    ? System.Text.Json.JsonSerializer.Deserialize<List<TodoTask>>(tk.GetRawText(), jsonOptions) ?? new()
                    : new();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Import failed: could not parse {FileName}", file.FileName);
            return BadRequest($"Failed to parse file: {ex.Message}");
        }

        // Upsert epics first (tasks may reference them)
        var existingEpics  = await _store.GetEpicsAsync();
        var existingEpicIds = existingEpics.Select(e => e.Id).ToHashSet();
        int epicsImported  = 0;
        foreach (var epic in incomingEpics.Where(e => !existingEpicIds.Contains(e.Id)))
        {
            await _store.SaveEpicAsync(epic);
            epicsImported++;
        }

        // Upsert tasks — parents must be inserted before subtasks
        var existingTasks   = await _store.GetTasksAsync();
        var existingTaskIds = existingTasks.Select(t => t.Id).ToHashSet();
        var toInsert = incomingTasks.Where(t => !existingTaskIds.Contains(t.Id))
                                    .OrderBy(t => t.ParentId == null ? 0 : 1) // parents first
                                    .ToList();
        foreach (var task in toInsert)
            await _store.SaveTaskAsync(task);

        _logger.LogInformation("Import complete: file={FileName}, epics={Epics}, tasks={Tasks}, skipped={Skipped}",
            file.FileName, epicsImported, toInsert.Count, incomingTasks.Count - toInsert.Count);

        return Ok(new { importedEpics = epicsImported, importedTasks = toInsert.Count, skippedTasks = incomingTasks.Count - toInsert.Count });
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
