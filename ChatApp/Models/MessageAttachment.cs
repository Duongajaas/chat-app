namespace ChatApp.Models;

public class MessageAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }

    // Quy tắc bảo mật: lưu object key/path trong storage, KHÔNG lưu public URL trực tiếp.
    // URL thật chỉ được generate động theo signed URL khi cần xem/tải file.
    public string StorageKey { get; set; } = default!;

    public string? FileName { get; set; }
    public string? FileType { get; set; }
    public long? FileSize { get; set; }

    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? DurationSeconds { get; set; }

    public string? ThumbnailKey { get; set; }

    // jsonb: dùng cho STICKER (sticker_pack_id, sticker_id), GIF (source, animated)...
    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
