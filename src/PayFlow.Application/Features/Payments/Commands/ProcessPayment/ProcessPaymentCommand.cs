using MediatR;

namespace PayFlow.Application.Features.Payments.Commands.ProcessPayment;

public record ProcessPaymentCommand(Guid PaymentId) : IRequest;
