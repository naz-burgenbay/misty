using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Misty.Web;
using Misty.Web.Services.Auth;
using Misty.Web.Services.Communication;
using Misty.Web.Services.Realtime;
using Misty.Web.Services.Messaging;
using Misty.Web.Services.Presence;
using Misty.Web.Services.Permissions;
using Misty.Web.Services.Users;
using Misty.Web.Services.Ux;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["Api:BaseUrl"]
    ?? throw new InvalidOperationException("Api:BaseUrl is not configured.");

builder.Services.AddScoped<ILocalStorage, JsLocalStorage>();
builder.Services.AddScoped<HttpAuthService>(sp => new HttpAuthService(
    new HttpClient { BaseAddress = new Uri(apiBaseUrl) },
    sp.GetRequiredService<ILocalStorage>(),
    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<HttpAuthService>>()));
builder.Services.AddScoped<IAuthService>(sp => sp.GetRequiredService<HttpAuthService>());

builder.Services.AddScoped(sp =>
{
    var handler = new AuthorizationMessageHandler(sp.GetRequiredService<IAuthService>())
    {
        InnerHandler = new HttpClientHandler(),
    };
    return new HttpClient(handler) { BaseAddress = new Uri(apiBaseUrl) };
});

builder.Services.AddScoped<ISignalRClient>(sp => new HubSignalRClient(
    sp.GetRequiredService<IAuthService>(),
    new Uri(new Uri(apiBaseUrl), "hubs/realtime").ToString(),
    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<HubSignalRClient>>()));
builder.Services.AddScoped<IUserDirectory, HttpUserDirectory>();
builder.Services.AddScoped<IUserProfileService, HttpUserProfileService>();
builder.Services.AddScoped<IChannelService, HttpChannelService>();
builder.Services.AddScoped<IDirectMessageService, HttpDirectMessageService>();
builder.Services.AddScoped<IMessageStore, HttpMessageStore>();
builder.Services.AddScoped<IPresenceService, HttpPresenceService>();
builder.Services.AddScoped<IPermissionsCache, HttpPermissionsCache>();
builder.Services.AddScoped<IModerationService, HttpModerationService>();
builder.Services.AddScoped<IFriendService, HttpFriendService>();
builder.Services.AddScoped<IUserBlockService, HttpUserBlockService>();
builder.Services.AddScoped<IChannelRolesService, HttpChannelRolesService>();
builder.Services.AddScoped<IChannelMembersService, HttpChannelMembersService>();
builder.Services.AddScoped<IInboxService, HttpInboxService>();
builder.Services.AddScoped<IReportService, HttpReportService>();
builder.Services.AddScoped<IToastService, StubToastService>();
builder.Services.AddScoped<IModalService, StubModalService>();

var host = builder.Build();

var auth = host.Services.GetRequiredService<IAuthService>();
await auth.InitializeAsync();

var hub = host.Services.GetRequiredService<ISignalRClient>();
var channels = host.Services.GetRequiredService<IChannelService>();
var directMessages = host.Services.GetRequiredService<IDirectMessageService>();
var friends = host.Services.GetRequiredService<IFriendService>();
var userDir = host.Services.GetRequiredService<IUserDirectory>();

void SeedMe()
{
    if (auth.CurrentUser is { } me)
        userDir.Seed(new UserSummary(me.Id, me.DisplayName, me.Username));
}

if (auth.IsAuthenticated)
{
    SeedMe();
    _ = hub.StartAsync();
    _ = channels.RefreshAsync();
    _ = directMessages.RefreshAsync();
    _ = friends.RefreshAsync();
}

auth.AuthStateChanged += () =>
{
    if (auth.IsAuthenticated)
    {
        SeedMe();
        _ = hub.StartAsync();
        _ = channels.RefreshAsync();
        _ = directMessages.RefreshAsync();
        _ = friends.RefreshAsync();
    }
    else _ = hub.StopAsync();
};

await host.RunAsync();
