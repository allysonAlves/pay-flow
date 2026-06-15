namespace PayFlow.Infrastructure.Gateway;

public class FakeGatewayOptions
{
    public decimal FailureThreshold { get; set; } = 10_000;
    public int DelayMs { get; set; } = 500;
}
