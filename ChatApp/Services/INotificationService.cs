using ChatApp.Models;

namespace ChatApp.Services;

public interface INotificationService
{
    Task<List<Notification>> GetUserNotificationsAsync(Guid userId, int skip = 0, int take = 20);
    Task<Notification> CreateAsync(Guid userId, string type, string title, string body, string? data = null, string channel = "in_app");
    Task MarkAsReadAsync(Guid userId, Guid notificationId);
    Task MarkAllAsReadAsync(Guid userId);
    Task UpdateDeliveryStatusAsync(Guid notificationId, NotificationStatus status, string? lastError = null);
}
