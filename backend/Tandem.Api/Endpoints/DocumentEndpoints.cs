using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tandem.Api.Data;
using Tandem.Api.Data.Entities;

namespace Tandem.Api.Endpoints;

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/documents")
            .WithTags("Documents")
            .RequireAuthorization();

        // List documents accessible to current user
        group.MapGet("/", async (HttpContext context, TandemDbContext db) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)!.Value;

            var ownedDocs = await db.Documents
                .Where(d => d.OwnerId == userId)
                .Select(d => new DocumentListItem(
                    d.Id, d.Title, d.OwnerId, d.Owner.DisplayName, d.CreatedAt, d.UpdatedAt,
                    d.Permissions.Select(p => new CollaboratorInfo(p.UserId, p.User.DisplayName, p.User.AvatarColor, p.Role.ToString())).ToList(),
                    true))
                .ToListAsync();

            var sharedDocs = await db.DocumentPermissions
                .Where(p => p.UserId == userId)
                .Select(p => new DocumentListItem(
                    p.Document.Id, p.Document.Title, p.Document.OwnerId, p.Document.Owner.DisplayName,
                    p.Document.CreatedAt, p.Document.UpdatedAt,
                    p.Document.Permissions.Select(pp => new CollaboratorInfo(pp.UserId, pp.User.DisplayName, pp.User.AvatarColor, pp.Role.ToString())).ToList(),
                    false))
                .ToListAsync();

            return Results.Ok(new { Owned = ownedDocs, Shared = sharedDocs });
        });

        // Create new document
        group.MapPost("/", async (CreateDocumentRequest request, HttpContext context, TandemDbContext db) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)!.Value;

            var doc = new Document
            {
                Id = Guid.NewGuid(),
                Title = request.Title ?? "Untitled Document",
                OwnerId = userId,
                YjsState = [],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.Documents.Add(doc);
            await db.SaveChangesAsync();

            return Results.Created($"/api/documents/{doc.Id}", new DocumentDetail(
                doc.Id, doc.Title, doc.OwnerId, "", doc.CreatedAt, doc.UpdatedAt, true, "Owner", []));
        });

        // Get document detail
        group.MapGet("/{id:guid}", async (Guid id, HttpContext context, TandemDbContext db) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)!.Value;

            var doc = await db.Documents
                .Include(d => d.Owner)
                .Include(d => d.Permissions)
                    .ThenInclude(p => p.User)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (doc == null) return Results.NotFound();

            var isOwner = doc.OwnerId == userId;
            var permission = doc.Permissions.FirstOrDefault(p => p.UserId == userId);

            if (!isOwner && permission == null)
                return Results.Forbid();

            var role = isOwner ? "Owner" : permission!.Role.ToString();
            var collaborators = doc.Permissions
                .Select(p => new CollaboratorInfo(p.UserId, p.User.DisplayName, p.User.AvatarColor, p.Role.ToString()))
                .ToList();

            return Results.Ok(new DocumentDetail(
                doc.Id, doc.Title, doc.OwnerId, doc.Owner.DisplayName,
                doc.CreatedAt, doc.UpdatedAt, isOwner, role, collaborators));
        });

        // Update document title
        group.MapPut("/{id:guid}", async (Guid id, UpdateDocumentRequest request, HttpContext context, TandemDbContext db) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)!.Value;

            var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id);
            if (doc == null) return Results.NotFound();

            if (doc.OwnerId != userId)
            {
                var hasEditPermission = await db.DocumentPermissions
                    .AnyAsync(p => p.DocumentId == id && p.UserId == userId && p.Role == DocumentRole.Editor);
                if (!hasEditPermission) return Results.Forbid();
            }

            doc.Title = request.Title;
            doc.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(new { doc.Id, doc.Title, doc.UpdatedAt });
        });

        // Soft-delete document
        group.MapDelete("/{id:guid}", async (Guid id, HttpContext context, TandemDbContext db) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)!.Value;

            var doc = await db.Documents.IgnoreQueryFilters()
                .FirstOrDefaultAsync(d => d.Id == id);
            if (doc == null) return Results.NotFound();
            if (doc.OwnerId != userId) return Results.Forbid();

            doc.IsDeleted = true;
            doc.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.NoContent();
        });

        // List version history
        group.MapGet("/{id:guid}/versions", async (Guid id, HttpContext context, TandemDbContext db) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)!.Value;

            if (!await HasAccess(db, id, userId))
                return Results.Forbid();

            var versions = await db.DocumentVersions
                .Where(v => v.DocumentId == id)
                .OrderByDescending(v => v.VersionNumber)
                .Select(v => new VersionListItem(v.Id, v.VersionNumber, v.Label, v.CreatedByUserId, v.CreatedBy.DisplayName, v.CreatedAt))
                .ToListAsync();

            return Results.Ok(versions);
        });

        // Get a specific version's Yjs snapshot
        group.MapGet("/{id:guid}/versions/{versionId:guid}", async (Guid id, Guid versionId, HttpContext context, TandemDbContext db) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)!.Value;

            if (!await HasAccess(db, id, userId))
                return Results.Forbid();

            var version = await db.DocumentVersions
                .FirstOrDefaultAsync(v => v.Id == versionId && v.DocumentId == id);

            if (version == null) return Results.NotFound();

            return Results.Ok(new { version.Id, version.VersionNumber, version.Label, version.YjsSnapshot, version.CreatedAt });
        });
    }

    private static async Task<bool> HasAccess(TandemDbContext db, Guid documentId, string userId)
    {
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId);
        if (doc == null) return false;
        if (doc.OwnerId == userId) return true;
        return await db.DocumentPermissions.AnyAsync(p => p.DocumentId == documentId && p.UserId == userId);
    }
}

public record CreateDocumentRequest(string? Title);
public record UpdateDocumentRequest(string Title);
public record DocumentListItem(
    Guid Id, string Title, string OwnerId, string OwnerName,
    DateTime CreatedAt, DateTime UpdatedAt,
    List<CollaboratorInfo> Collaborators, bool IsOwner);
public record DocumentDetail(
    Guid Id, string Title, string OwnerId, string OwnerName,
    DateTime CreatedAt, DateTime UpdatedAt, bool IsOwner, string Role,
    List<CollaboratorInfo> Collaborators);
public record CollaboratorInfo(string UserId, string DisplayName, string AvatarColor, string Role);
public record VersionListItem(Guid Id, int VersionNumber, string? Label, string CreatedByUserId, string CreatedByName, DateTime CreatedAt);
