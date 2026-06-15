using MediatR;
using PayFlow.Application.Interfaces;
using PayFlow.Application.Features.Payments.DTOs;

namespace PayFlow.Application.Features.Payments.Queries.GetPayment;

public class GetPaymentQueryHandler : IRequestHandler<GetPaymentQuery, PaymentDetailDto>
{
    private readonly IPaymentReadRepository _readRepository;

    public GetPaymentQueryHandler(IPaymentReadRepository readRepository)
        => _readRepository = readRepository;

    public async Task<PaymentDetailDto> Handle(GetPaymentQuery query, CancellationToken cancellationToken)
        => await _readRepository.GetDetailAsync(query.PaymentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Payment {query.PaymentId} not found.");
}
