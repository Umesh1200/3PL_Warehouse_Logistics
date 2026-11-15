using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ToTable("order_status_histories");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).HasColumnName("history_id").ValueGeneratedNever();

        builder.Property(h => h.OutboundOrderId).HasColumnName("order_id").IsRequired();
        builder.Property(h => h.FromStatus).HasColumnName("from_status").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(h => h.ToStatus).HasColumnName("to_status").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(h => h.Reason).HasColumnName("reason").HasMaxLength(500);
        builder.Property(h => h.Notes).HasColumnName("notes").HasMaxLength(1000);
        builder.Property(h => h.ChangedByUserId).HasColumnName("changed_by_user_id").IsRequired();
        builder.Property(h => h.ChangedAtUtc).HasColumnName("changed_at_utc").IsRequired();

        // Index
        builder.HasIndex(h => new { h.OutboundOrderId, h.ChangedAtUtc });

        // Relationship
        builder.HasOne(h => h.OutboundOrder)
            .WithMany(o => o.StatusHistories)
            .HasForeignKey(h => h.OutboundOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

