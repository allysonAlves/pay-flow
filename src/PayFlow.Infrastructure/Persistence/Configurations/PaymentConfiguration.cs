using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayFlow.Domain.Aggregates.Payments;
using PayFlow.Domain.ValueObjects;

namespace PayFlow.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => PaymentId.From(value));

        builder.Property(p => p.CustomerId)
            .HasColumnName("customer_id")
            .HasConversion(id => id.Value, value => CustomerId.From(value));

        builder.Property(p => p.MerchantId)
            .HasColumnName("merchant_id")
            .HasConversion(id => id.Value, value => MerchantId.From(value));

        builder.OwnsOne(p => p.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("amount").HasPrecision(18, 2).IsRequired();
            money.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        });

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(p => p.CreatedAt).HasDatabaseName("ix_payments_created_at");

        builder.Ignore(p => p.DomainEvents);
    }
}
