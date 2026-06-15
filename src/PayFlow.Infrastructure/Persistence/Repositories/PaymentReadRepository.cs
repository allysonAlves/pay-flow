using Microsoft.EntityFrameworkCore;
using PayFlow.Application.Common;
using PayFlow.Application.Interfaces;
using PayFlow.Application.Features.Payments.DTOs;
using PayFlow.Domain.Aggregates.Payments;
using PayFlow.Domain.ValueObjects;

namespace PayFlow.Infrastructure.Persistence.Repositories;

public class PaymentReadRepository : IPaymentReadRepository
{
    private readonly PaymentDbContext _context;

    public PaymentReadRepository(PaymentDbContext context) => _context = context;

    public async Task<PaymentDetailDto?> GetDetailAsync(Guid paymentId, CancellationToken ct = default)
        => await _context.Payments
            .AsNoTracking()
            .Where(p => p.Id == PaymentId.From(paymentId))
            .Select(p => new PaymentDetailDto(
                p.Id.Value,
                p.CustomerId.Value,
                p.MerchantId.Value,
                p.Amount.Amount,
                p.Amount.Currency,
                p.Status.ToString(),
                p.CreatedAt,
                p.UpdatedAt))
            .FirstOrDefaultAsync(ct);

    public async Task<PagedResult<PaymentSummaryDto>> ListAsync(
        string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _context.Payments.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PaymentStatus>(status, true, out var parsed))
            query = query.Where(p => p.Status == parsed);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PaymentSummaryDto(
                p.Id.Value,
                p.Amount.Amount,
                p.Amount.Currency,
                p.Status.ToString(),
                p.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<PaymentSummaryDto>(items, total, page, pageSize);
    }
}
