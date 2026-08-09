namespace ChatApp.Options;

public class SmtpOptions 
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = default!;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = default!;
    public string Password { get; set; } = default!;
    public string FromEmail { get; set; } = default!;
    public string FromName { get; set; } = "ChatApp";
    public bool UseSsl { get; set; } = false;
}

public class AppUrlsOptions {
    public const string SectionName = "AppUrls";
    
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";
}