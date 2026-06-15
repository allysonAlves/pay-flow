using MediatR;
using PayFlow.Application.Common;
using PayFlow.Application.Interfaces;
using PayFlow.Application.Features.Payments.DTOs;

namespace PayFlow.Application.Features.Payments.Queries.ListPayments;

public class ListPaymentsQueryHandler : IRequestHandler<ListPaymentsQuery, PagedResult<PaymentSummaryDto>>
{
    private readonly IPaymentReadRepository _readRepository;

    public ListPaymentsQueryHandler(IPaymentReadRepository readRepository)
        => _readRepository = readRepository;

    public Task<PagedResult<PaymentSummaryDto>> Handle(ListPaymentsQuery query, CancellationToken cancellationToken)
        => _readRepository.ListAsync(query.Status, query.Page, query.PageSize, cancellationToken);
}
