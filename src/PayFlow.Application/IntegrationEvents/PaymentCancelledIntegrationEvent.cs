using MediatR;

namespace PayFlow.Application.IntegrationEvents;

public record PaymentCancelledIntegrationEvent(Guid PaymentId) : INotification;
