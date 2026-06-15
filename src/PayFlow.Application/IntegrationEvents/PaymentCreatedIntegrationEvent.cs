using MediatR;

namespace PayFlow.Application.IntegrationEvents;

public record PaymentCreatedIntegrationEvent(Guid PaymentId) : INotification;
