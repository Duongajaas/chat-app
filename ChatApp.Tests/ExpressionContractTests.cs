using ChatApp.Common;
using ChatApp.DTOs;
using ChatApp.Media;
using ChatApp.Models;
using ChatApp.Services;
using Xunit;

namespace ChatApp.Tests;

public class ExpressionContractTests
{
    private readonly ExpressionCatalog catalog = new(new ExpressionOptions());

    [Fact]
    public void Catalog_accepts_new_packs_but_rejects_untrusted_urls_and_duplicate_ids()
    {
        var options = new ExpressionOptions();
        Assert.True(ExpressionCatalog.IsValid(options));
        var sticker = new ExpressionMedia("sticker", "local", "new-sticker", "New", "/stickers/new-sticker.webp", PackId: "new-pack");
        options.StickerPacks = [.. options.StickerPacks, new("new-pack", "New pack", [sticker])];
        Assert.True(ExpressionCatalog.IsValid(options));
        Assert.Equal(sticker, new ExpressionCatalog(options).Normalize(new("sticker", "new-sticker", "new-pack")));
        options.StickerPacks = [new("new-pack", "New pack", [sticker with { Url = "https://evil.test/sticker.svg" }])];
        Assert.False(ExpressionCatalog.IsValid(options));
        options.StickerPacks = [new("new-pack", "New pack", [sticker, sticker])];
        Assert.False(ExpressionCatalog.IsValid(options));
    }

    [Fact]
    public void Sticker_metadata_comes_from_the_catalog_and_pack_must_match()
    {
        var sticker = catalog.Normalize(new("sticker", "cat-happy", "cats"));
        Assert.Equal("/stickers/cat-happy.svg", sticker.Url);
        Assert.Equal("local", sticker.Provider);
        Assert.Throws<AppException>(() => catalog.Normalize(new("sticker", "cat-happy", "other")));
        Assert.Throws<AppException>(() => catalog.Normalize(new("sticker", "../../secret")));
        Assert.Throws<AppException>(() => catalog.Normalize(new("sticker", "nonexistent")));
    }

    [Theory]
    [InlineData("https://evil.test/media")]
    [InlineData("../asset")]
    [InlineData("")]
    [InlineData("abc\n")]
    public void Gif_reference_rejects_urls_and_invalid_ids(string id) =>
        Assert.Throws<AppException>(() => catalog.Normalize(new("gif", id)));

    [Fact]
    public void Gif_persistence_contains_only_provider_reference_and_recall_hides_media()
    {
        var media = catalog.Normalize(new("gif", "abc123"));
        Assert.Null(media.Url); Assert.Null(media.ThumbnailUrl);
        var message = new Message { Type = MessageType.Gif, ExpressionJson = ExpressionCatalog.Serialize(media) };
        Assert.Equal("abc123", MessageService.ToResponse(message).Expression!.Id);
        message.DeletedAt = DateTime.UtcNow;
        Assert.Null(MessageService.ToResponse(message).Expression);
    }

    [Fact]
    public void Retry_identity_includes_kind_id_and_pack_without_changing_text_hashes()
    {
        var room = Guid.NewGuid();
        var a = new SendMessageRequest("", Expression: new("sticker", "cat-happy", "cats"));
        Assert.Equal(MessageReconciliation.Hash(room, a, null), MessageReconciliation.Hash(room, a, null));
        Assert.NotEqual(MessageReconciliation.Hash(room, a, null), MessageReconciliation.Hash(room,
            a with { Expression = new("sticker", "cat-love", "cats") }, null));
        Assert.NotEqual(MessageReconciliation.Hash(room, a, null), MessageReconciliation.Hash(room,
            a with { Expression = new("gif", "cat123") }, null));
        Assert.NotEqual(MessageReconciliation.Hash(room, a, null), MessageReconciliation.Hash(room,
            a with { Expression = new("sticker", "cat-happy", "other") }, null));
    }
}
