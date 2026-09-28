namespace ChatApp.Presence;

public sealed class PresenceOptions
{
    public int HeartbeatSeconds { get; set; } = 15;
    public int ConnectionTtlSeconds { get; set; } = 60;
    public int SweepSeconds { get; set; } = 5;
}
