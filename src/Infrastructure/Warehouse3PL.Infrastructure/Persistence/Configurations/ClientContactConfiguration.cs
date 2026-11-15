using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Security;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ClientContactConfiguration : IEntityTypeConfiguration<ClientContact>
{
    public void Configure(EntityTypeBuilder<ClientContact> builder)
    {
        builder.ToTable("client_contacts");

        builder.HasKey(cc => cc.Id);
        builder.Property(cc => cc.Id)
            .HasColumnName("contact_id")
            .ValueGeneratedNever();

        builder.Property(cc => cc.ClientId)
            .HasColumnName("client_id")
            .IsRequired();

        // Enforce B-Tree index on client_id per DB-SPEC-3PL-2026
        builder.HasIndex(cc => cc.ClientId);

        builder.Property(cc => cc.FirstName)
            .HasColumnName("first_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(cc => cc.LastName)
            .HasColumnName("last_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(cc => cc.Email)
            .HasColumnName("email")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(cc => cc.Phone)
            .HasColumnName("phone")
            .HasMaxLength(50);

        builder.Property(cc => cc.IsBillingContact)
            .HasColumnName("is_billing_contact")
            .HasDefaultValue(false);

        builder.Property(cc => cc.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.Property(cc => cc.DeletedAt).HasColumnName("deleted_at");
        builder.Property(cc => cc.DeletedBy).HasColumnName("deleted_by");

        builder.Property(cc => cc.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(cc => cc.CreatedBy).HasColumnName("created_by");
        builder.Property(cc => cc.UpdatedAt).HasColumnName("updated_at");
        builder.Property(cc => cc.UpdatedBy).HasColumnName("updated_by");
    }
}

