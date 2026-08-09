namespace ChatApp.Models;

public class Report
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReporterId { get; set; }

    public ReportTargetType TargetType { get; set; }

    // Polymorphic reference (tùy TargetType trỏ tới users.id / messages.id / conversations.id)
    // — KHÔNG set FK cứng, validate ở tầng service.
    public Guid TargetId { get; set; }

    public string Reason { get; set; } = default!;
    public string? Description { get; set; }

    public ReportStatus Status { get; set; } = ReportStatus.Pending;

    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
