using System.Net;
using System.Text;
using ChatApp.Common;
using ChatApp.Media;
using ChatApp.Models;
using ChatApp.Services;
using Xunit;

namespace ChatApp.Tests;

public class SecurityContractTests
{
    [Theory]
    [InlineData("https://example.test/private.png")]
    [InlineData("//example.test/image")]
    [InlineData("/api/media/assets/../../secret")]
    public void Public_or_malformed_media_references_are_rejected(string reference) =>
        Assert.Throws<AppException>(() => MediaValidation.AssetId(null, reference));

    [Fact]
    public void Upload_reference_must_match_explicit_asset()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, MediaValidation.AssetId(id, "/api/media/assets/" + id));
        Assert.Throws<AppException>(() => MediaValidation.AssetId(Guid.NewGuid(), "/api/media/assets/" + id));
    }

    [Fact]
    public void Html_and_svg_cannot_be_uploaded_as_images()
    {
        Assert.Throws<AppException>(() => MediaValidation.Detect(Encoding.UTF8.GetBytes("<svg onload='alert(1)'></svg>")));
        Assert.Throws<AppException>(() => MediaValidation.Detect(Encoding.UTF8.GetBytes("<!doctype html><script>alert(1)</script>")));
        Assert.Equal("image/png", MediaValidation.Detect(new byte[] {137,80,78,71,13,10,26,10,0,0,0,0}));
    }

    [Fact]
    public void Legacy_public_media_url_never_leaves_message_response()
    {
        Assert.Null(MessageService.ToResponse(new Message { MediaUrl = "https://example.test/secret.png" }).MediaUrl);
    }

    [Fact]
    public async Task Storage_upload_is_authenticated_and_rejects_public_provider_response()
    {
        var storage = Storage(new Handler(async request => {
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            Assert.EndsWith("/raw/upload", request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("authenticated", body);
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"public_id":"chatapp_private/test","type":"upload","resource_type":"raw","bytes":12}""") };
        }));
        Assert.Equal(503, (await Assert.ThrowsAsync<AppException>(() => storage.UploadAsync("chatapp_private/test", new byte[12], default))).StatusCode);
    }

    [Fact]
    public async Task Download_uses_signed_authenticated_url_and_bounds_provider_response()
    {
        var storage = Storage(new Handler(request => {
            Assert.Equal("res.cloudinary.com", request.RequestUri!.Host);
            Assert.Contains("/raw/authenticated/s--", request.RequestUri.AbsolutePath);
            Assert.DoesNotContain("secret", request.RequestUri.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[20]) });
        }));
        await Assert.ThrowsAsync<AppException>(() => storage.DownloadAsync("chatapp_private/test", 12, default));
    }

    private static PrivateMediaStorage Storage(HttpMessageHandler handler) => new(new HttpClient(handler),
        Microsoft.Extensions.Options.Options.Create(new CloudinaryOptions { CloudName = "test-cloud", ApiKey = "key", ApiSecret = "secret" }));
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request);
    }
}
