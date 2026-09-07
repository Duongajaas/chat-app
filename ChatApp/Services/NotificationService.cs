using ChatApp.Data;
using ChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Services;

public class NotificationService : INotificationService
{
    private readonly AppDbContext _db;

    public NotificationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<Notification>> GetUserNotificationsAsync(Guid userId, int skip = 0, int take = 20)
    {
        return await _db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<Notification> CreateAsync(Guid userId, string type, string title, string body, string? data = null, string channel = "in_app")
    {
        var notification = new Notification
        {
            UserId = userId,
            Type = type,
            Title = title,
            Body = body,
            Data = data,
            Channel = channel,
            Status = NotificationStatus.Created,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync();

        return notification;
    }

    public async Task MarkAsReadAsync(Guid userId, Guid notificationId)
    {
        var notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);

        if (notification is null)
            return;

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            notification.Status = NotificationStatus.Read;
            await _db.SaveChangesAsync();
        }
    }

    public async Task MarkAllAsReadAsync(Guid userId)
    {
        await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTime.UtcNow)
                .SetProperty(n => n.Status, NotificationStatus.Read));
    }

    public async Task UpdateDeliveryStatusAsync(Guid notificationId, NotificationStatus status, string? lastError = null)
    {
        var notification = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId);
        if (notification is null)
            return;

        notification.Status = status;
        notification.LastError = lastError;
        notification.LastAttemptAt = DateTime.UtcNow;

        if (status == NotificationStatus.Sent)
        {
            notification.SentAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }
}
