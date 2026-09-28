namespace ChatApp.Models;

public class AvatarAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid UploadedBy { get; set; }
    public string PublicId { get; set; } = "";
    public string? ProviderAssetId { get; set; }
    public string? DeliveryUrl { get; set; }
    public long? ProviderVersion { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(10);
    public DateTime? VerifiedAt { get; set; }
    public DateTime? AttachedAt { get; set; }
    public DateTime? DeleteAfter { get; set; }
    public bool DeletePending { get; set; }
    public DateTime? DeletedAt { get; set; }
}
