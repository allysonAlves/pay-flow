using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayFlow.Infrastructure.Outbox;

namespace PayFlow.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.EventType).HasColumnName("event_type").HasMaxLength(200).IsRequired();
        builder.Property(o => o.Payload).HasColumnName("payload").IsRequired();
        builder.Property(o => o.CreatedAt).HasColumnName("created_at");
        builder.Property(o => o.ProcessedAt).HasColumnName("processed_at");
        builder.Property(o => o.Error).HasColumnName("error");
        builder.Property(o => o.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);

        builder.HasIndex(o => o.ProcessedAt).HasDatabaseName("ix_outbox_messages_processed_at");
    }
}
