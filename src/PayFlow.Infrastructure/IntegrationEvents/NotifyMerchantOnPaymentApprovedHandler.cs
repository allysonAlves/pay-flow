using MediatR;
using PayFlow.Application.IntegrationEvents;
using PayFlow.Application.Interfaces;

namespace PayFlow.Infrastructure.IntegrationEvents;

public class NotifyMerchantOnPaymentApprovedHandler : INotificationHandler<PaymentApprovedIntegrationEvent>
{
    private readonly IMerchantNotifier _notifier;

    public NotifyMerchantOnPaymentApprovedHandler(IMerchantNotifier notifier) => _notifier = notifier;

    public Task Handle(PaymentApprovedIntegrationEvent notification, CancellationToken cancellationToken)
        => _notifier.NotifyPaymentApprovedAsync(notification.PaymentId, cancellationToken);
}
