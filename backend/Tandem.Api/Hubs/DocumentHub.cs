using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Tandem.Api.Data;
using Tandem.Api.Data.Entities;
using Tandem.Api.Services;

namespace Tandem.Api.Hubs;

[Authorize]
public class DocumentHub : Hub
{
    private readonly DocumentSyncService _syncService;
    private readonly PresenceService _presenceService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentHub> _logger;

    // Track which document each connection is viewing
    private static readonly Dictionary<string, string> ConnectionDocumentMap = new();
    private static readonly object MapLock = new();

    public DocumentHub(
        DocumentSyncService syncService,
        PresenceService presenceService,
        IServiceScopeFactory scopeFactory,
        ILogger<DocumentHub> logger)
    {
        _syncService = syncService;
        _presenceService = presenceService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Client joins a document editing session.
    /// Validates permissions, adds to group, sends current state, broadcasts presence.
    /// </summary>
    public async Task JoinDocument(string documentId)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var displayName = Context.User?.FindFirst(ClaimTypes.Name)?.Value ?? "Anonymous";
        var avatarColor = Context.User?.FindFirst("avatar_color")?.Value ?? "#6366F1";

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Unauthorized JoinDocument attempt");
            return;
        }

        // Validate the user has access to this document
        if (!await HasDocumentAccessAsync(documentId, userId))
        {
            _logger.LogWarning("User {UserId} denied access to document {DocumentId}", userId, documentId);
            await Clients.Caller.SendAsync("AccessDenied", documentId);
            return;
        }

        // Track the connection-document mapping
        lock (MapLock)
        {
            ConnectionDocumentMap[Context.ConnectionId] = documentId;
        }

        // Add to SignalR group
        await Groups.AddToGroupAsync(Context.ConnectionId, documentId);

        // Get current document state
        var currentState = await _syncService.GetDocumentStateAsync(documentId);

        // Send current state to the joining client
        await Clients.Caller.SendAsync("DocumentState", currentState);

        // Update presence in Redis
        await _presenceService.SetUserPresenceAsync(documentId, userId, displayName, avatarColor);

        // Get and broadcast updated presence list
        var presence = await _presenceService.GetDocumentPresenceAsync(documentId);
        await Clients.Group(documentId).SendAsync("PresenceUpdate", presence);

        _logger.LogInformation("User {DisplayName} ({UserId}) joined document {DocumentId}", displayName, userId, documentId);
    }

    /// <summary>
    /// Receives a Yjs CRDT binary update from a client and broadcasts to all others in the document group.
    /// </summary>
    public async Task SendYjsUpdate(string documentId, byte[] update)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return;

        // Apply update to server-side state manager (for persistence)
        _syncService.ApplyUpdate(documentId, update, userId);

        // Broadcast to all other clients in the same document
        await Clients.OthersInGroup(documentId).SendAsync("ReceiveYjsUpdate", update);
    }

    /// <summary>
    /// Receives a full Yjs state sync from a client (used for initial state reconciliation).
    /// </summary>
    public async Task SendFullState(string documentId, byte[] fullState)
    {
        _syncService.SetFullState(documentId, fullState);
    }

    /// <summary>
    /// Receives awareness/cursor update from a client and broadcasts to others.
    /// </summary>
    public async Task SendAwareness(string documentId, byte[] awarenessUpdate)
    {
        await Clients.OthersInGroup(documentId).SendAsync("ReceiveAwareness", awarenessUpdate);
    }

    /// <summary>
    /// Client leaves a document session.
    /// </summary>
    public async Task LeaveDocument(string documentId)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return;

        lock (MapLock)
        {
            ConnectionDocumentMap.Remove(Context.ConnectionId);
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, documentId);
        await _presenceService.RemoveUserPresenceAsync(documentId, userId);

        var presence = await _presenceService.GetDocumentPresenceAsync(documentId);
        await Clients.Group(documentId).SendAsync("PresenceUpdate", presence);

        // If no more users, flush and evict the document from memory
        if (presence.Count == 0)
        {
            await _syncService.EvictDocumentAsync(documentId);
        }

        _logger.LogInformation("User {UserId} left document {DocumentId}", userId, documentId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        string? documentId;
        lock (MapLock)
        {
            ConnectionDocumentMap.TryGetValue(Context.ConnectionId, out documentId);
            ConnectionDocumentMap.Remove(Context.ConnectionId);
        }

        if (!string.IsNullOrEmpty(documentId))
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await _presenceService.RemoveUserPresenceAsync(documentId, userId);

                var presence = await _presenceService.GetDocumentPresenceAsync(documentId);
                await Clients.Group(documentId).SendAsync("PresenceUpdate", presence);

                if (presence.Count == 0)
                {
                    await _syncService.EvictDocumentAsync(documentId);
                }
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task<bool> HasDocumentAccessAsync(string documentId, string userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TandemDbContext>();
        var docGuid = Guid.Parse(documentId);

        var doc = await db.Documents.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == docGuid);

        if (doc == null) return false;

        // Owner always has access
        if (doc.OwnerId == userId) return true;

        // Check permissions
        return await db.DocumentPermissions.AnyAsync(
            p => p.DocumentId == docGuid && p.UserId == userId);
    }
}
