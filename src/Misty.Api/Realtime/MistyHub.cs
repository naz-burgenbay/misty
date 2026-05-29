using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Misty.Application.Presence;
using Misty.Infrastructure.Persistence;

namespace Misty.Api.Realtime;

[Authorize]
public sealed class MistyHub : Hub
{
    public const string PresenceGroup = "presence";

    private readonly ApplicationDbContext _db;
    private readonly IPresenceTracker _presence;

    public MistyHub(ApplicationDbContext db, IPresenceTracker presence)
    {
        _db = db;
        _presence = presence;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Guid.Parse(Context.User!.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

        // Per-user group used to fan out events that target a specific user across all their tabs/devices (membership, role, and moderation events).
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");

        // Single broadcast group used to fan out PresenceChanged. Every authenticated connection joins; the backplane handles cross-instance delivery.
        await Groups.AddToGroupAsync(Context.ConnectionId, PresenceGroup);

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

        var becameOnline = await _presence.TrackConnectionAsync(userId, Context.ConnectionId);
        if (becameOnline)
            await Clients.Group(PresenceGroup).SendAsync("PresenceChanged",
                new { UserId = userId, IsOnline = true, OccurredAt = DateTime.UtcNow });

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var sub = Context.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (Guid.TryParse(sub, out var userId))
        {
            var becameOffline = await _presence.UntrackConnectionAsync(userId, Context.ConnectionId);
            if (becameOffline)
                await Clients.Group(PresenceGroup).SendAsync("PresenceChanged",
                    new { UserId = userId, IsOnline = false, OccurredAt = DateTime.UtcNow });
        }

        await base.OnDisconnectedAsync(exception);
    }
}
