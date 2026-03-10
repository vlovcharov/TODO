using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Models;
using TodoApp.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────────
var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=db;Port=5432;Database=taskflow;Username=taskflow;Password=taskflow";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connStr));

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(connStr), ServiceLifetime.Scoped);

// ── App services ──────────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase;
        opts.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 10 * 1024 * 1024;
});

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration["AllowedOrigins"];
        if (string.IsNullOrWhiteSpace(origins) || origins == "*")
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        else
            policy.WithOrigins(origins.Split(',')).AllowAnyHeader().AllowAnyMethod();
    }));

builder.Services.AddScoped<DataStore>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddHostedService<RolloverService>();

var app = builder.Build();

// ── Run migrations & seed ─────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Retry loop — Postgres container may not be ready immediately on first boot
    for (int i = 0; i < 10; i++)
    {
        try
        {
            db.Database.Migrate();
            break;
        }
        catch (Exception ex) when (i < 9)
        {
            Console.WriteLine($"DB not ready yet (attempt {i + 1}/10): {ex.Message}");
            Thread.Sleep(2000);
        }
    }

    if (!db.AppMeta.Any())
    {
        db.AppMeta.Add(new AppMeta { Id = 1, LastRolloverCheck = DateTime.UtcNow });
        db.SaveChanges();
    }
}

app.UseCors();
app.MapControllers();
app.Run();
