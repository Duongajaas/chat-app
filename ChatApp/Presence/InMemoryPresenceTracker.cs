namespace ChatApp.Presence;

// Single-instance deployment. Every dictionary/set access shares the same lock.
public class InMemoryPresenceTracker : IPresenceTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, HashSet<string>> _connections = new();
    public Task AddConnectionAsync(Guid userId, string connectionId)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(userId, out var set)) _connections[userId] = set = [];
            set.Add(connectionId);
        }
        return Task.CompletedTask;
    }
    public Task RemoveConnectionAsync(Guid userId, string connectionId)
    {
        lock (_gate)
        {
            if (_connections.TryGetValue(userId, out var set) && set.Remove(connectionId) && set.Count == 0)
                _connections.Remove(userId);
        }
        return Task.CompletedTask;
    }
    public Task<bool> IsOnlineAsync(Guid userId)
    {
        lock (_gate) return Task.FromResult(_connections.ContainsKey(userId));
    }
    public Task<int> GetConnectionCountAsync(Guid userId)
    {
        lock (_gate) return Task.FromResult(_connections.TryGetValue(userId, out var set) ? set.Count : 0);
    }
}
