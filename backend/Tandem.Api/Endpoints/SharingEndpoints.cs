using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tandem.Api.Data;
using Tandem.Api.Data.Entities;

namespace Tandem.Api.Endpoints;

public static class SharingEndpoints
{
    public static void MapSharingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/documents/{documentId:guid}/share")
            .WithTags("Sharing")
            .RequireAuthorization();

        // List shares for a document
        group.MapGet("/", async (Guid documentId, HttpContext context, TandemDbContext db) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)!.Value;

            if (!await IsOwner(db, documentId, userId))
                return Results.Forbid();

            var shares = await db.DocumentPermissions
                .Where(p => p.DocumentId == documentId)
                .Select(p => new ShareInfo(p.Id, p.UserId, p.User.DisplayName, p.User.Email!, p.User.AvatarColor, p.Role.ToString(), p.GrantedAt))
                .ToListAsync();

            return Results.Ok(shares);
        });

        // Share document with a user
        group.MapPost("/", async (
            Guid documentId,
            ShareRequest request,
            HttpContext context,
            TandemDbContext db,
            UserManager<ApplicationUser> userManager) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)!.Value;

            if (!await IsOwner(db, documentId, userId))
                return Results.Forbid();

            var targetUser = await userManager.FindByEmailAsync(request.Email);
            if (targetUser == null)
                return Results.NotFound(new { Message = $"No user found with email {request.Email}" });

            if (targetUser.Id == userId)
                return Results.BadRequest(new { Message = "Cannot share a document with yourself" });

            // Check if already shared
            var existing = await db.DocumentPermissions
                .FirstOrDefaultAsync(p => p.DocumentId == documentId && p.UserId == targetUser.Id);

            if (existing != null)
            {
                existing.Role = Enum.Parse<DocumentRole>(request.Role, true);
                await db.SaveChangesAsync();
                return Results.Ok(new ShareInfo(existing.Id, targetUser.Id, targetUser.DisplayName, targetUser.Email!, targetUser.AvatarColor, existing.Role.ToString(), existing.GrantedAt));
            }

            var permission = new DocumentPermission
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                UserId = targetUser.Id,
                Role = Enum.Parse<DocumentRole>(request.Role, true),
                GrantedAt = DateTime.UtcNow
            };

            db.DocumentPermissions.Add(permission);
            await db.SaveChangesAsync();

            return Results.Created($"/api/documents/{documentId}/share",
                new ShareInfo(permission.Id, targetUser.Id, targetUser.DisplayName, targetUser.Email!, targetUser.AvatarColor, permission.Role.ToString(), permission.GrantedAt));
        });

        // Update share role
        group.MapPut("/{shareUserId}", async (
            Guid documentId,
            string shareUserId,
            UpdateShareRequest request,
            HttpContext context,
            TandemDbContext db) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)!.Value;

            if (!await IsOwner(db, documentId, userId))
                return Results.Forbid();

            var permission = await db.DocumentPermissions
                .FirstOrDefaultAsync(p => p.DocumentId == documentId && p.UserId == shareUserId);

            if (permission == null) return Results.NotFound();

            permission.Role = Enum.Parse<DocumentRole>(request.Role, true);
            await db.SaveChangesAsync();

            return Results.Ok(new { permission.Id, permission.Role });
        });

        // Revoke access
        group.MapDelete("/{shareUserId}", async (
            Guid documentId,
            string shareUserId,
            HttpContext context,
            TandemDbContext db) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)!.Value;

            if (!await IsOwner(db, documentId, userId))
                return Results.Forbid();

            var permission = await db.DocumentPermissions
                .FirstOrDefaultAsync(p => p.DocumentId == documentId && p.UserId == shareUserId);

            if (permission == null) return Results.NotFound();

            db.DocumentPermissions.Remove(permission);
            await db.SaveChangesAsync();

            return Results.NoContent();
        });
    }

    private static async Task<bool> IsOwner(TandemDbContext db, Guid documentId, string userId)
    {
        return await db.Documents.AnyAsync(d => d.Id == documentId && d.OwnerId == userId);
    }
}

public record ShareRequest(string Email, string Role);
public record UpdateShareRequest(string Role);
public record ShareInfo(Guid Id, string UserId, string DisplayName, string Email, string AvatarColor, string Role, DateTime GrantedAt);
