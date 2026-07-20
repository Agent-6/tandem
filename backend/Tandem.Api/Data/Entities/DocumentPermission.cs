namespace Tandem.Api.Data.Entities;

public class DocumentPermission
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DocumentRole Role { get; set; } = DocumentRole.Viewer;

    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Document Document { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
}

public enum DocumentRole
{
    Viewer = 0,
    Editor = 1
}
