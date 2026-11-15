using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Outbound;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class DeliveryExceptionContactLogConfiguration : IEntityTypeConfiguration<DeliveryExceptionContactLog>
{
    public void Configure(EntityTypeBuilder<DeliveryExceptionContactLog> builder)
    {
        builder.ToTable("delivery_exception_contact_logs");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("contact_log_id")
            .ValueGeneratedNever();

        builder.Property(c => c.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        builder.Property(c => c.ExceptionId)
            .HasColumnName("exception_id")
            .IsRequired();

        builder.Property(c => c.PackageId)
            .HasColumnName("package_id")
            .IsRequired();

        builder.Property(c => c.ContactMethod)
            .HasColumnName("contact_method")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.Outcome)
            .HasColumnName("outcome")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.ContactedAtUtc)
            .HasColumnName("contacted_at_utc")
            .IsRequired();

        builder.Property(c => c.StaffUserId)
            .HasColumnName("staff_user_id")
            .IsRequired();

        builder.Property(c => c.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(c => c.ResolutionAction)
            .HasColumnName("resolution_action")
            .HasMaxLength(500);

        // Auditing
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.CreatedBy).HasColumnName("created_by");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.Property(c => c.UpdatedBy).HasColumnName("updated_by");

        // Indexes
        builder.HasIndex(c => new { c.ExceptionId, c.ContactedAtUtc })
            .HasDatabaseName("ix_delivery_exception_contact_logs_exception_id");

        builder.HasIndex(c => c.ClientId)
            .HasDatabaseName("ix_delivery_exception_contact_logs_client_id");

        // Navigations
        builder.HasOne(c => c.Exception)
            .WithMany(e => e.ContactLogs)
            .HasForeignKey(c => c.ExceptionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.StaffUser)
            .WithMany()
            .HasForeignKey(c => c.StaffUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

