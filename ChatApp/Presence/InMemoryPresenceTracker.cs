using System.Collections.Concurrent;

namespace ChatApp.Presence;

public class InMemoryPresenceTracker : IPresenceTracker
{
    private readonly ConcurrentDictionary<Guid, HashSet<string>> _connectionsByUser = new();

    public Task AddConnectionAsync(Guid userId, string connectionId)
    {
        var set = _connectionsByUser.GetOrAdd(userId, _ => new HashSet<string>());
        lock (set)
        {
            set.Add(connectionId);
        }

        return Task.CompletedTask;
    }

    public Task RemoveConnectionAsync(Guid userId, string connectionId)
    {
        if (_connectionsByUser.TryGetValue(userId, out var connections))
        {
            lock (connections)
            {
                connections.Remove(connectionId);
                if (connections.Count == 0)
                {
                    _connectionsByUser.TryRemove(userId, out _);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> IsOnlineAsync(Guid userId)
    {
        return Task.FromResult(_connectionsByUser.TryGetValue(userId, out var connections) && connections.Count > 0);
    }

    public Task<int> GetConnectionCountAsync(Guid userId)
    {
        return Task.FromResult(_connectionsByUser.TryGetValue(userId, out var connections) ? connections.Count : 0);
    }
}
