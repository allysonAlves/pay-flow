using MediatR;

namespace PayFlow.Application.Features.Payments.Commands.CancelPayment;

public record CancelPaymentCommand(Guid PaymentId) : IRequest;
