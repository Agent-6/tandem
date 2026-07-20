using Microsoft.AspNetCore.Identity;

namespace Tandem.Api.Data.Entities;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Hex color code used for cursor and avatar display (e.g., "#4F46E5").
    /// </summary>
    public string AvatarColor { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<Document> OwnedDocuments { get; set; } = [];
    public ICollection<DocumentPermission> DocumentPermissions { get; set; } = [];
    public ICollection<DocumentVersion> CreatedVersions { get; set; } = [];
}
