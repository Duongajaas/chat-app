using Microsoft.Extensions.Options;
using MailKit.Security;
using MailKit.Net.Smtp;
using ChatApp.Options;
using MimeKit;

namespace ChatApp.Services;

public interface IEmailService
{
    Task SendPasswordResetEmailAsync(string toEmail, string resetToken);
}

public class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;
    private readonly SmtpOptions _smtpOptions;
    private readonly AppUrlsOptions _appUrlsOptions;
    
    public EmailService(
        ILogger<EmailService> logger,
        IOptions<SmtpOptions> smtpOptions,
        IOptions<AppUrlsOptions> appUrlsOptions) 
    {
        _logger = logger;
        _smtpOptions = smtpOptions.Value;
        _appUrlsOptions = appUrlsOptions.Value;
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string resetToken) {
        var encodeToken = Uri.EscapeDataString(resetToken);
        var url = $"{_appUrlsOptions.FrontendBaseUrl}/reset-password?token={encodeToken}";

        try {   
            var message = new MimeMessage();
            var fromName = _smtpOptions.FromName;
            var fromEmail = _smtpOptions.FromEmail;
            var host = _smtpOptions.Host;
            var port = _smtpOptions.Port;
            var username = _smtpOptions.Username;
            var password = _smtpOptions.Password;
            var secureSocketOption = _smtpOptions.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

            Console.WriteLine($"[DEBUG] Current dir = {username}");

            message.From.Add(new MailboxAddress(fromName, fromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "Quên mật khẩu";
            var htmlBody = $"""
                <h2>Đặt lại mật khẩu</h2>
                <p>Bạn (hoặc ai đó) vừa yêu cầu đặt lại mật khẩu cho tài khoản này.</p>
                <p>Nhấn vào link bên dưới, link hết hạn sau <strong>30 phút</strong>:</p>
                <p><a href="{url}">{url}</a></p>
                <p>Nếu bạn không yêu cầu điều này, hãy bỏ qua email — mật khẩu vẫn an toàn.</p>
                """;

            var textBody = $"""
                Nhấn vào link sau để đặt lại mật khẩu (hết hạn sau 30 phút):
                {url}

                Nếu bạn không yêu cầu điều này, hãy bỏ qua email này.
                """;

            message.Body = new BodyBuilder
            {
                HtmlBody = htmlBody,
                TextBody = textBody
            }.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, secureSocketOption);
            await client.AuthenticateAsync(username, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
        catch (Exception ex) {
             _logger.LogError(ex, "Gửi email thất bại tới {Email}", toEmail);
        }
    }
}
