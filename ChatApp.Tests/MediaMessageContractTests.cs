using System.Text.Json;
using ChatApp.DTOs;
using ChatApp.Models;
using ChatApp.Services;
using ChatApp.Hubs;
using Xunit;

namespace ChatApp.Tests;

public class MediaMessageContractTests
{
    [Fact]
    public void Text_retry_hash_remains_compatible_and_media_changes_identity()
    {
        var conversation = Guid.NewGuid();
        var text = new SendMessageRequest("hello");
        var legacy = GroupService.Hash(JsonSerializer.Serialize(new { conversationId = conversation,
            Content = "hello", ReplyToMessageId = (Guid?)null, Mentions = Array.Empty<MentionInput>(), sourceId = (Guid?)null }));
        Assert.Equal(legacy, MessageReconciliation.Hash(conversation, text, null));
        Assert.NotEqual(legacy, MessageReconciliation.Hash(conversation,
            new SendMessageRequest("hello", MediaUrl: "https://example.test/image.png"), null));
        Assert.NotEqual(MessageReconciliation.Hash(conversation,
            new SendMessageRequest(null, MediaUrl: "https://example.test/audio.webm", VoiceDuration: 3), null),
            MessageReconciliation.Hash(conversation,
            new SendMessageRequest(null, MediaUrl: "https://example.test/audio.webm", VoiceDuration: 4), null));
    }

    [Fact]
    public void Response_exposes_media_only_while_message_is_available()
    {
        var message = new Message { Content = "", MediaUrl = "https://example.test/audio.webm", VoiceDuration = 3, WaveformPoints = "[1,2]" };
        var sent = MessageService.ToResponse(message);
        Assert.Equal(message.MediaUrl, sent.MediaUrl);
        Assert.Equal(3, sent.VoiceDuration);
        Assert.Equal("[1,2]", sent.WaveformPoints);
        message.DeletedAt = DateTime.UtcNow;
        var recalled = MessageService.ToResponse(message);
        Assert.Null(recalled.MediaUrl);
        Assert.Null(recalled.VoiceDuration);
        Assert.Null(recalled.WaveformPoints);
    }

    [Fact]
    public void Hub_does_not_bypass_persisted_http_send()
    {
        Assert.Null(typeof(ChatHub).GetMethod("SendMessage"));
    }
}
