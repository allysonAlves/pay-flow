namespace PayFlow.API.Requests;

public record CreatePaymentRequest(Guid CustomerId, Guid MerchantId, decimal Amount, string Currency);
