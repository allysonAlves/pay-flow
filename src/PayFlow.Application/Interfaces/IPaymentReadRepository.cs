using PayFlow.Application.Common;
using PayFlow.Application.Features.Payments.DTOs;

namespace PayFlow.Application.Interfaces;

public interface IPaymentReadRepository
{
    Task<PaymentDetailDto?> GetDetailAsync(Guid paymentId, CancellationToken ct = default);
    Task<PagedResult<PaymentSummaryDto>> ListAsync(
        string? status, int page, int pageSize, CancellationToken ct = default);

}