using StackExchange.Redis;

namespace Tandem.Api.Services;

/// <summary>
/// Manages ephemeral presence data (who's online, cursor positions) via Redis.
/// Uses Redis hashes keyed by document ID with per-user presence data.
/// </summary>
public class PresenceService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PresenceService> _logger;
    private static readonly TimeSpan PresenceTtl = TimeSpan.FromMinutes(5);

    public PresenceService(IConnectionMultiplexer redis, ILogger<PresenceService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task SetUserPresenceAsync(string documentId, string userId, string displayName, string avatarColor)
    {
        var db = _redis.GetDatabase();
        var key = $"presence:{documentId}";
        var value = System.Text.Json.JsonSerializer.Serialize(new PresenceEntry
        {
            UserId = userId,
            DisplayName = displayName,
            AvatarColor = avatarColor,
            JoinedAt = DateTime.UtcNow
        });

        await db.HashSetAsync(key, userId, value);
        await db.KeyExpireAsync(key, PresenceTtl);
    }

    public async Task RemoveUserPresenceAsync(string documentId, string userId)
    {
        var db = _redis.GetDatabase();
        var key = $"presence:{documentId}";
        await db.HashDeleteAsync(key, userId);
    }

    public async Task<List<PresenceEntry>> GetDocumentPresenceAsync(string documentId)
    {
        var db = _redis.GetDatabase();
        var key = $"presence:{documentId}";
        var entries = await db.HashGetAllAsync(key);

        var result = new List<PresenceEntry>();
        foreach (var entry in entries)
        {
            if (entry.Value.HasValue)
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<PresenceEntry>(entry.Value.ToString());
                if (parsed != null)
                    result.Add(parsed);
            }
        }
        return result;
    }

    public async Task<int> GetActiveConnectionCountAsync(string documentId)
    {
        var db = _redis.GetDatabase();
        var key = $"presence:{documentId}";
        return (int)await db.HashLengthAsync(key);
    }
}

public class PresenceEntry
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string AvatarColor { get; set; } = "";
    public DateTime JoinedAt { get; set; }
}
