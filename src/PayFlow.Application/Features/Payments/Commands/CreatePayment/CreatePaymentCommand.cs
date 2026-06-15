using MediatR;

namespace PayFlow.Application.Features.Payments.Commands.CreatePayment;

public record CreatePaymentCommand(
    Guid CustomerId,
    Guid MerchantId,
    decimal Amount,
    string Currency
) : IRequest<Guid>;