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

// Auth services. HttpAuthService receives a bare HttpClient so login and refresh requests don't recurse through AuthorizationMessageHandler (which depends on IAuthService).
builder.Services.AddScoped<ILocalStorage, JsLocalStorage>();
builder.Services.AddScoped<HttpAuthService>(sp => new HttpAuthService(
    new HttpClient { BaseAddress = new Uri(apiBaseUrl) },
    sp.GetRequiredService<ILocalStorage>(),
    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<HttpAuthService>>()));
builder.Services.AddScoped<IAuthService>(sp => sp.GetRequiredService<HttpAuthService>());

// The shared API HttpClient: every other service uses this. It runs requests through AuthorizationMessageHandler, which attaches the bearer token and retries once after a forced refresh on 401.
builder.Services.AddScoped(sp =>
{
    var handler = new AuthorizationMessageHandler(sp.GetRequiredService<IAuthService>())
    {
        InnerHandler = new HttpClientHandler(),
    };
    return new HttpClient(handler) { BaseAddress = new Uri(apiBaseUrl) };
});

// Client-side service skeletons. All are Scoped, which in Blazor WASM behaves as singleton-within-a-session, the right lifetime for per-tab state (auth, hub, observable stores) that must reset on sign-out by replacing the root scope, not by manual cleanup inside the service.
builder.Services.AddScoped<ISignalRClient>(sp => new HubSignalRClient(
    sp.GetRequiredService<IAuthService>(),
    new Uri(new Uri(apiBaseUrl), "hubs/realtime").ToString(),
    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<HubSignalRClient>>()));
builder.Services.AddScoped<IUserDirectory, HttpUserDirectory>();
builder.Services.AddScoped<IChannelService, HttpChannelService>();
builder.Services.AddScoped<IDirectMessageService, HttpDirectMessageService>();
builder.Services.AddScoped<IMessageStore, HttpMessageStore>();
builder.Services.AddScoped<IPresenceService, HttpPresenceService>();
builder.Services.AddScoped<IPermissionsCache, StubPermissionsCache>();
builder.Services.AddScoped<IModerationService, HttpModerationService>();
builder.Services.AddScoped<IToastService, StubToastService>();
builder.Services.AddScoped<IModalService, StubModalService>();

var host = builder.Build();

// Restore session from the refresh token in localStorage before the root component renders, so the initial route resolves with the right auth state.
var auth = host.Services.GetRequiredService<IAuthService>();
await auth.InitializeAsync();

// Drive the SignalR connection from auth state. The hub uses an access-token provider that pulls (and refreshes) from IAuthService on every (re)connect, so the connection survives token expiry across long offline windows.
var hub = host.Services.GetRequiredService<ISignalRClient>();
var channels = host.Services.GetRequiredService<IChannelService>();
var directMessages = host.Services.GetRequiredService<IDirectMessageService>();
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
}

auth.AuthStateChanged += () =>
{
    if (auth.IsAuthenticated)
    {
        SeedMe();
        _ = hub.StartAsync();
        _ = channels.RefreshAsync();
        _ = directMessages.RefreshAsync();
    }
    else _ = hub.StopAsync();
};

await host.RunAsync();
