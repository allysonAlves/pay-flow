using PayFlow.Application.Contracts;

namespace PayFlow.Application.Interfaces;

public interface IPaymentGateway
{
    Task<GatewayResponse> ProcessAsync(GatewayRequest request, CancellationToken ct = default);

}