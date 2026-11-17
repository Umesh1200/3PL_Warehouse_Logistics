using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Warehouse3PL.Domain.Entities.Analytics;
namespace Warehouse3PL.Infrastructure.Persistence.Configurations;
public sealed class AnalyticsIngestionJournalConfiguration:IEntityTypeConfiguration<AnalyticsIngestionJournal>
{
    public void Configure(EntityTypeBuilder<AnalyticsIngestionJournal>b)
    {
        b.ToTable("analytics_ingestion_journal");b.HasKey(x=>x.Id);b.Property(x=>x.ProjectionName).HasMaxLength(100);b.Property(x=>x.EventKind).HasMaxLength(100);b.Property(x=>x.PayloadHash).HasMaxLength(64);
        b.HasIndex(x=>new{x.ProjectionName,x.EventId}).IsUnique();b.HasIndex(x=>new{x.ProjectionName,x.IngestionOrdinal}).IsUnique();
    }
}
