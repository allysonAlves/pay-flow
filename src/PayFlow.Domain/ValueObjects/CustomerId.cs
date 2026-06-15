namespace PayFlow.Domain.ValueObjects;

public record class CustomerId(Guid Value)
{
    public static CustomerId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("CustomerId cannot be empty.");
        return new(value);
    }
}