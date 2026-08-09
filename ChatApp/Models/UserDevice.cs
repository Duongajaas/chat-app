namespace ChatApp.Models;

public class UserDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string DeviceToken { get; set; } = default!;
    public DevicePlatform Platform { get; set; }
    public string? DeviceName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastActiveAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
