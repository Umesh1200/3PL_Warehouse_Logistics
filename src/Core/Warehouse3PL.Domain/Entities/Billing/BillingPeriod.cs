using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Domain aggregate defining a discrete commercial billing window for a client tenant.
/// Governs activity grouping, rating cutoff horizons, and automated invoice runs.
/// DB-SPEC-3PL-2026 Phase 5.0 Commercial Domain &amp; Billing Foundation.
/// </summary>
public class BillingPeriod : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid? FacilityId { get; set; }

    public string PeriodCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public DateTime StartDateUtc { get; set; }
    public DateTime EndDateUtc { get; set; }

    public BillingPeriodStatus Status { get; set; } = BillingPeriodStatus.Open;
    public BillingFrequency Frequency { get; set; } = BillingFrequency.Monthly;

    public DateTime? LockedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public DateTime? InvoicedAtUtc { get; set; }
    public string? Notes { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility? Facility { get; set; }
    public ICollection<BillableActivity> Activities { get; set; } = [];

    public BillingPeriod()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Constructs and validates a new commercial BillingPeriod.
    /// </summary>
    public static BillingPeriod Create(
        Guid clientId,
        string periodCode,
        string name,
        DateTime startDateUtc,
        DateTime endDateUtc,
        BillingFrequency frequency = BillingFrequency.Monthly,
        Guid? facilityId = null,
        string? notes = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client tenant ID is required.");
        }

        if (string.IsNullOrWhiteSpace(periodCode))
        {
            throw new ValidationException(nameof(periodCode), "Billing period code is required.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException(nameof(name), "Billing period name is required.");
        }

        if (startDateUtc >= endDateUtc)
        {
            throw new ValidationException(nameof(startDateUtc), "Start date must be chronologically earlier than end date.");
        }

        return new BillingPeriod
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FacilityId = facilityId,
            PeriodCode = periodCode.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            StartDateUtc = startDateUtc,
            EndDateUtc = endDateUtc,
            Status = BillingPeriodStatus.Open,
            Frequency = frequency,
            Notes = notes?.Trim()
        };
    }

    /// <summary>
    /// Locks the billing period to prevent additional billable activity attachment prior to rating.
    /// </summary>
    public void Lock(DateTime? lockedAtUtc = null)
    {
        if (Status != BillingPeriodStatus.Open)
        {
            throw new InvalidDomainStateException($"Cannot lock a billing period in '{Status}' status.");
        }

        Status = BillingPeriodStatus.Locked;
        LockedAtUtc = lockedAtUtc ?? DateTime.UtcNow;
    }

    /// <summary>
    /// Reopens a locked billing period if adjustments or retroactive operational entries are required.
    /// </summary>
    public void Reopen()
    {
        if (Status != BillingPeriodStatus.Locked)
        {
            throw new InvalidDomainStateException($"Cannot reopen a billing period in '{Status}' status. Only Locked periods can be reopened.");
        }

        Status = BillingPeriodStatus.Open;
        LockedAtUtc = null;
    }

    /// <summary>
    /// Closes the billing period following successful rating and pre-invoice auditing.
    /// </summary>
    public void Close(DateTime? closedAtUtc = null)
    {
        if (Status != BillingPeriodStatus.Locked)
        {
            throw new InvalidDomainStateException($"Billing period must be Locked before it can be Closed. Current status: '{Status}'.");
        }

        Status = BillingPeriodStatus.Closed;
        ClosedAtUtc = closedAtUtc ?? DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the billing period as invoiced after billing run completes.
    /// </summary>
    public void MarkInvoiced(DateTime? invoicedAtUtc = null)
    {
        if (Status != BillingPeriodStatus.Closed && Status != BillingPeriodStatus.Locked)
        {
            throw new InvalidDomainStateException($"Cannot mark as invoiced from status '{Status}'. Period must be Locked or Closed.");
        }

        Status = BillingPeriodStatus.Invoiced;
        InvoicedAtUtc = invoicedAtUtc ?? DateTime.UtcNow;
    }
}

