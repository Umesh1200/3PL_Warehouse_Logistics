using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Infrastructure.Persistence.Configurations;

public class ProductAffinityPolicyConfiguration : IEntityTypeConfiguration<ProductAffinityPolicy>
{
    public void Configure(EntityTypeBuilder<ProductAffinityPolicy> builder)
    {
        builder.ToTable("product_affinity_policies");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("policy_id")
            .ValueGeneratedNever();

        builder.Property(p => p.FacilityId)
            .HasColumnName("facility_id")
            .IsRequired(false);

        builder.Property(p => p.ClientId)
            .HasColumnName("client_id")
            .IsRequired(false);

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(p => p.Description)
            .HasColumnName("description")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(p => p.MinSupport)
            .HasPrecision(8, 4);

        builder.Property(p => p.MinConfidence)
            .HasPrecision(8, 4);

        builder.Property(p => p.MinLift)
            .HasPrecision(8, 4);

        builder.Property(p => p.MinClusterLift)
            .HasPrecision(8, 4);

        builder.Property(p => p.WeightProductAffinity)
            .HasPrecision(5, 4);

        builder.HasIndex(p => new { p.FacilityId, p.ClientId, p.IsActive });
    }
}

public class ProductAffinityPairConfiguration : IEntityTypeConfiguration<ProductAffinityPair>
{
    public void Configure(EntityTypeBuilder<ProductAffinityPair> builder)
    {
        builder.ToTable("product_affinity_pairs");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("pair_id")
            .ValueGeneratedNever();

        builder.Property(p => p.ItemASku)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.ItemBSku)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.Support)
            .HasPrecision(8, 4);

        builder.Property(p => p.ItemASupport)
            .HasPrecision(8, 4);

        builder.Property(p => p.ItemBSupport)
            .HasPrecision(8, 4);

        builder.Property(p => p.ConfidenceAtoB)
            .HasPrecision(8, 4);

        builder.Property(p => p.ConfidenceBtoA)
            .HasPrecision(8, 4);

        builder.Property(p => p.MaxConfidence)
            .HasPrecision(8, 4);

        builder.Property(p => p.KulczynskiMeasure)
            .HasPrecision(8, 4);

        builder.Property(p => p.Lift)
            .HasPrecision(10, 4);

        builder.Property(p => p.ChiSquareStatistic)
            .HasPrecision(12, 4);

        builder.Property(p => p.MinExpectedCellFrequency)
            .HasPrecision(12, 4);

        builder.Property(p => p.PValue)
            .HasPrecision(10, 6);

        builder.HasIndex(p => new { p.FacilityId, p.ClientId, p.ItemAId, p.ItemBId });
        builder.HasIndex(p => new { p.FacilityId, p.ClientId, p.IsStatisticallySignificant, p.Lift });
    }
}

public class ProductAffinityClusterConfiguration : IEntityTypeConfiguration<ProductAffinityCluster>
{
    public void Configure(EntityTypeBuilder<ProductAffinityCluster> builder)
    {
        builder.ToTable("product_affinity_clusters");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("cluster_id")
            .ValueGeneratedNever();

        builder.Property(c => c.ClusterCode)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.PrimaryAnchorSku)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.AverageLift)
            .HasPrecision(10, 4);

        builder.Property(c => c.MaxLift)
            .HasPrecision(10, 4);

        builder.HasMany(c => c.Members)
            .WithOne(m => m.Cluster)
            .HasForeignKey(m => m.ClusterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.FacilityId, c.ClientId, c.ClusterCode });
    }
}

public class ProductAffinityClusterMemberConfiguration : IEntityTypeConfiguration<ProductAffinityClusterMember>
{
    public void Configure(EntityTypeBuilder<ProductAffinityClusterMember> builder)
    {
        builder.ToTable("product_affinity_cluster_members");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasColumnName("member_id")
            .ValueGeneratedNever();

        builder.Property(m => m.Sku)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(m => m.AverageWithinClusterLift)
            .HasPrecision(10, 4);

        builder.HasIndex(m => new { m.ClusterId, m.ItemId });
        builder.HasIndex(m => m.ItemId);
    }
}
