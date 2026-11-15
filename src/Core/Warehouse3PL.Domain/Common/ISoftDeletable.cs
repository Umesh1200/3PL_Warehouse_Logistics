namespace Warehouse3PL.Domain.Common;

/// <summary>
/// Interface for entities that support soft deletion instead of hard database deletion.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    Guid? DeletedBy { get; set; }
}

