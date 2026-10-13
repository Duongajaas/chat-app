using System.Security.Claims;
using ChatApp.Calls;
using ChatApp.Common;
using ChatApp.Models;
using ChatApp.Services;
using ChatApp.Options;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using Xunit;

namespace ChatApp.Tests;

public class CallContractTests
{
    [Fact]
    public void Token_keeps_session_family_across_refresh_and_call_actor_requires_it()
    {
        var user = new User { Username = "caller", FullName = "Caller" };
        var session = Guid.NewGuid();
        var tokens = new TokenService(Microsoft.Extensions.Options.Options.Create(new JwtOptions { SecretKey = new string('s', 48), Issuer = "test", Audience = "test" }));
        var token = new JwtSecurityTokenHandler().ReadJwtToken(tokens.GenerateAccessToken(user, session).Token);
        Assert.Equal(session.ToString(), token.Claims.Single(c => c.Type == "sid").Value);
        Assert.Throws<AppException>(() => CallActor.From(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())]))));
    }
    [Theory]
    [InlineData(CallStatus.Ringing, true)] [InlineData(CallStatus.Accepted, true)]
    [InlineData(CallStatus.Ended, false)] [InlineData(CallStatus.Failed, false)] [InlineData(CallStatus.Busy, false)]
    [InlineData(CallStatus.Missed, false)] [InlineData(CallStatus.Canceled, false)] [InlineData(CallStatus.Rejected, false)]
    public void Only_nonterminal_states_reserve_a_slot(CallStatus status, bool active) => Assert.Equal(active, CallService.IsLive(status));
    [Fact]
    public void Call_log_response_preserves_type_and_recall_redacts_content()
    {
        var log = new Message { Type = MessageType.CallLog, Content = "{\"status\":\"Ended\"}" };
        Assert.Equal("CallLog", MessageService.ToResponse(log).Type);
        log.DeletedAt = DateTime.UtcNow;
        Assert.DoesNotContain("Ended", MessageService.ToResponse(log).Content);
    }
}

