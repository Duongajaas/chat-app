using System.Text.Json;
using System.Text.RegularExpressions;
using ChatApp.Common;
using ChatApp.DTOs;

namespace ChatApp.Media;

public sealed class ExpressionOptions
{
    public StickerPack[] StickerPacks { get; set; } =
    [
        new("cats", "Mèo đáng yêu", [
            Sticker("cat-happy", "Mèo vui", "cats"), Sticker("cat-love", "Mèo yêu thương", "cats")]),
        new("feelings", "Gửi yêu thương", [
            Sticker("heart", "Trái tim rung rinh", "feelings", true), Sticker("hello", "Xin chào", "feelings")]),
        new("celebrate", "Cùng ăn mừng", [
            Sticker("star", "Bạn thật tuyệt", "celebrate"), Sticker("party", "Ăn mừng", "celebrate", true)])
    ];

    private static ExpressionMedia Sticker(string id, string name, string pack, bool animated = false) =>
        new("sticker", "local", id, name, $"/stickers/{id}{(animated ? "-animated" : "")}.svg",
            $"/stickers/{id}.svg", PackId: pack, FileType: "image/svg+xml", Animated: animated);
}

public sealed class ExpressionCatalog(ExpressionOptions options)
{
    public StickerPack[] Packs => options.StickerPacks;

    public static bool IsValid(ExpressionOptions options)
    {
        if (options.StickerPacks is not { Length: > 0 }) return false;
        var stickers = options.StickerPacks.SelectMany(pack => pack.Stickers ?? []).ToArray();
        static bool Asset(string? url) => url != null && Regex.IsMatch(url,
            "\\A/stickers/[a-z0-9-]+\\.(svg|webp|png|gif)\\z", RegexOptions.CultureInvariant);
        return options.StickerPacks.Select(pack => pack.Id).Distinct().Count() == options.StickerPacks.Length &&
            stickers.Select(sticker => sticker.Id).Distinct().Count() == stickers.Length &&
            options.StickerPacks.All(pack => !string.IsNullOrWhiteSpace(pack.Id) && !string.IsNullOrWhiteSpace(pack.Name) &&
                pack.Stickers is { Length: > 0 } && pack.Stickers.All(sticker => sticker.Kind == "sticker" && sticker.Provider == "local" &&
                    sticker.PackId == pack.Id && !string.IsNullOrWhiteSpace(sticker.Id) && !string.IsNullOrWhiteSpace(sticker.Name) &&
                    sticker.Width is > 0 and <= 2048 && sticker.Height is > 0 and <= 2048 && Asset(sticker.Url) &&
                    (!sticker.Animated || Asset(sticker.ThumbnailUrl))));
    }

    public ExpressionMedia Normalize(ExpressionInput input)
    {
        if (input.Kind == "sticker")
        {
            var match = Packs.SelectMany(p => p.Stickers).SingleOrDefault(s => s.Id == input.Id &&
                (input.PackId == null || s.PackId == input.PackId));
            return match ?? throw AppException.BadRequest("Sticker không tồn tại hoặc không được phép sử dụng.");
        }
        if (input.Kind != "gif" || input.PackId != null || input.Id == null ||
            !Regex.IsMatch(input.Id, "\\A[a-zA-Z0-9]{1,64}\\z", RegexOptions.CultureInvariant))
            throw AppException.BadRequest("Mã GIF không hợp lệ.");
        // Persist only the provider ID. Browsers resolve current metadata directly with GIPHY,
        // which avoids storing stale provider URLs or proxying GIPHY requests.
        return new("gif", "giphy", input.Id, "GIF từ GIPHY", Animated: true);
    }

    public static string Serialize(ExpressionMedia media) => JsonSerializer.Serialize(media);

    public static ExpressionMedia? Read(string? json) =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<ExpressionMedia>(json);
}
