namespace ChatApp.Models;

public class BlockCooldown
{
    public Guid ActorId { get; set; }
    public Guid TargetId { get; set; }
    public DateTime LastUnblockedAt { get; set; }
    public DateTime ReblockAllowedAt { get; set; }
}
