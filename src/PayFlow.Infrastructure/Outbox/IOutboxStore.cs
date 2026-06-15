namespace PayFlow.Infrastructure.Outbox;

internal interface IOutboxStore
{
    Task<List<OutboxMessage>> LockNextBatchAsync(int batchSize, CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
}
