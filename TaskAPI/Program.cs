using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

var builder = WebApplication.CreateBuilder(args);

// --- Сервисы ---
builder.Services.AddDbContext<TaskDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddSingleton<IRabbitMqService, RabbitMqService>();

builder.Services.AddOpenApi();

// Graceful shutdown: даём 5 секунд на закрытие RabbitMQ
builder.Services.Configure<HostOptions>(o =>
{
    o.ShutdownTimeout = TimeSpan.FromSeconds(5);
});

var app = builder.Build();

app.MapOpenApi();

// Миграции при старте (для простоты)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
    db.Database.EnsureCreated(); // HACK: в проде — Migrate()
}

// --- Graceful shutdown hook ---
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    var rabbit = app.Services.GetRequiredService<IRabbitMqService>();
    // Даём 5 секунд на закрытие
    rabbit.CloseAsync().Wait(TimeSpan.FromSeconds(5));
});

// ============================================================
#region POST /tasks
// ============================================================
app.MapPost("/tasks", async (CreateTaskRequest req, TaskDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Title))
        return Results.BadRequest(new { error = "Title must not be empty" });

    if (req.Title.Length > 200)
        return Results.BadRequest(new { error = "Title must be at most 200 characters" });

    var task = new TaskItem
    {
        Id = Guid.NewGuid(),
        Title = req.Title.Trim(),
        IsCompleted = false,
        CreatedAt = DateTimeOffset.UtcNow,
        CompletedAt = null,
        Priority = Priority.Medium,
        Version = 0
    };

    db.Tasks.Add(task);
    await db.SaveChangesAsync();

    return Results.Created($"/tasks/{task.Id}", ToDto(task));
});
#endregion

// ============================================================
#region GET /tasks
// ============================================================
app.MapGet("/tasks", async (TaskDbContext db) =>
{
    var tasks = await db.Tasks.AsNoTracking().ToListAsync();
    return Results.Ok(tasks.Select(ToDto));
});
#endregion

// ============================================================
#region PUT /tasks/{id}/complete
// ============================================================
app.MapPut("/tasks/{id:guid}/complete", async (
    Guid id,
    TaskDbContext db,
    IRabbitMqService rabbit,
    ILogger<Program> logger) =>
{
    var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id);
    if (task is null)
        return Results.NotFound();

    // Конкурентность: если уже завершена — 409
    if (task.IsCompleted)
        return Results.Conflict(new { error = "Task already completed" });

    // Оптимистичная блокировка через Version
    var originalVersion = task.Version;
    task.IsCompleted = true;
    task.CompletedAt = DateTimeOffset.UtcNow;
    task.Version = originalVersion + 1;

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        // Кто-то успел раньше
        return Results.Conflict(new { error = "Task was completed by another request" });
    }

    // Публикуем ПОСЛЕ сохранения в БД
    var msg = new TaskCompletedMessage(
        task.Id,
        task.Title,
        task.CompletedAt!.Value,
        task.Priority.ToString());

    // Не блокируем основной поток — fire and forget
    _ = Task.Run(() => rabbit.PublishTaskCompletedAsync(msg));

    return Results.Ok(ToDto(task));
});
#endregion

// ============================================================
#region DELETE /tasks/{id}
// ============================================================
app.MapDelete("/tasks/{id:guid}", async (Guid id, TaskDbContext db) =>
{
    var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id);
    if (task is null)
        return Results.NotFound();

    db.Tasks.Remove(task);
    await db.SaveChangesAsync();
    return Results.NoContent();
});
#endregion
static TaskDto ToDto(TaskItem t) =>
    new(t.Id, t.Title, t.IsCompleted, t.CreatedAt, t.CompletedAt, t.Priority.ToString());

app.Run();

// --- DTO ---
record CreateTaskRequest(string Title);
public record TaskDto(Guid Id, string Title, bool IsCompleted, DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt, string Priority);

public partial class Program { }