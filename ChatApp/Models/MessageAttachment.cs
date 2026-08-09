namespace ChatApp.Models;

public class MessageAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }

    public string FileUrl { get; set; } = default!;
    public string? FileName { get; set; }
    public string? FileType { get; set; }
    public long? FileSize { get; set; }

    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? DurationSeconds { get; set; }

    public string? ThumbnailUrl { get; set; }

    // jsonb: dùng cho STICKER (sticker_pack_id, sticker_id), GIF (source, animated)...
    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
