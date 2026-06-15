using PayFlow.Application.Interfaces;
using StackExchange.Redis;

namespace PayFlow.Infrastructure.Idempotency;

public class RedisIdempotencyService : IIdempotencyService
{
    private readonly IConnectionMultiplexer _redis;

    public RedisIdempotencyService(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<bool> HasBeenProcessedAsync(string key, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        return await db.KeyExistsAsync(key);
    }

    public async Task MarkAsProcessedAsync(string key, TimeSpan expiry, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync(key, "1", expiry, When.NotExists);
    }
}
