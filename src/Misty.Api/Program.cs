using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Misty.Api.Common;
using Misty.Api.Realtime;
using Misty.Application.Common.Behaviors;
using Misty.Application.Communication;
using Misty.Application.Communication.Contracts;
using Misty.Application.Messaging;
using Misty.Application.Users;
using Misty.Domain.Users;
using Misty.Infrastructure.Communication;
using Misty.Infrastructure.Messaging;
using Misty.Infrastructure.Persistence;
using Misty.Infrastructure.Users;
using Azure.Monitor.OpenTelemetry.Exporter;
using OpenAI;
using OpenAI.Chat;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using StackExchange.Redis;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

try
{
    Log.Logger = new LoggerConfiguration()
        .WriteTo.Console()
        .CreateBootstrapLogger();
}
catch (InvalidOperationException)
{
}

try
{
    Log.Information("Starting Misty.Api...");
    await Run(args);
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Misty.Api terminated unexpectedly.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static async Task Run(string[] args)
{

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) =>
{
    lc.ReadFrom.Configuration(ctx.Configuration)
      .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
      .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", Serilog.Events.LogEventLevel.Warning)
      .WriteTo.Console();

    var appInsightsConnStr = ctx.Configuration["ApplicationInsights:ConnectionString"];
    if (!string.IsNullOrWhiteSpace(appInsightsConnStr))
    {
        lc.WriteTo.ApplicationInsights(
            appInsightsConnStr,
            TelemetryConverter.Traces);
    }
});

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

const string WebClientCorsPolicy = "WebClient";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5192", "https://localhost:7103" };
builder.Services.AddCors(options =>
{
    options.AddPolicy(WebClientCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(RegisterUserCommand).Assembly));
builder.Services.AddValidatorsFromAssemblyContaining<RegisterUserValidator>();
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IUserRepository, UserRepository>();

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("JWT signing key 'Jwt:Key' is not configured.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "Misty.Api";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "Misty.Web";

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) &&
                    ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddSingleton<IUserIdProvider, SubClaimUserIdProvider>();

var connectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddDbContextFactory<ApplicationDbContext>(
    options => options.UseSqlServer(connectionString),
    ServiceLifetime.Scoped);

var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Connection string 'Redis' is not configured.");

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var opts = ConfigurationOptions.Parse(redisConnectionString);
    opts.AllowAdmin = true;
    return ConnectionMultiplexer.Connect(opts);
});

builder.Services
    .AddSignalR()
    .AddStackExchangeRedis(redisConnectionString);

var serviceBusConnectionString = builder.Configuration.GetConnectionString("ServiceBus")
    ?? throw new InvalidOperationException("Connection string 'ServiceBus' is not configured.");

builder.Services.AddSingleton(_ => new ServiceBusClient(serviceBusConnectionString));

var openAiApiKey = builder.Configuration["OpenAI:ApiKey"];
if (!string.IsNullOrWhiteSpace(openAiApiKey))
{
    builder.Services.AddSingleton(new OpenAIClient(openAiApiKey));
    builder.Services.AddSingleton(sp => sp.GetRequiredService<OpenAIClient>().GetChatClient("gpt-4o-mini"));
}

builder.Services.AddHostedService<CacheInvalidationWorker>();
builder.Services.AddHostedService<OutboxRelayWorker>();
builder.Services.AddHostedService<RealtimeDeliveryWorker>();
builder.Services.AddHostedService<PermissionEventsBroadcastWorker>();
builder.Services.AddHostedService<AIResponseWorker>();
builder.Services.AddHostedService<InboxWorker>();

var blobConnectionString = builder.Configuration.GetConnectionString("BlobStorage")
    ?? throw new InvalidOperationException("Connection string 'BlobStorage' is not configured.");
var blobOptions = new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04);

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("sql")
    .AddRedis(redisConnectionString, "redis")
    .AddCheck("service-bus", new ServiceBusSenderHealthCheck(serviceBusConnectionString, "message-events"))
    .AddCheck("blob-storage", new BlobStorageHealthCheck(blobConnectionString, blobOptions, "avatars"));

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Misty.Api"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddHttpClientInstrumentation();

        var appInsightsConnStr = builder.Configuration["ApplicationInsights:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(appInsightsConnStr))
            tracing.AddAzureMonitorTraceExporter(o => o.ConnectionString = appInsightsConnStr);
    });

builder.Services.AddSingleton(new BlobServiceClient(blobConnectionString, blobOptions));
builder.Services.AddScoped<IAvatarService, AzureBlobAvatarService>();
builder.Services.AddScoped<IChannelIconService, AzureBlobChannelIconService>();
builder.Services.AddScoped<IOutboxWriter, OutboxWriter>();

builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<IPermissionService, CachedPermissionService>();
builder.Services.AddScoped<IUserQueryService, StubUserQueryService>();
builder.Services.AddScoped<IChannelQueryService, ChannelQueryService>();
builder.Services.AddScoped<IUserBlockService, UserBlockService>();

builder.Services.AddScoped<IChannelRepository, ChannelRepository>();
builder.Services.AddScoped<IMembershipRepository, MembershipRepository>();
builder.Services.AddScoped<IChannelRoleRepository, ChannelRoleRepository>();
builder.Services.AddScoped<IModerationRepository, ModerationRepository>();
builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
builder.Services.AddScoped<IMessageRepository, MessageRepository>();
builder.Services.AddScoped<IReactionRepository, ReactionRepository>();
builder.Services.AddScoped<IAttachmentRepository, AttachmentRepository>();
builder.Services.AddScoped<IFriendRequestRepository, FriendRequestRepository>();
builder.Services.AddScoped<IFriendshipRepository, FriendshipRepository>();
builder.Services.AddScoped<IChannelInviteRepository, ChannelInviteRepository>();
builder.Services.AddScoped<IInboxItemRepository, InboxItemRepository>();
builder.Services.AddScoped<IAttachmentStorage, AzureBlobAttachmentStorage>();
builder.Services.AddSingleton<Misty.Application.Presence.IPresenceTracker, Misty.Infrastructure.Presence.RedisPresenceTracker>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors(WebClientCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    var aiBotId = AIResponseWorker.AiUserId;
    if (!await db.Set<User>().AnyAsync(u => u.Id == aiBotId))
    {
        var botUser = User.Create(aiBotId, "misty-bot", "misty-bot@internal.misty", "Misty Bot");
        botUser.SetPasswordHash(string.Empty);
        db.Set<User>().Add(botUser);
        await db.SaveChangesAsync();
    }
}

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            entries = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    duration = e.Value.Duration.TotalMilliseconds,
                    error = e.Value.Exception?.Message,
                    exceptionType = e.Value.Exception?.GetType().FullName,
                    data = e.Value.Data
                })
        });
        await context.Response.WriteAsync(payload);
    }
});
app.MapControllers();
app.MapHub<MistyHub>("/hubs/realtime");

app.Run();
}

public partial class Program { }

