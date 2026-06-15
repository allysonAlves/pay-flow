using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PayFlow.Infrastructure.Persistence;

namespace PayFlow.Infrastructure.Outbox;

internal sealed class EfOutboxStore : IOutboxStore
{
    private readonly PaymentDbContext _context;
    private IDbContextTransaction? _transaction;

    public EfOutboxStore(PaymentDbContext context) => _context = context;

    public async Task<List<OutboxMessage>> LockNextBatchAsync(int batchSize, CancellationToken ct)
    {
        _transaction = await _context.Database.BeginTransactionAsync(ct);
        return await _context.OutboxMessages
            .FromSqlInterpolated($"""
                SELECT * FROM outbox_messages
                WHERE processed_at IS NULL
                ORDER BY created_at
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        await _context.SaveChangesAsync(ct);
        if (_transaction is not null)
        {
            await _transaction.CommitAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
}
