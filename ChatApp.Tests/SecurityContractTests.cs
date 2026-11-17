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

    [Theory]
    [InlineData("report.pdf", "%PDF-1.7\n%âãÏÓ\n", "application/pdf")]
    [InlineData("notes.docx", "PK\u0003\u0004\u0014\u0000\u0006\u0000\u0008\u0000\u0000\u0000", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("sheet.XLSX", "PK\u0003\u0004\u0014\u0000\u0006\u0000\u0008\u0000\u0000\u0000", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("slides.pptx", "PK\u0003\u0004\u0014\u0000\u0006\u0000\u0008\u0000\u0000\u0000", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData("archive.zip", "PK\u0003\u0004\u0014\u0000\u0006\u0000\u0008\u0000\u0000\u0000", "application/zip")]
    [InlineData("empty.zip", "PK\u0005\u0006\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000", "application/zip")]
    [InlineData("todo.txt", "Mua sữa\nHọp nhóm", "text/plain")]
    [InlineData("data.csv", "a,b\n1,2", "text/csv")]
    [InlineData("readme.md", "# Tiêu đề", "text/markdown")]
    [InlineData("photo.png", "\u0089PNG\r\n\u001a\n\u0000\u0000\u0000\u0000", "image/png")]
    public void Documents_are_detected_from_content_and_extension(string name, string latin1Content, string expected)
    {
        var bytes = name.EndsWith(".txt") || name.EndsWith(".csv") || name.EndsWith(".md")
            ? Encoding.UTF8.GetBytes(latin1Content) : Encoding.Latin1.GetBytes(latin1Content);
        Assert.Equal(expected, MediaValidation.Detect(bytes, name));
    }

    [Fact]
    public void Legacy_word_files_use_the_ole_signature()
    {
        var ole = new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1, 0, 0, 0, 0 };
        Assert.Equal("application/msword", MediaValidation.Detect(ole, "old.doc"));
        Assert.Equal("application/vnd.ms-excel", MediaValidation.Detect(ole, "old.xls"));
        Assert.Throws<AppException>(() => MediaValidation.Detect(ole, "setup.msi"));
    }

    [Theory]
    [InlineData("app.jar")]      // ZIP container that is not an allowed document type
    [InlineData("payload.docx.exe")]
    [InlineData("noextension")]
    public void Zip_containers_with_other_extensions_are_rejected(string name)
    {
        var zip = Encoding.Latin1.GetBytes("PK\u0003\u0004\u0014\u0000\u0006\u0000\u0008\u0000\u0000\u0000");
        Assert.Throws<AppException>(() => MediaValidation.Detect(zip, name));
    }

    [Fact]
    public void Executables_markup_and_binary_text_are_rejected()
    {
        Assert.Throws<AppException>(() => MediaValidation.Detect(Encoding.Latin1.GetBytes("MZ\u0090\u0000\u0003\u0000\u0000\u0000\u0004\u0000\u0000\u0000"), "tool.exe"));
        Assert.Throws<AppException>(() => MediaValidation.Detect(Encoding.UTF8.GetBytes("<!doctype html><script>alert(1)</script>"), "page.html"));
        Assert.Throws<AppException>(() => MediaValidation.Detect(Encoding.UTF8.GetBytes("<svg onload='alert(1)'></svg>"), "image.txt"));
        Assert.Throws<AppException>(() => MediaValidation.Detect(Encoding.UTF8.GetBytes("﻿  <html></html>"), "page.md"));
        Assert.Throws<AppException>(() => MediaValidation.Detect(new byte[] { 0x41, 0x00, 0x42 }, "binary.txt"));
        Assert.Throws<AppException>(() => MediaValidation.Detect(new byte[] { 0xff, 0xfe, 0x41 }, "latin.txt"));
        Assert.Throws<AppException>(() => MediaValidation.Detect(Array.Empty<byte>(), "empty.txt"));
        // A PDF is still a PDF even when renamed; a .pdf that is really HTML is rejected.
        Assert.Throws<AppException>(() => MediaValidation.Detect(Encoding.UTF8.GetBytes("<html><body>not a pdf</body>"), "fake.pdf"));
    }

    [Fact]
    public void Nameless_detection_still_only_accepts_images_and_audio()
    {
        Assert.Throws<AppException>(() => MediaValidation.Detect(Encoding.Latin1.GetBytes("%PDF-1.7\n%âãÏÓ\n")));
        Assert.Throws<AppException>(() => MediaValidation.Detect(Encoding.UTF8.GetBytes("plain text file")));
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
