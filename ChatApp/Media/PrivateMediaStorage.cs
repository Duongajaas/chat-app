using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChatApp.Common;
using CloudinaryDotNet;
using Microsoft.Extensions.Options;

namespace ChatApp.Media;

public interface IPrivateMediaStorage
{
    Task UploadAsync(string key, byte[] bytes, CancellationToken ct);
    Task<byte[]> DownloadAsync(string key, long expectedLength, CancellationToken ct);
}

public sealed class PrivateMediaStorage(HttpClient http, IOptions<CloudinaryOptions> options) : IPrivateMediaStorage
{
    private CloudinaryOptions Settings
    {
        get
        {
            var s = options.Value;
            if (!Regex.IsMatch(s.CloudName, "^[a-zA-Z0-9_-]+$") || string.IsNullOrWhiteSpace(s.ApiKey) || string.IsNullOrWhiteSpace(s.ApiSecret))
                throw new AppException("Chưa cấu hình kho media riêng tư.", 503);
            return s;
        }
    }

    public async Task UploadAsync(string key, byte[] bytes, CancellationToken ct)
    {
        var s = Settings;
        using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"https://api.cloudinary.com/v1_1/{s.CloudName}/raw/upload");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(s.ApiKey + ":" + s.ApiSecret)));
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", "media");
        form.Add(new StringContent(key), "public_id");
        form.Add(new StringContent("authenticated"), "type");
        form.Add(new StringContent("false"), "overwrite");
        request.Content = form;
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new AppException("Không tải được media. Vui lòng thử lại.", 503);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        if (root.GetProperty("public_id").GetString() != key || root.GetProperty("type").GetString() != "authenticated" ||
            root.GetProperty("resource_type").GetString() != "raw" || root.GetProperty("bytes").GetInt64() != bytes.LongLength)
            throw new AppException("Kho media trả về tài sản không hợp lệ.", 503);
    }

    public async Task<byte[]> DownloadAsync(string key, long expectedLength, CancellationToken ct)
    {
        var s = Settings;
        var cloud = new Cloudinary(new Account(s.CloudName, s.ApiKey, s.ApiSecret));
        // The signed provider URL never leaves the server.
        var url = cloud.Api.Url.Secure(true).ResourceType("raw").Type("authenticated").Signed(true).BuildUrl(key);
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new AppException("Media tạm thời không khả dụng.", 503);
        if (expectedLength is <= 0 or > MediaValidation.MaxBytes || response.Content.Headers.ContentLength > MediaValidation.MaxBytes)
            throw new AppException("Media không hợp lệ.", 503);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + read > expectedLength) throw new AppException("Media không hợp lệ.", 503);
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (output.Length != expectedLength) throw new AppException("Media không đầy đủ.", 503);
        return output.ToArray();
    }
}

public static class MediaValidation
{
    public const int MaxBytes = 8 * 1024 * 1024;
    public static string Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12) throw AppException.BadRequest("File không hợp lệ.");
        if (bytes.StartsWith(new byte[] { 0xff, 0xd8, 0xff })) return "image/jpeg";
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        if (bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WAVE"u8)) return "audio/wav";
        if (bytes[..4].SequenceEqual("OggS"u8)) return "audio/ogg";
        if (bytes.StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 })) return "audio/webm";
        throw AppException.BadRequest("Chỉ hỗ trợ JPEG, PNG, WebP và âm thanh WebM, Ogg, WAV.");
    }

    public static Guid? AssetId(Guid? assetId, string? reference)
    {
        if (reference == null) return assetId;
        const string prefix = "/api/media/assets/";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(reference[prefix.Length..], "D", out var parsed) ||
            (assetId.HasValue && assetId != parsed))
            throw AppException.BadRequest("Hãy upload media trước và gửi assetId nội bộ; không nhận URL công khai.");
        return parsed;
    }
}
