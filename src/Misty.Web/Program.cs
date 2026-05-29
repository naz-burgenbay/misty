using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Misty.Web;
using Misty.Web.Services.Auth;
using Misty.Web.Services.Realtime;
using Misty.Web.Services.Messaging;
using Misty.Web.Services.Presence;
using Misty.Web.Services.Permissions;
using Misty.Web.Services.Ux;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Client-side service skeletons. All are Scoped, which in Blazor WASM behaves as singleton-within-a-session, the right lifetime for per-tab state (auth, hub, observable stores) that must reset on sign-out by replacing the root scope, not by manual cleanup inside the service.
builder.Services.AddScoped<IAuthService, StubAuthService>();
builder.Services.AddScoped<ISignalRClient, StubSignalRClient>();
builder.Services.AddScoped<IMessageStore, StubMessageStore>();
builder.Services.AddScoped<IPresenceService, StubPresenceService>();
builder.Services.AddScoped<IPermissionsCache, StubPermissionsCache>();
builder.Services.AddScoped<IToastService, StubToastService>();
builder.Services.AddScoped<IModalService, StubModalService>();

await builder.Build().RunAsync();
