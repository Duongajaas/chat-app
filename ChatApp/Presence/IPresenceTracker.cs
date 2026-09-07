namespace ChatApp.Presence;

public interface IPresenceTracker
{
    Task AddConnectionAsync(Guid userId, string connectionId);
    Task RemoveConnectionAsync(Guid userId, string connectionId);
    Task<bool> IsOnlineAsync(Guid userId);
    Task<int> GetConnectionCountAsync(Guid userId);
}
