namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Standard 30-day AR aging intervals for customer statements.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public enum CustomerStatementAgingBucket
{
    /// <summary>
    /// Not yet due or due today (Days Overdue &lt;= 0).
    /// </summary>
    Current = 1,

    /// <summary>
    /// 1 to 30 days past due date.
    /// </summary>
    Days1To30 = 2,

    /// <summary>
    /// 31 to 60 days past due date.
    /// </summary>
    Days31To60 = 3,

    /// <summary>
    /// 61 to 90 days past due date.
    /// </summary>
    Days61To90 = 4,

    /// <summary>
    /// More than 90 days past due date.
    /// </summary>
    DaysOver90 = 5
}

