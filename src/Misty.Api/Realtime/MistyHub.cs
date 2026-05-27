using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Misty.Infrastructure.Persistence;

namespace Misty.Api.Realtime;

[Authorize]
public sealed class MistyHub : Hub
{
    private readonly ApplicationDbContext _db;

    public MistyHub(ApplicationDbContext db) => _db = db;

    public override async Task OnConnectedAsync()
    {
        var userId = Guid.Parse(Context.User!.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

        var channelIds = await _db.Memberships
            .Where(m => m.UserId == userId)
            .Select(m => m.ChannelId)
            .ToListAsync();

        foreach (var channelId in channelIds)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"channel:{channelId}");

        var conversationIds = await _db.Conversations
            .Where(c => c.UserAId == userId || c.UserBId == userId)
            .Select(c => c.Id)
            .ToListAsync();

        foreach (var conversationId in conversationIds)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"conversation:{conversationId}");

        await base.OnConnectedAsync();
    }
}
