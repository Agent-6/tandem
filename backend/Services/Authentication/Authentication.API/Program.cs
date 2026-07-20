using Authentication.API.Identity;
using Authentication.API.Identity.Data;
using Authentication.API.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults (Aspire)
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// Configure PostgreSQL from Aspire
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("auth-db")));

// Add infrastructure services (OpenIddict, cookie auth, etc.)
builder.Services.AddInfrastructureServices();

// Configure Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddDefaultTokenProviders();

// Configure CORS for Angular SPA
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
            "https://oidcdebugger.com",
            "http://localhost:4200",
            "http://localhost:4201")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.MapOpenApi();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();
app.MapRazorPages();
app.MapControllers();

// Seed data
await SeedData(app.Services);

app.Run();

static async Task SeedData(IServiceProvider serviceProvider)
{
    await using var scope = serviceProvider.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("Checking for pending migrations...");

        var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
        var pendingList = pendingMigrations.ToList();

        logger.LogInformation("Found {Count} pending migrations", pendingList.Count);

        if (pendingList.Any())
        {
            logger.LogInformation("Applying migrations...");
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Migrations applied successfully");
        }
        else
        {
            logger.LogInformation("No pending migrations found");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while migrating the database");
        throw;
    }

    // Seed scopes
    await SeedScopesAsync(scope.ServiceProvider);

    // Seed OpenIddict applications
    await SeedOpenIddictApplicationsAsync(scope.ServiceProvider);

    // Seed default roles
    await SeedRolesAsync(scope.ServiceProvider);

    // Seed admin user
    await SeedAdminUserAsync(scope.ServiceProvider);
}

static async Task SeedScopesAsync(IServiceProvider serviceProvider)
{
    var scopeManager = serviceProvider.GetRequiredService<IOpenIddictScopeManager>();
    var existingScope = await scopeManager.FindByNameAsync("tandem.api");
    if (existingScope != null)
    {
        await scopeManager.DeleteAsync(existingScope);
    }
    await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
    {
        Name = "tandem.api",
        DisplayName = "Tandem API Access",
        Resources =
        {
            "tandem-api"
        }
    });
}

static async Task SeedOpenIddictApplicationsAsync(IServiceProvider serviceProvider)
{
    var applicationManager = serviceProvider.GetRequiredService<IOpenIddictApplicationManager>();

    // Angular SPA client (public client - no secret)
    const string spaClientId = "tandem-spa";
    var existingSpaApp = await applicationManager.FindByClientIdAsync(spaClientId);
    if (existingSpaApp != null)
    {
        await applicationManager.DeleteAsync(existingSpaApp);
    }
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = spaClientId,
            DisplayName = "Tandem Angular SPA",
            ClientType = OpenIddictConstants.ClientTypes.Public,
            RedirectUris =
            {
                new Uri("https://localhost:4200/auth/callback"),
                new Uri("http://localhost:4200/auth/callback")
            },
            PostLogoutRedirectUris =
            {
                new Uri("https://localhost:4200/logout"),
                new Uri("http://localhost:4200/logout"),
                new Uri("https://localhost:4200"),
                new Uri("http://localhost:4200")
            },
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Endpoints.Revocation,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddictConstants.Permissions.Scopes.Email,
                OpenIddictConstants.Permissions.Scopes.Profile,
                OpenIddictConstants.Permissions.Scopes.Roles,
                $"{OpenIddictConstants.Permissions.Prefixes.Scope}tandem.api",
            },
            Requirements =
            {
                OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange
            }
        };
        await applicationManager.CreateAsync(descriptor);
    }

    // Scalar API Reference client (public client - PKCE - refresh tokens)
    const string scalarClientId = "scalar-docs";
    var existingScalarApp = await applicationManager.FindByClientIdAsync(scalarClientId);
    if (existingScalarApp != null)
    {
        await applicationManager.DeleteAsync(existingScalarApp);
    }
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = scalarClientId,
            DisplayName = "Tandem Scalar API Reference",
            ClientType = OpenIddictConstants.ClientTypes.Public,
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Endpoints.Revocation,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddictConstants.Permissions.Scopes.Email,
                OpenIddictConstants.Permissions.Scopes.Profile,
                OpenIddictConstants.Permissions.Scopes.Roles,
                $"{OpenIddictConstants.Permissions.Prefixes.Scope}tandem.api",
            },
            Requirements =
            {
                OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange
            }
        };

        // Dynamically find the Scalar resource URL injected by Aspire
        var configuration = serviceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var redirectUris = new List<Uri>();

        foreach (var child in configuration.GetSection("services").GetChildren())
        {
            if (child.Key.Contains("scalar", StringComparison.OrdinalIgnoreCase))
            {
                var httpsUrl = child["https:0"];
                var httpUrl = child["http:0"];

                foreach (var url in new[] { httpsUrl, httpUrl })
                {
                    if (!string.IsNullOrEmpty(url))
                    {
                        var baseUri = new Uri(url);
                        redirectUris.Add(new Uri(baseUri, "scalar/oauth2-redirect"));
                        redirectUris.Add(new Uri(baseUri, "oauth2-redirect"));
                        redirectUris.Add(new Uri(baseUri, "scalar"));
                        redirectUris.Add(baseUri);
                    }
                }
            }
        }

        // Fallbacks in case Aspire environment variables are not loaded yet or for standalone runs
        if (redirectUris.Count == 0)
        {
            redirectUris.Add(new Uri("https://localhost:7115/scalar/oauth2-redirect"));
            redirectUris.Add(new Uri("https://localhost:7115/scalar"));
        }

        foreach (var uri in redirectUris)
        {
            descriptor.RedirectUris.Add(uri);
            descriptor.PostLogoutRedirectUris.Add(uri);
        }

        await applicationManager.CreateAsync(descriptor);
    }

    // Backend-to-backend client credentials client
    const string backendClientId = "tandem-backend";
    var existingBackendApp = await applicationManager.FindByClientIdAsync(backendClientId);
    if (existingBackendApp != null)
    {
        await applicationManager.DeleteAsync(existingBackendApp);
    }
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = backendClientId,
            ClientSecret = "BACKEND_SECRET",
            DisplayName = "Tandem Backend Service",
            ClientType = OpenIddictConstants.ClientTypes.Confidential,
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Endpoints.Revocation,
                OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                $"{OpenIddictConstants.Permissions.Prefixes.Scope}tandem.api",
            }
        };
        await applicationManager.CreateAsync(descriptor);
    }

    // Identity API client (for introspection)
    const string identityApiClientId = "tandem-identity-api";
    var existingApiClient = await applicationManager.FindByClientIdAsync(identityApiClientId);
    if (existingApiClient != null)
    {
        await applicationManager.DeleteAsync(existingApiClient);
    }
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = identityApiClientId,
            ClientSecret = "IDENTITY_SECRET",
            DisplayName = "Tandem Identity API Resource Server",
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Introspection,
            }
        };
        await applicationManager.CreateAsync(descriptor);
    }
}

static async Task SeedRolesAsync(IServiceProvider serviceProvider)
{
    var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    var roles = new[] { "Admin", "User", "Manager" };
    foreach (var roleName in roles)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            await roleManager.CreateAsync(new IdentityRole(roleName));
        }
    }
}

static async Task SeedAdminUserAsync(IServiceProvider serviceProvider)
{
    var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    const string adminEmail = "admin@tandem.local";
    const string adminPassword = "Admin@123456";

    if (await userManager.FindByEmailAsync(adminEmail) is not null)
    {
        return;
    }

    var adminUser = new ApplicationUser
    {
        UserName = adminEmail,
        Email = adminEmail,
        EmailConfirmed = true,
        DisplayName = "Admin User",
        CreatedAt = DateTime.UtcNow
    };

    var result = await userManager.CreateAsync(adminUser, adminPassword);
    if (result.Succeeded)
    {
        await userManager.AddToRoleAsync(adminUser, "Admin");
    }
}
