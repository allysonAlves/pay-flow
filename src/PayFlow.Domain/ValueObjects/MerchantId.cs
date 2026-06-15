namespace PayFlow.Domain.ValueObjects;

public record class MerchantId(Guid Value)
{
    public static MerchantId From(Guid value)
    {
        if(value == Guid.Empty)
            throw new ArgumentException("MerchantId cannot be empty.");
        return new(value);
    }
}