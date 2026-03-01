using System.Collections.Concurrent;
using Serilog;

namespace SapAdapter.Commands;

/// <summary>
/// LRU-like idempotency cache with TTL-based expiration.
/// Prevents duplicate command execution within the configured window.
/// </summary>
public class IdempotencyCache
{
    private static readonly ILogger Log = Serilog.Log.ForContext<IdempotencyCache>();
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly int _maxEntries;
    private readonly TimeSpan _ttl;

    private record CacheEntry(object Result, DateTime ExpiresAt);

    public IdempotencyCache(int maxEntries = 500, int ttlSeconds = 300)
    {
        _maxEntries = maxEntries;
        _ttl = TimeSpan.FromSeconds(ttlSeconds);
    }

    public object? Get(string key)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (DateTime.UtcNow < entry.ExpiresAt)
            {
                Log.Debug("Idempotency cache HIT for {Key}", key);
                return entry.Result;
            }

            _cache.TryRemove(key, out _);
        }
        return null;
    }

    public void Set(string key, object result)
    {
        // Evict expired entries if over limit
        if (_cache.Count >= _maxEntries)
        {
            var expired = _cache.Where(kvp => DateTime.UtcNow >= kvp.Value.ExpiresAt).Select(kvp => kvp.Key).ToList();
            foreach (var k in expired) _cache.TryRemove(k, out _);
        }

        _cache[key] = new CacheEntry(result, DateTime.UtcNow + _ttl);
        Log.Debug("Idempotency cache SET for {Key} (TTL: {Ttl}s)", key, _ttl.TotalSeconds);
    }
}
