using PayFlow.Domain.Aggregates.Payments;
using PayFlow.Domain.ValueObjects;

namespace PayFlow.Domain.Interfaces;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(PaymentId id, CancellationToken ct = default);
    Task AddAsync(Payment payment, CancellationToken ct = default);
    Task UpdateAsync(Payment payment, CancellationToken ct = default);
}