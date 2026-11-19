import React from 'react';
import { useFacility } from '../../context/FacilityContext';
import { PermissionGuard } from '../../components/auth/PermissionGuard';

export const FacilitiesPage: React.FC = () => {
  const { facilities, activeFacility, setActiveFacility, refreshFacilities, isLoadingFacilities } =
    useFacility();

  return (
    <PermissionGuard requiredPermission="facilities:view">
      <div className="facilities-page">
        <div className="card">
          <div className="card-header">
            <div>
              <h2 className="card-title">Authorized Facilities Directory</h2>
              <p className="card-subtitle">
                Physical warehouses and logistics facilities authorized for your account session.
              </p>
            </div>
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => refreshFacilities()}
              disabled={isLoadingFacilities}
            >
              Refresh
            </button>
          </div>

          {isLoadingFacilities ? (
            <div className="table-loading" role="status">
              Loading authorized facilities...
            </div>
          ) : (
            <div className="table-responsive">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Facility Code</th>
                    <th>Warehouse Name</th>
                    <th>Location / City</th>
                    <th>Operating Timezone</th>
                    <th>Status</th>
                    <th>Active Session</th>
                  </tr>
                </thead>
                <tbody>
                  {facilities.length === 0 ? (
                    <tr>
                      <td colSpan={6} className="empty-cell">
                        No authorized facilities found for your tenant scope.
                      </td>
                    </tr>
                  ) : (
                    facilities.map((fac) => {
                      const isCurrent = activeFacility?.id === fac.id;
                      return (
                        <tr key={fac.id} className={isCurrent ? 'row-active-facility' : ''}>
                          <td>
                            <span className="code-badge">{fac.facilityCode}</span>
                          </td>
                          <td>
                            <div className="facility-cell">
                              <span className="facility-name">{fac.name}</span>
                              {fac.addressLine1 && (
                                <span className="facility-address">{fac.addressLine1}</span>
                              )}
                            </div>
                          </td>
                          <td>{fac.city || 'Standard'}</td>
                          <td>
                            <span className="tz-badge">{fac.timezone}</span>
                          </td>
                          <td>
                            {fac.isActive ? (
                              <span className="badge badge-success">Operational</span>
                            ) : (
                              <span className="badge badge-danger">Offline</span>
                            )}
                          </td>
                          <td>
                            {isCurrent ? (
                              <span className="badge-active-indicator">
                                <span className="indicator-dot" /> ACTIVE SCOPE
                              </span>
                            ) : (
                              <button
                                type="button"
                                className="btn-action"
                                onClick={() => setActiveFacility(fac)}
                                aria-label={`Switch active warehouse to ${fac.name}`}
                              >
                                Select Facility
                              </button>
                            )}
                          </td>
                        </tr>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>
          )}
        </div>

        <style>{`
          .facilities-page {
            display: flex;
            flex-direction: column;
            gap: 20px;
          }
          .facility-cell {
            display: flex;
            flex-direction: column;
          }
          .facility-name {
            font-weight: 700;
            color: var(--text-main);
          }
          .facility-address {
            font-size: 12.5px;
            color: var(--text-muted);
          }
          .tz-badge {
            background: #f1f5f9;
            color: #334155;
            padding: 3px 8px;
            border-radius: var(--radius-xs);
            font-size: 12px;
            font-weight: 600;
          }
          .row-active-facility {
            background: #fcfcff;
          }
          .badge-active-indicator {
            display: inline-flex;
            align-items: center;
            gap: 6px;
            background: var(--brand-primary-light);
            color: var(--brand-primary);
            font-size: 11px;
            font-weight: 750;
            padding: 4px 10px;
            border-radius: 12px;
            letter-spacing: 0.05em;
          }
          .indicator-dot {
            width: 6px;
            height: 6px;
            border-radius: 50%;
            background: var(--brand-primary);
          }
        `}</style>
      </div>
    </PermissionGuard>
  );
};
