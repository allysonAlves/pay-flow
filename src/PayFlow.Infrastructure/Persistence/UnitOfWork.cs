using PayFlow.Domain.Interfaces;

namespace PayFlow.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly PaymentDbContext _context;

    public UnitOfWork(PaymentDbContext context) => _context = context;

    public async Task CommitAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
