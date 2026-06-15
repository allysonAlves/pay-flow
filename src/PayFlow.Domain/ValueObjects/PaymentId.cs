namespace PayFlow.Domain.ValueObjects;

public record class PaymentId(Guid Value)
{
    public static PaymentId New() => new PaymentId(Guid.NewGuid());
    
    public static PaymentId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("PaymentId cannot be empty.");
        return new(value);
    }
}