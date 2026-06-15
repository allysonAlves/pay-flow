namespace PayFlow.Application.Contracts;

public record GatewayRequest(Guid PaymentId, decimal Amount, string Currency);