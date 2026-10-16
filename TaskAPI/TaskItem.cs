using System.ComponentModel.DataAnnotations;

public enum Priority { Low = 0, Medium = 1, High = 2 }

public class TaskItem
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public bool IsCompleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Priority Priority { get; set; }

    // Ручная версия для оптимистичной блокировки
    public uint Version { get; set; }
}