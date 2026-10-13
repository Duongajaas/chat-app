namespace ChatApp.Calls;

public sealed class CallOptions
{
    public int RingSeconds { get; set; } = 30;
    public int LeaseSeconds { get; set; } = 60;
    public int ConnectSeconds { get; set; } = 30;
    public int RecoverySeconds { get; set; } = 20;
    public string[] IceUrls { get; set; } = ["stun:stun.l.google.com:19302"];
    public string[] TurnUrls { get; set; } = [];
    public string TurnSecret { get; set; } = "";
    public int CredentialSeconds { get; set; } = 3600;
}
