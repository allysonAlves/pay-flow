namespace PayFlow.Application.Interfaces;

public interface IIdempotencyService
{
    Task<bool> HasBeenProcessedAsync(string key, CancellationToken ct = default);
    Task MarkAsProcessedAsync(string key, TimeSpan expiry, CancellationToken ct = default);

}