using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Tandem.Api.Data;

namespace Tandem.Api.Services;

/// <summary>
/// Manages in-memory Yjs document state and debounced persistence to PostgreSQL.
/// Each active document has a binary state buffer that accumulates Yjs updates.
/// After a configurable idle period, the state is flushed to the database.
/// </summary>
public class DocumentSyncService : IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentSyncService> _logger;
    private readonly ConcurrentDictionary<string, DocumentState> _activeDocuments = new();
    private readonly TimeSpan _debounceInterval = TimeSpan.FromSeconds(3);
    private readonly TimeSpan _versionInterval = TimeSpan.FromMinutes(5);

    public DocumentSyncService(IServiceScopeFactory scopeFactory, ILogger<DocumentSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Gets the current Yjs state for a document. Loads from DB if not in memory.
    /// </summary>
    public async Task<byte[]> GetDocumentStateAsync(string documentId)
    {
        if (_activeDocuments.TryGetValue(documentId, out var state))
        {
            return state.CurrentState;
        }

        // Load from database
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TandemDbContext>();
        var docGuid = Guid.Parse(documentId);

        var doc = await db.Documents.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == docGuid);

        if (doc == null)
            return [];

        var docState = new DocumentState
        {
            CurrentState = doc.YjsState ?? [],
            LastUpdated = DateTime.UtcNow,
            LastVersionSnapshot = DateTime.UtcNow
        };

        _activeDocuments.TryAdd(documentId, docState);
        return docState.CurrentState;
    }

    /// <summary>
    /// Applies a Yjs update to the in-memory document state and schedules a debounced save.
    /// Returns the merged state (the raw update bytes to broadcast).
    /// </summary>
    public void ApplyUpdate(string documentId, byte[] update, string userId)
    {
        var state = _activeDocuments.GetOrAdd(documentId, _ => new DocumentState());

        lock (state.Lock)
        {
            // Append the update to accumulated updates
            state.PendingUpdates.Add(update);
            state.LastUpdated = DateTime.UtcNow;
            state.LastUpdatedBy = userId;

            // Cancel previous debounce timer and start a new one
            state.DebounceCts?.Cancel();
            state.DebounceCts = new CancellationTokenSource();
            var cts = state.DebounceCts;

            _ = Task.Delay(_debounceInterval, cts.Token)
                .ContinueWith(async _ =>
                {
                    await FlushToDatabase(documentId);
                }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }
    }

    /// <summary>
    /// Stores the accumulated state snapshot to PostgreSQL.
    /// </summary>
    public async Task FlushToDatabase(string documentId)
    {
        if (!_activeDocuments.TryGetValue(documentId, out var state))
            return;

        byte[] stateToSave;
        string? userId;
        bool shouldCreateVersion;

        lock (state.Lock)
        {
            if (state.PendingUpdates.Count == 0)
                return;

            // The current state is the full Yjs doc state (set by the client's full state sync)
            stateToSave = state.CurrentState;
            userId = state.LastUpdatedBy;

            shouldCreateVersion = DateTime.UtcNow - state.LastVersionSnapshot > _versionInterval;
            if (shouldCreateVersion)
            {
                state.LastVersionSnapshot = DateTime.UtcNow;
            }

            state.PendingUpdates.Clear();
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TandemDbContext>();
            var docGuid = Guid.Parse(documentId);

            var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == docGuid);
            if (doc == null) return;

            doc.YjsState = stateToSave;
            doc.UpdatedAt = DateTime.UtcNow;

            if (shouldCreateVersion)
            {
                var versionCount = await db.DocumentVersions.CountAsync(v => v.DocumentId == docGuid);
                db.DocumentVersions.Add(new Data.Entities.DocumentVersion
                {
                    Id = Guid.NewGuid(),
                    DocumentId = docGuid,
                    YjsSnapshot = stateToSave,
                    VersionNumber = versionCount + 1,
                    Label = $"Auto-save v{versionCount + 1}",
                    CreatedByUserId = userId ?? "",
                    CreatedAt = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync();
            _logger.LogDebug("Flushed document {DocumentId} to database (version snapshot: {IsVersion})", documentId, shouldCreateVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush document {DocumentId} to database", documentId);
        }
    }

    /// <summary>
    /// Updates the full state of a document in memory (called when receiving a full state sync).
    /// </summary>
    public void SetFullState(string documentId, byte[] fullState)
    {
        var state = _activeDocuments.GetOrAdd(documentId, _ => new DocumentState());
        lock (state.Lock)
        {
            state.CurrentState = fullState;
        }
    }

    /// <summary>
    /// Removes a document from in-memory tracking (e.g., when all clients disconnect).
    /// Flushes any pending updates first.
    /// </summary>
    public async Task EvictDocumentAsync(string documentId)
    {
        await FlushToDatabase(documentId);
        _activeDocuments.TryRemove(documentId, out _);
        _logger.LogInformation("Evicted document {DocumentId} from memory", documentId);
    }

    public bool IsDocumentActive(string documentId) => _activeDocuments.ContainsKey(documentId);

    public void Dispose()
    {
        foreach (var kvp in _activeDocuments)
        {
            kvp.Value.DebounceCts?.Cancel();
            kvp.Value.DebounceCts?.Dispose();
        }
    }

    private class DocumentState
    {
        public byte[] CurrentState { get; set; } = [];
        public List<byte[]> PendingUpdates { get; } = [];
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        public DateTime LastVersionSnapshot { get; set; } = DateTime.UtcNow;
        public string? LastUpdatedBy { get; set; }
        public CancellationTokenSource? DebounceCts { get; set; }
        public object Lock { get; } = new();
    }
}
