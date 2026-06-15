namespace PayFlow.Application.Contracts;

public record GatewayResponse(bool Success, string? TransactionId, string? ErrorMessage);