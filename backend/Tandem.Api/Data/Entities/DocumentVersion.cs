namespace Tandem.Api.Data.Entities;

public class DocumentVersion
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    /// <summary>
    /// Frozen Yjs binary snapshot at this point in time.
    /// </summary>
    public byte[] YjsSnapshot { get; set; } = [];

    public int VersionNumber { get; set; }

    public string? Label { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Document Document { get; set; } = null!;
    public ApplicationUser CreatedBy { get; set; } = null!;
}
