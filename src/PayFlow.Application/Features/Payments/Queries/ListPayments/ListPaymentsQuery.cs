using MediatR;
using PayFlow.Application.Common;
using PayFlow.Application.Features.Payments.DTOs;

namespace PayFlow.Application.Features.Payments.Queries.ListPayments;

public record ListPaymentsQuery(
    string? Status = null,
    int Page = 1,
    int PageSize = 20
) : IRequest<PagedResult<PaymentSummaryDto>>;
