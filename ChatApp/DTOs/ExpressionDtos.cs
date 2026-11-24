using System.ComponentModel.DataAnnotations;

namespace ChatApp.DTOs;

// Clients select an ID; media URLs are never accepted from a message payload.
public record ExpressionInput(
    [Required, RegularExpression("^(sticker|gif)$")] string Kind,
    [Required, StringLength(80, MinimumLength = 1)] string Id,
    [StringLength(80)] string? PackId = null);

public record ExpressionMedia(string Kind, string Provider, string Id, string Name,
    string? Url = null, string? ThumbnailUrl = null, int Width = 240, int Height = 240,
    string? PackId = null, string? FileType = null, bool Animated = false);

public record StickerPack(string Id, string Name, ExpressionMedia[] Stickers);
