namespace Warehouse3PL.Application.Common.Interfaces;

public interface ITenantProvider
{
    Guid? TenantId { get; }
    bool HasTenant { get; }
    bool BypassTenantFilter { get; set; }

    void SetTenantId(Guid tenantId);
}

