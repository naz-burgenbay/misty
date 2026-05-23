using Azure.Storage.Blobs;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Misty.Api.Common;
using Misty.Application.Common.Behaviors;
using Misty.Application.Communication;
using Misty.Application.Communication.Contracts;
using Misty.Application.Users;
using Misty.Domain.Users;
using Misty.Infrastructure.Communication;
using Misty.Infrastructure.Persistence;
using Misty.Infrastructure.Users;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using System.Text;

try
{
    Log.Logger = new LoggerConfiguration()
        .WriteTo.Console()
        .CreateBootstrapLogger();
}
catch (InvalidOperationException)
{
    // Logger already frozen (e.g. multiple WebApplicationFactory instances in tests). Safe to ignore.
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

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

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
    });
builder.Services.AddAuthorization();

var connectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// AddDbContextFactory is registered so that background services like outbox relay or cache invalidation worker can create their own DbContext instances without depending on the scoped lifetime of the main ApplicationDbContext
builder.Services.AddDbContextFactory<ApplicationDbContext>(
    options => options.UseSqlServer(connectionString),
    ServiceLifetime.Scoped);

var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Connection string 'Redis' is not configured.");

var serviceBusConnectionString = builder.Configuration.GetConnectionString("ServiceBus")
    ?? throw new InvalidOperationException("Connection string 'ServiceBus' is not configured.");

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("sql")
    .AddRedis(redisConnectionString, "redis")
    .AddAzureServiceBusTopic(serviceBusConnectionString, "message-events", name: "service-bus");

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Misty.Api"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (builder.Environment.IsDevelopment())
            tracing.AddConsoleExporter();
    });

var blobConnectionString = builder.Configuration.GetConnectionString("BlobStorage")
    ?? throw new InvalidOperationException("Connection string 'BlobStorage' is not configured.");
builder.Services.AddSingleton(new BlobServiceClient(blobConnectionString));
builder.Services.AddScoped<IAvatarService, AzureBlobAvatarService>();

builder.Services.AddScoped<IPermissionService, StubPermissionService>();
builder.Services.AddScoped<IUserQueryService, StubUserQueryService>();
builder.Services.AddScoped<IChannelQueryService, ChannelQueryService>();
builder.Services.AddScoped<IUserBlockService, StubUserBlockService>();

builder.Services.AddScoped<IChannelRepository, ChannelRepository>();
builder.Services.AddScoped<IMembershipRepository, MembershipRepository>();
builder.Services.AddScoped<IChannelRoleRepository, ChannelRoleRepository>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
}

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
}

// Expose Program to the test project for WebApplicationFactory<Program>
public partial class Program { }

