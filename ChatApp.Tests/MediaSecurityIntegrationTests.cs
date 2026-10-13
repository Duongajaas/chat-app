using System.Security.Claims;
using ChatApp.Common;
using ChatApp.Controllers;
using ChatApp.Data;
using ChatApp.DTOs;
using ChatApp.Media;
using ChatApp.Models;
using ChatApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ChatApp.Tests;

public class MediaSecurityIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Media_checks_asset_owner_membership_periods_hide_recall_and_session()
    {
        User[] users; Guid group; PrivateMediaAsset asset; MessageResponse message;
        await using (var db = fixture.Open())
        {
            users = Enumerable.Range(0, 3).Select(_ => new User { Username = Guid.NewGuid().ToString("N"), FullName = "Media test" }).ToArray();
            db.Users.AddRange(users);
            foreach (var u in users)
                db.RefreshTokens.Add(new RefreshToken { UserId = u.Id, FamilyId = u.Id, TokenHash = Guid.NewGuid().ToString(), ExpiresAt = DateTime.UtcNow.AddDays(1) });
            foreach (var u in users.Skip(1))
            {
                var ids = new[] { users[0].Id, u.Id }.Order().ToArray();
                db.Friendships.Add(new Friendship { UserLowId = ids[0], UserHighId = ids[1] });
            }
            asset = new PrivateMediaAsset { OwnerId = users[0].Id, StorageKey = "chatapp_private/" + Guid.NewGuid().ToString("N"),
                FileName = "voice.wav", ContentType = "audio/wav", Length = 12, Uploaded = true };
            db.PrivateMediaAssets.Add(asset); await db.SaveChangesAsync();
            group = (await new ConversationService(db).CreateConversationAsync(users[0].Id, new(ConversationType.Group, "Private", [users[1].Id]))).Id;
            message = await new MessageService(db, new BlockService(db)).SendMessageAsync(users[0].Id, Guid.NewGuid(), group,
                new(null, AssetId: asset.Id, VoiceDuration: 3));
            Assert.Null(message.MediaUrl); Assert.Single(message.Attachments!);
            var retry = await new MessageService(db, new BlockService(db)).SendMessageAsync(users[0].Id, message.ClientMessageId!.Value, group,
                new(null, AssetId: asset.Id, VoiceDuration: 3));
            Assert.Equal(message.Id, retry.Id);
            Assert.Single(await db.MessageAttachments.Where(a => a.MessageId == message.Id).ToArrayAsync());
            var receipt = Assert.Single(await new MessageReconciliation(db).ReconcileAsync(users[0].Id, group,
                [new ReconcileItem(message.ClientMessageId.Value, AssetId: asset.Id, VoiceDuration: 3)], default));
            Assert.Equal("Readable", receipt.State);
        }
        var attachment = message.Attachments![0].Id;
        var storage = new Mock<IPrivateMediaStorage>();
        storage.Setup(s => s.DownloadAsync(asset.StorageKey, 12, It.IsAny<CancellationToken>())).ReturnsAsync(new byte[12]);
        async Task<IActionResult> Download(User user)
        {
            await using var db = fixture.Open();
            var controller = new FilesController(db, storage.Object) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
            controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("sid", user.Id.ToString())], "test"));
            return await controller.Download(message.Id, attachment, default);
        }
        Assert.IsType<FileContentResult>(await Download(users[1]));
        await Assert.ThrowsAsync<AppException>(() => Download(users[2]));
        await using (var db = fixture.Open())
        {
            await new GroupService(db).AddAsync(users[0].Id, group, users[2].Id);
            await Assert.ThrowsAsync<AppException>(() => new MessageService(db, new BlockService(db)).SendMessageAsync(users[2].Id, Guid.NewGuid(), group, new(null, AssetId: asset.Id)));
        }
        // A later member cannot download a file attached before their membership period.
        await Assert.ThrowsAsync<AppException>(() => Download(users[2]));
        await using (var db = fixture.Open()) await new GroupService(db).RemoveAsync(users[0].Id, group, users[1].Id, true);
        // Leaving/kicking preserves access to periods the member was entitled to read.
        Assert.IsType<FileContentResult>(await Download(users[1]));
        await using (var db = fixture.Open()) await new MessageService(db, new BlockService(db)).DeleteForMeAsync(users[1].Id, message.Id);
        await Assert.ThrowsAsync<AppException>(() => Download(users[1]));
        await using (var db = fixture.Open()) await new MessageService(db, new BlockService(db)).DeleteMessageAsync(users[0].Id, message.Id);
        await Assert.ThrowsAsync<AppException>(() => Download(users[0]));
        storage.Verify(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
