using Microsoft.Extensions.Options;
using PayFlow.Application.Contracts;
using PayFlow.Application.Interfaces;

namespace PayFlow.Infrastructure.Gateway;

public class FakePaymentGateway : IPaymentGateway
{
    private readonly FakeGatewayOptions _options;

    public FakePaymentGateway(IOptions<FakeGatewayOptions> options)
        => _options = options.Value;

    public async Task<GatewayResponse> ProcessAsync(GatewayRequest request, CancellationToken ct = default)
    {
        await Task.Delay(_options.DelayMs, ct);

        if (request.Amount >= _options.FailureThreshold)
            return new GatewayResponse(false, null, "Insufficient funds");

        return new GatewayResponse(true, Guid.NewGuid().ToString(), null);
    }
}
