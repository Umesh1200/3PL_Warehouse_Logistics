using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class PickWaveOrderConfiguration : IEntityTypeConfiguration<PickWaveOrder>
{
    public void Configure(EntityTypeBuilder<PickWaveOrder> builder)
    {
        builder.ToTable("pick_wave_orders");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id)
            .HasColumnName("pick_wave_order_id")
            .ValueGeneratedNever();

        builder.Property(o => o.PickWaveId)
            .HasColumnName("pick_wave_id")
            .IsRequired();

        builder.Property(o => o.OutboundOrderId)
            .HasColumnName("outbound_order_id")
            .IsRequired();

        builder.Property(o => o.SequenceNumber)
            .HasColumnName("sequence_number")
            .IsRequired();

        // Unique constraint per wave and order
        builder.HasIndex(o => new { o.PickWaveId, o.OutboundOrderId })
            .IsUnique();

        // Relationships
        builder.HasOne(o => o.PickWave)
            .WithMany(w => w.WaveOrders)
            .HasForeignKey(o => o.PickWaveId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(o => o.OutboundOrder)
            .WithMany()
            .HasForeignKey(o => o.OutboundOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

