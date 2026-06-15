using PayFlow.Domain.Aggregates.Payments;
using PayFlow.Domain.Interfaces;
using PayFlow.Domain.ValueObjects;

namespace PayFlow.Infrastructure.Persistence.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly PaymentDbContext _context;

    public PaymentRepository(PaymentDbContext context) => _context = context;

    public async Task<Payment?> GetByIdAsync(PaymentId id, CancellationToken ct = default)
        => await _context.Payments.FindAsync(new object[] { id }, ct);

    public async Task AddAsync(Payment payment, CancellationToken ct = default)
        => await _context.Payments.AddAsync(payment, ct);

    public Task UpdateAsync(Payment payment, CancellationToken ct = default)
        => Task.CompletedTask;
}
