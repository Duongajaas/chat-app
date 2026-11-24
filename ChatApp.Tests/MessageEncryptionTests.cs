using System.Security.Cryptography;
using System.Text;
using ChatApp.Models;
using ChatApp.Services;
using Xunit;

namespace ChatApp.Tests;

public class MessageEncryptionTests
{
    private readonly AesGcmMessageEncryptionService _service = new();

    [Fact]
    public void Encrypt_And_Decrypt_Produces_Original_Plaintext()
    {
        var original = "Bảo mật tin nhắn với AES-256-GCM!";
        var encrypted = _service.Encrypt(original);

        Assert.NotNull(encrypted);
        Assert.StartsWith("v1:", encrypted);
        Assert.NotEqual(original, encrypted);

        var decrypted = _service.Decrypt(encrypted);
        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void Decrypt_Plaintext_Without_Prefix_Returns_Original_For_Backward_Compatibility()
    {
        var legacyMessage = "Đây là tin nhắn cũ từ trước khi bật mã hóa.";
        var decrypted = _service.Decrypt(legacyMessage);

        Assert.Equal(legacyMessage, decrypted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Encrypt_And_Decrypt_Handle_Null_Or_Empty_Safely(string? input)
    {
        var encrypted = _service.Encrypt(input);
        Assert.True(string.IsNullOrEmpty(encrypted));

        var decrypted = _service.Decrypt(input);
        Assert.True(string.IsNullOrEmpty(decrypted));
    }

    [Fact]
    public void Tampered_Ciphertext_Returns_Safe_Placeholder_Without_Crashing()
    {
        var original = "Thông tin tuyệt mật cần bảo vệ.";
        var encrypted = _service.Encrypt(original);

        // Tamper with the base64 content
        var payloadBase64 = encrypted[3..];
        var bytes = Convert.FromBase64String(payloadBase64);
        bytes[^1] ^= 0xFF; // Flip bits in ciphertext
        var tampered = "v1:" + Convert.ToBase64String(bytes);

        var decrypted = _service.Decrypt(tampered);
        Assert.Equal("[Nội dung không thể giải mã]", decrypted);
    }

    [Fact]
    public void Truncated_Payload_Returns_Safe_Placeholder()
    {
        var tooShort = "v1:" + Convert.ToBase64String(new byte[10]); // Less than 28 bytes
        var decrypted = _service.Decrypt(tooShort);
        Assert.Equal("[Nội dung không thể giải mã]", decrypted);
    }

    [Fact]
    public void Decryption_With_Different_Key_Fails_Gracefully()
    {
        var keyA = new byte[32];
        var keyB = new byte[32];
        keyA[0] = 1;
        keyB[0] = 2;

        var serviceA = new AesGcmMessageEncryptionService(keyA);
        var serviceB = new AesGcmMessageEncryptionService(keyB);

        var encrypted = serviceA.Encrypt("Bí mật gửi cho A");
        var decrypted = serviceB.Decrypt(encrypted);

        Assert.Equal("[Nội dung không thể giải mã]", decrypted);
    }

    [Fact]
    public void Key_Resolver_Accepts_Base64_Or_Passphrase()
    {
        var raw32Bytes = new byte[32];
        RandomNumberGenerator.Fill(raw32Bytes);
        var base64Key = Convert.ToBase64String(raw32Bytes);

        var resolvedFromBase64 = AesGcmMessageEncryptionService.ResolveKey(base64Key);
        Assert.Equal(raw32Bytes, resolvedFromBase64);

        var passphrase = "Short";
        var resolvedFromPassphrase = AesGcmMessageEncryptionService.ResolveKey(passphrase);
        Assert.Equal(32, resolvedFromPassphrase.Length);
    }

    [Fact]
    public void ToResponse_Decrypts_Active_Message_And_Prioritizes_Deleted_Or_Expired()
    {
        var encrypted = _service.Encrypt("Tin nhắn bí mật");
        var activeMessage = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            SenderId = Guid.NewGuid(),
            Content = encrypted,
            Type = MessageType.Text
        };

        var activeRes = MessageService.ToResponse(activeMessage, _service);
        Assert.Equal("Tin nhắn bí mật", activeRes.Content);
        Assert.Equal("Sent", activeRes.Status);

        // Deleted message
        var deletedMessage = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            SenderId = Guid.NewGuid(),
            Content = encrypted,
            DeletedAt = DateTime.UtcNow,
            Status = MessageStatus.Deleted,
            Type = MessageType.Text
        };
        var deletedRes = MessageService.ToResponse(deletedMessage, _service);
        Assert.Equal("Tin nhắn đã được thu hồi", deletedRes.Content);
        Assert.Equal("Deleted", deletedRes.Status);

        // Expired message
        var expiredMessage = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            SenderId = Guid.NewGuid(),
            Content = encrypted,
            ExpiresAt = DateTime.UtcNow.AddSeconds(-10),
            Type = MessageType.Text
        };
        var expiredRes = MessageService.ToResponse(expiredMessage, _service);
        Assert.Equal("Tin nhắn đã tự hủy", expiredRes.Content);
        Assert.Equal("Expired", expiredRes.Status);
    }
}
