using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Infrastructure.Persistence;

namespace Warehouse3PL.Infrastructure.Services.Billing;

/// <summary>
/// Centralizes concurrency-safe invoice and credit-note sequence allocation and non-mutating dry-run previews.
/// On PostgreSQL, callers must execute <see cref="AllocateNumbersAsync"/> inside
/// <see cref="IApplicationDbContext.ExecuteWithInvoiceSequenceLockAsync{TResult}"/>.
/// </summary>
internal static class InvoiceSequenceAllocator
{
    public static string NormalizePrefix(string? prefix) =>
        string.IsNullOrWhiteSpace(prefix) ? "INV" : prefix.Trim().ToUpperInvariant();

    public static async Task<IReadOnlyList<string>> AllocateNumbersAsync(
        IApplicationDbContext dbContext,
        Guid clientId,
        int fiscalYear,
        string prefix,
        string clientCode,
        int count,
        CancellationToken cancellationToken)
    {
        if (count <= 0)
        {
            return Array.Empty<string>();
        }

        var normalizedPrefix = NormalizePrefix(prefix);
        InvoiceSequence sequence;

        if (!dbContext.IsRelationalDatabase)
        {
            sequence = await dbContext.InvoiceSequences
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    s => s.ClientId == clientId &&
                         s.FiscalYear == fiscalYear &&
                         s.Prefix == normalizedPrefix,
                    cancellationToken)
                ?? await CreateAndAddSequenceAsync(dbContext, clientId, fiscalYear, normalizedPrefix, cancellationToken);
        }
        else
        {
            var appDb = dbContext as ApplicationDbContext
                ?? throw new InvalidOperationException("Relational sequence allocation requires ApplicationDbContext.");

            if (appDb.Database.CurrentTransaction == null)
            {
                throw new InvalidOperationException(
                    "Relational invoice sequence allocation requires an active database transaction.");
            }

            if (appDb.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
            {
                var createdAt = DateTime.UtcNow;
                await appDb.Database.ExecuteSqlInterpolatedAsync(
                    $@"INSERT INTO invoice_sequences (sequence_id, client_id, fiscal_year, prefix, last_sequence_number, created_at)
                       VALUES ({Guid.NewGuid()}, {clientId}, {fiscalYear}, {normalizedPrefix}, 0, {createdAt})
                       ON CONFLICT (client_id, fiscal_year, prefix) DO NOTHING",
                    cancellationToken);

                var trackedEntry = appDb.ChangeTracker.Entries<InvoiceSequence>()
                    .FirstOrDefault(e => e.Entity.ClientId == clientId &&
                                         e.Entity.FiscalYear == fiscalYear &&
                                         string.Equals(e.Entity.Prefix, normalizedPrefix, StringComparison.OrdinalIgnoreCase));

                if (trackedEntry != null)
                {
                    await appDb.Database.ExecuteSqlInterpolatedAsync(
                        $@"SELECT 1 FROM invoice_sequences
                           WHERE client_id = {clientId} AND fiscal_year = {fiscalYear} AND prefix = {normalizedPrefix}
                           FOR UPDATE",
                        cancellationToken);
                    await trackedEntry.ReloadAsync(cancellationToken);
                    sequence = trackedEntry.Entity;
                }
                else
                {
                    var lockedRows = await appDb.InvoiceSequences
                        .FromSqlInterpolated(
                            $@"SELECT sequence_id, client_id, fiscal_year, prefix, last_sequence_number, created_at, created_by, updated_at, updated_by
                               FROM invoice_sequences
                               WHERE client_id = {clientId} AND fiscal_year = {fiscalYear} AND prefix = {normalizedPrefix}
                               FOR UPDATE")
                        .IgnoreQueryFilters()
                        .ToListAsync(cancellationToken);

                    sequence = lockedRows.Single();
                }
            }
            else
            {
                sequence = await appDb.InvoiceSequences
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        s => s.ClientId == clientId &&
                             s.FiscalYear == fiscalYear &&
                             s.Prefix == normalizedPrefix,
                        cancellationToken)
                    ?? await CreateAndAddSequenceAsync(dbContext, clientId, fiscalYear, normalizedPrefix, cancellationToken);
            }
        }

        var numbers = new string[count];
        for (int i = 0; i < count; i++)
        {
            numbers[i] = sequence.NextNumber(clientCode);
        }

        return numbers;
    }

    public static async Task<IReadOnlyList<string>> PreviewNumbersAsync(
        IApplicationDbContext dbContext,
        Guid clientId,
        int fiscalYear,
        string prefix,
        string clientCode,
        int count,
        CancellationToken cancellationToken)
    {
        if (count <= 0)
        {
            return Array.Empty<string>();
        }

        var normalizedPrefix = NormalizePrefix(prefix);
        var existingLastNumber = await dbContext.InvoiceSequences
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(s => s.ClientId == clientId &&
                        s.FiscalYear == fiscalYear &&
                        s.Prefix == normalizedPrefix)
            .Select(s => (int?)s.LastSequenceNumber)
            .FirstOrDefaultAsync(cancellationToken) ?? 0;

        var previewSequence = InvoiceSequence.Create(clientId, fiscalYear, normalizedPrefix);
        previewSequence.LastSequenceNumber = existingLastNumber;

        var numbers = new string[count];
        for (int i = 0; i < count; i++)
        {
            numbers[i] = previewSequence.NextNumber(clientCode);
        }

        return numbers;
    }

    private static async Task<InvoiceSequence> CreateAndAddSequenceAsync(
        IApplicationDbContext dbContext,
        Guid clientId,
        int fiscalYear,
        string normalizedPrefix,
        CancellationToken cancellationToken)
    {
        var sequence = InvoiceSequence.Create(clientId, fiscalYear, normalizedPrefix);
        await dbContext.InvoiceSequences.AddAsync(sequence, cancellationToken);
        return sequence;
    }
}
