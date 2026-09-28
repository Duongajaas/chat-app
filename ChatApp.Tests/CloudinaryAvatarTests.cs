using ChatApp.Common;
using ChatApp.Media;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using Xunit;

namespace ChatApp.Tests;

public class CloudinaryAvatarTests
{
    private static CloudinaryAvatarStorage Storage(HttpMessageHandler handler) => new(new HttpClient(handler),
        Microsoft.Extensions.Options.Options.Create(new CloudinaryOptions { CloudName = "test-cloud", ApiKey = "key", ApiSecret = "secret", AvatarUploadPreset = "avatars" }));
    [Fact]
    public void Signed_intent_binds_public_id_transform_and_overwrite_without_exposing_secret()
    {
        var upload = Storage(new Handler(_ => throw new Exception("No network expected"))).Prepare("chatapp/avatars/group/asset");
        Assert.Equal("https://api.cloudinary.com/v1_1/test-cloud/image/upload", upload.Url);
        Assert.Equal("false", upload.Fields["overwrite"]);
        Assert.Equal("c_limit,w_512,h_512", upload.Fields["transformation"]);
        Assert.DoesNotContain("secret", upload.Fields.Values);
        var fields = upload.Fields.Where(f => f.Key is not ("signature" or "api_key")).ToDictionary();
        Assert.Equal(CloudinaryAvatarStorage.Sign(fields, "secret"), upload.Fields["signature"]);
        fields["public_id"] = "other";
        Assert.NotEqual(CloudinaryAvatarStorage.Sign(fields, "secret"), upload.Fields["signature"]);
    }
    [Fact]
    public async Task Verification_uses_authenticated_provider_metadata_and_constructs_canonical_url()
    {
        var storage = Storage(new Handler(request => {
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            Assert.Equal("key:secret", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)));
            Assert.Contains("resources/image/upload/", request.RequestUri!.AbsoluteUri);
            return new(HttpStatusCode.OK) { Content = new StringContent("""
                {"resource_type":"image","type":"upload","format":"png","version":42,"asset_id":"provider-id",
                 "public_id":"chatapp/avatars/a","bytes":1000,"width":128,"height":128,"secure_url":"https://untrusted.test"}
                """) };
        }));
        var asset = await storage.InspectAsync("chatapp/avatars/a", CancellationToken.None);
        Assert.Equal("provider-id", asset.AssetId);
        Assert.Equal("https://res.cloudinary.com/test-cloud/image/upload/c_fill,w_128,h_128,q_auto,f_auto/v42/chatapp/avatars/a.png", asset.Url);
    }
    [Theory]
    [InlineData(HttpStatusCode.NotFound, 400)]
    [InlineData(HttpStatusCode.TooManyRequests, 503)]
    public async Task Provider_failure_is_not_treated_as_verified(HttpStatusCode status, int expected)
    {
        var storage = Storage(new Handler(_ => new(status)));
        Assert.Equal(expected, (await Assert.ThrowsAsync<AppException>(() => storage.InspectAsync("missing", CancellationToken.None))).StatusCode);
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }
}
