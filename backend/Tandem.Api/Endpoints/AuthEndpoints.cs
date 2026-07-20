using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Tandem.Api.Data.Entities;
using Tandem.Api.Services;

namespace Tandem.Api.Endpoints;

public static class AuthEndpoints
{
    private static readonly string[] AvatarColors =
    [
        "#6366F1", "#EC4899", "#14B8A6", "#F59E0B", "#8B5CF6",
        "#EF4444", "#06B6D4", "#84CC16", "#F97316", "#A855F7"
    ];

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (
            RegisterRequest request,
            UserManager<ApplicationUser> userManager,
            TokenService tokenService) =>
        {
            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                DisplayName = request.DisplayName,
                AvatarColor = AvatarColors[Random.Shared.Next(AvatarColors.Length)],
                CreatedAt = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                return Results.BadRequest(new { Errors = result.Errors.Select(e => e.Description) });
            }

            var token = tokenService.GenerateAccessToken(user);
            return Results.Ok(new AuthResponse(token, user.Id, user.DisplayName, user.Email!, user.AvatarColor));
        });

        group.MapPost("/login", async (
            LoginRequest request,
            UserManager<ApplicationUser> userManager,
            TokenService tokenService) =>
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user == null || !await userManager.CheckPasswordAsync(user, request.Password))
            {
                return Results.Unauthorized();
            }

            var token = tokenService.GenerateAccessToken(user);
            return Results.Ok(new AuthResponse(token, user.Id, user.DisplayName, user.Email!, user.AvatarColor));
        });

        group.MapGet("/me", async (
            HttpContext context,
            UserManager<ApplicationUser> userManager) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
                return Results.NotFound();

            return Results.Ok(new UserProfile(user.Id, user.DisplayName, user.Email!, user.AvatarColor));
        }).RequireAuthorization();
    }
}

public record RegisterRequest(string Email, string Password, string DisplayName);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string Token, string UserId, string DisplayName, string Email, string AvatarColor);
public record UserProfile(string UserId, string DisplayName, string Email, string AvatarColor);
