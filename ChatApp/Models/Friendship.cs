namespace ChatApp.Models;

// Dùng pattern low/high: 1 tình bạn = đúng 1 record.
// Tầng service PHẢI tự sắp Guid nhỏ hơn vào UserLowId trước khi insert.
public class Friendship
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserLowId { get; set; }
    public Guid UserHighId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
