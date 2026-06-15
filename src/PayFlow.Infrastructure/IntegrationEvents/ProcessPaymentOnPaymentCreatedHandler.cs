using MediatR;
using PayFlow.Application.IntegrationEvents;
using PayFlow.Application.Features.Payments.Commands.ProcessPayment;

namespace PayFlow.Infrastructure.IntegrationEvents;

public class ProcessPaymentOnPaymentCreatedHandler : INotificationHandler<PaymentCreatedIntegrationEvent>
{
    private readonly IMediator _mediator;

    public ProcessPaymentOnPaymentCreatedHandler(IMediator mediator) => _mediator = mediator;

    public Task Handle(PaymentCreatedIntegrationEvent notification, CancellationToken cancellationToken)
        => _mediator.Send(new ProcessPaymentCommand(notification.PaymentId), cancellationToken);
}
