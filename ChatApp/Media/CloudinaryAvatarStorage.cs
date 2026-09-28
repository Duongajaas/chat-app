using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChatApp.Common;
using Microsoft.Extensions.Options;

namespace ChatApp.Media;

public class CloudinaryOptions
{
    public string CloudName { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public string AvatarUploadPreset { get; set; } = "";
}
public record AvatarUpload(string Url, Dictionary<string, string> Fields);
public record VerifiedAvatar(string AssetId, string PublicId, long Version, long Bytes, int Width, int Height, string Format, string Url);
public interface IAvatarStorage
{
    bool IsConfigured { get; }
    AvatarUpload Prepare(string publicId);
    Task<VerifiedAvatar> InspectAsync(string publicId, CancellationToken ct);
    Task DeleteAsync(string publicId, CancellationToken ct);
}

public class CloudinaryAvatarStorage(HttpClient http, IOptions<CloudinaryOptions> options) : IAvatarStorage
{
    private CloudinaryOptions Settings => options.Value;
    public bool IsConfigured => Regex.IsMatch(Settings.CloudName, "^[a-zA-Z0-9_-]{1,100}$") &&
        !string.IsNullOrWhiteSpace(Settings.ApiKey) && !string.IsNullOrWhiteSpace(Settings.ApiSecret) &&
        !string.IsNullOrWhiteSpace(Settings.AvatarUploadPreset);
    private void EnsureConfigured()
    {
        if (!IsConfigured) throw new AppException("Chưa cấu hình Cloudinary cho avatar.", 503);
    }
    public static string Sign(IReadOnlyDictionary<string, string> fields, string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("&", fields.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value)) + secret))).ToLowerInvariant();
    public AvatarUpload Prepare(string publicId)
    {
        EnsureConfigured();
        var fields = new Dictionary<string, string> {
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ["public_id"] = publicId, ["overwrite"] = "false", ["upload_preset"] = Settings.AvatarUploadPreset,
            ["allowed_formats"] = "jpg,png,webp", ["transformation"] = "c_limit,w_512,h_512"
        };
        fields["signature"] = Sign(fields, Settings.ApiSecret);
        fields["api_key"] = Settings.ApiKey;
        return new($"https://api.cloudinary.com/v1_1/{Settings.CloudName}/image/upload", fields);
    }
    private HttpRequestMessage Request(HttpMethod method, string suffix)
    {
        EnsureConfigured();
        var request = new HttpRequestMessage(method, $"https://api.cloudinary.com/v1_1/{Settings.CloudName}/{suffix}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(Settings.ApiKey + ":" + Settings.ApiSecret)));
        return request;
    }
    public async Task<VerifiedAvatar> InspectAsync(string publicId, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, "resources/image/upload/" + Uri.EscapeDataString(publicId));
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) throw AppException.BadRequest("Ảnh chưa được tải lên.");
        if (!response.IsSuccessStatusCode) throw new AppException("Không xác minh được ảnh trên Cloudinary.", 503);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = document.RootElement;
        var format = root.GetProperty("format").GetString()!;
        var version = root.GetProperty("version").GetInt64();
        if (root.GetProperty("resource_type").GetString() != "image" || root.GetProperty("type").GetString() != "upload")
            throw AppException.BadRequest("Tài sản không phải ảnh avatar công khai.");
        return new(root.GetProperty("asset_id").GetString()!, root.GetProperty("public_id").GetString()!, version,
            root.GetProperty("bytes").GetInt64(), root.GetProperty("width").GetInt32(), root.GetProperty("height").GetInt32(), format,
            $"https://res.cloudinary.com/{Settings.CloudName}/image/upload/c_fill,w_128,h_128,q_auto,f_auto/v{version}/{publicId}.{format}");
    }
    public async Task DeleteAsync(string publicId, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Post, "image/destroy");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["public_id"] = publicId, ["invalidate"] = "true" });
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new AppException("Không xóa được ảnh cũ trên Cloudinary.", 503);
    }
}
