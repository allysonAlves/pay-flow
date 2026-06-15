namespace PayFlow.Infrastructure.Outbox;

public class OutboxOptions
{
    public int IntervalSeconds { get; set; } = 10;
    public int BatchSize { get; set; } = 50;
}
