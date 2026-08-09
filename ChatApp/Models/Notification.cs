namespace ChatApp.Models;

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    public string Type { get; set; } = default!;
    public string? Title { get; set; }
    public string? Body { get; set; }

    // jsonb
    public string? Data { get; set; }

    public bool IsRead { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}
