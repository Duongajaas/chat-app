using ChatApp.Data;
using ChatApp.DTOs;
using ChatApp.Migrations;
using ChatApp.Models;
using ChatApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace ChatApp.Tests;

public class ExpressionMigrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Additive_repairs_can_be_reapplied_without_changing_existing_messages()
    {
        await using var db = fixture.Open();
        var first = new User { Username = Guid.NewGuid().ToString("N"), FullName = "Migration sender" };
        var second = new User { Username = Guid.NewGuid().ToString("N"), FullName = "Migration receiver" };
        db.Users.AddRange(first, second); await db.SaveChangesAsync();
        var room = await new ConversationService(db).GetOrCreateDirectConversationAsync(first.Id, second.Id);
        var service = new MessageService(db, new BlockService(db));
        var message = await service.SendMessageAsync(first.Id, Guid.NewGuid(), room.Id,
            new SendMessageRequest("", Expression: new("sticker", "cat-happy", "cats")));
        var repairSql = new EnsureRecentChatSchema().UpOperations.Concat(new ExpressionMessages().UpOperations).Cast<SqlOperation>();
        for (var run = 0; run < 2; run++)
            foreach (var operation in repairSql) await db.Database.ExecuteSqlRawAsync(operation.Sql);
        var history = await service.GetMessagesAsync(second.Id, room.Id);
        var saved = Assert.Single(history.Messages);
        Assert.Equal(message.Id, saved.Id); Assert.Equal(message.Sequence, saved.Sequence);
        Assert.Equal("cat-happy", saved.Expression!.Id);
        Assert.Equal("/stickers/cat-happy.svg", saved.Expression.Url);
    }
}
