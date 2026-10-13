namespace ChatApp.Models;

public class PrivateMediaAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public string StorageKey { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Length { get; set; }
    public bool Uploaded { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
