using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;
namespace Warehouse3PL.Infrastructure.Persistence.Configurations;
public sealed class MonitoringRuleConfiguration : IEntityTypeConfiguration<MonitoringRule>
{
    public void Configure(EntityTypeBuilder<MonitoringRule> b)
    {
        b.ToTable("monitoring_rules"); b.HasKey(x=>x.Id); b.Property(x=>x.Name).HasMaxLength(150); b.Property(x=>x.Threshold).HasPrecision(28,8);
        b.HasIndex(x=>new{x.ClientId,x.FacilityId,x.Enabled}); b.HasAlternateKey(x=>new{x.Id,x.ClientId,x.FacilityId});
    }
}
public sealed class MonitoringAlertConfiguration : IEntityTypeConfiguration<MonitoringAlert>
{
    public void Configure(EntityTypeBuilder<MonitoringAlert> b)
    {
        b.ToTable("monitoring_alerts"); b.HasKey(x=>x.Id); b.Property(x=>x.EvidenceKey).HasMaxLength(100); b.Property(x=>x.Explanation).HasMaxLength(1000); b.Property(x=>x.ResolutionNotes).HasMaxLength(1000); b.Property(x=>x.ObservedValue).HasPrecision(28,8);
        b.HasIndex(x=>new{x.RuleId,x.EvidenceKey}).IsUnique(); b.HasIndex(x=>new{x.ClientId,x.FacilityId,x.Status,x.Severity});
        b.Property<uint>("xmin").IsRowVersion();
        b.HasOne<MonitoringRule>().WithMany().HasForeignKey(x=>new{x.RuleId,x.ClientId,x.FacilityId}).HasPrincipalKey(x=>new{x.Id,x.ClientId,x.FacilityId}).OnDelete(DeleteBehavior.Restrict);
    }
}
