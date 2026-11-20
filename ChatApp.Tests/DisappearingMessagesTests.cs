using System.Text.Json;
using ChatApp.DTOs;
using ChatApp.Models;
using ChatApp.Services;
using Xunit;

namespace ChatApp.Tests;

public class DisappearingMessagesTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void MessageResponse_Serializes_ExpiresAt_And_TtlSeconds()
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(5);
        var response = new MessageResponse(
            Id: Guid.NewGuid(),
            ConversationId: Guid.NewGuid(),
            SenderId: Guid.NewGuid(),
            Sequence: 1,
            Content: "Tin nhắn tự hủy sau 5 phút",
            ClientMessageId: Guid.NewGuid(),
            CreatedAt: DateTime.UtcNow,
            ExpiresAt: expiresAt,
            TtlSeconds: 300
        );

        var json = JsonSerializer.Serialize(response, WebOptions);
        Assert.Contains("\"expiresAt\":", json);
        Assert.Contains("\"ttlSeconds\":300", json);

        var deserialized = JsonSerializer.Deserialize<MessageResponse>(json, WebOptions);
        Assert.NotNull(deserialized);
        Assert.Equal(300, deserialized.TtlSeconds);
        Assert.NotNull(deserialized.ExpiresAt);
    }

    [Fact]
    public void ToResponse_Returns_Expired_Status_And_Text_When_Past_Deadline()
    {
        var expiredMessage = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            SenderId = Guid.NewGuid(),
            Sequence = 10,
            Content = "Nội dung này đã hết hạn",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            ExpiresAt = DateTime.UtcNow.AddSeconds(-5),
            TtlSeconds = 60,
            Type = MessageType.Text
        };

        var response = MessageService.ToResponse(expiredMessage);
        Assert.Equal("Expired", response.Status);
        Assert.Equal("Tin nhắn đã tự hủy", response.Content);
        Assert.Null(response.VoiceDuration);
        Assert.Null(response.WaveformPoints);
    }

    [Fact]
    public void ToResponse_Returns_Normal_Sent_Status_When_Not_Yet_Expired()
    {
        var futureMessage = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            SenderId = Guid.NewGuid(),
            Sequence = 10,
            Content = "Nội dung còn hiệu lực",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            TtlSeconds = 600,
            Type = MessageType.Text
        };

        var response = MessageService.ToResponse(futureMessage);
        Assert.Equal("Sent", response.Status);
        Assert.Equal("Nội dung còn hiệu lực", response.Content);
        Assert.Equal(600, response.TtlSeconds);
        Assert.NotNull(response.ExpiresAt);
    }

    [Fact]
    public void ConversationSummaryResponse_Includes_DefaultMessageTtlSeconds()
    {
        var summary = new ConversationSummaryResponse(
            Id: Guid.NewGuid(),
            Name: "Nhóm bảo mật",
            Type: ConversationType.Group,
            UnreadCount: 0,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            LastMessageId: null,
            LastMessageAt: null,
            DefaultMessageTtlSeconds: 86400
        );

        Assert.Equal(86400, summary.DefaultMessageTtlSeconds);

        var json = JsonSerializer.Serialize(summary, WebOptions);
        Assert.Contains("\"defaultMessageTtlSeconds\":86400", json);
    }

    [Fact]
    public void SetDisappearingMessagesRequest_Serializes_Correctly()
    {
        var request = new SetDisappearingMessagesRequest(3600);
        var json = JsonSerializer.Serialize(request, WebOptions);
        var deserialized = JsonSerializer.Deserialize<SetDisappearingMessagesRequest>(json, WebOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(3600, deserialized.TtlSeconds);
    }

    [Fact]
    public void SendMessagePayload_Includes_TtlSeconds()
    {
        var payload = new SendMessagePayload(
            ClientMessageId: Guid.NewGuid(),
            Content: "Tin nhắn có TTL",
            TtlSeconds: 120
        );

        Assert.Equal(120, payload.TtlSeconds);
    }
}
