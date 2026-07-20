namespace Tandem.Api.Data.Entities;

public class Document
{
    public Guid Id { get; set; }

    public string Title { get; set; } = "Untitled Document";

    /// <summary>
    /// The full Yjs binary state vector, persisted as a debounced snapshot.
    /// </summary>
    public byte[] YjsState { get; set; } = [];

    public string OwnerId { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ApplicationUser Owner { get; set; } = null!;
    public ICollection<DocumentVersion> Versions { get; set; } = [];
    public ICollection<DocumentPermission> Permissions { get; set; } = [];
}
