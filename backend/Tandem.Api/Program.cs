using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Tandem.Api.Data;
using Tandem.Api.Data.Entities;
using Tandem.Api.Endpoints;
using Tandem.Api.Hubs;
using Tandem.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults (telemetry, health checks, service discovery)
builder.AddServiceDefaults();

// Database: PostgreSQL via Aspire integration
builder.AddNpgsqlDbContext<TandemDbContext>("tandemdb");

// Redis via Aspire integration (used for SignalR backplane + presence)
builder.AddRedisClient("redis");

// ASP.NET Core Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 6;
        options.Password.RequireDigit = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<TandemDbContext>()
    .AddDefaultTokenProviders();

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"] ?? "TandemSuperSecretKeyThatIsLongEnoughForHS256!!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "Tandem";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "TandemUsers";

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero
        };

        // Allow SignalR to receive JWT from query string
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// SignalR with Redis backplane
builder.Services.AddSignalR()
    .AddStackExchangeRedis(builder.Configuration.GetConnectionString("redis") ?? "localhost:6379", options =>
    {
        options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("Tandem");
    });

// Application services
builder.Services.AddSingleton<DocumentSyncService>();
builder.Services.AddScoped<PresenceService>();
builder.Services.AddScoped<TokenService>();

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .SetIsOriginAllowed(_ => true) // Dev: allow all origins; tighten in production
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// OpenAPI
builder.Services.AddOpenApi();

var app = builder.Build();

// Apply migrations on startup (dev only)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TandemDbContext>();
    await db.Database.EnsureCreatedAsync();

    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Map endpoints
app.MapAuthEndpoints();
app.MapDocumentEndpoints();
app.MapSharingEndpoints();

// Map SignalR hub
app.MapHub<DocumentHub>("/hubs/document");

// Aspire default endpoints (health checks)
app.MapDefaultEndpoints();

app.Run();
