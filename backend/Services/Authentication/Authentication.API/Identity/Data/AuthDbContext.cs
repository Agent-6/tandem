using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace Authentication.API.Identity.Data;

public class AuthDbContext(DbContextOptions<AuthDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<OpenIddictEntityFrameworkCoreApplication<Guid>> Applications { get; set; } = default!;
    public DbSet<OpenIddictEntityFrameworkCoreAuthorization<Guid>> Authorizations { get; set; } = default!;
    public DbSet<OpenIddictEntityFrameworkCoreScope<Guid>> Scopes { get; set; } = default!;
    public DbSet<OpenIddictEntityFrameworkCoreToken<Guid>> Tokens { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.UseOpenIddict<Guid>();
    }
}
