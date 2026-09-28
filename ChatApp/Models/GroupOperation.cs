namespace ChatApp.Models;

// Retained receipt: retry never creates another group/link, and never reveals a token again.
public class GroupOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActorId { get; set; }
    public string Operation { get; set; } = "";
    public Guid Key { get; set; }
    public string RequestHash { get; set; } = "";
    public Guid ResultId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
