namespace ChatApp.Models;

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    public string Type { get; set; } = default!;
    public string? Title { get; set; }
    public string? Body { get; set; }

    public NotificationStatus Status { get; set; } = NotificationStatus.Created;
    public string Channel { get; set; } = "in_app";
    public int RetryCount { get; set; } = 0;

    // jsonb
    public string? Data { get; set; }

    public bool IsRead { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
}
