import React, { useCallback, useEffect, useState } from 'react';
import { PermissionGuard } from '../../components/auth/PermissionGuard';
import { AppIcon } from '../../components/common/AppIcon';
import { useAuth } from '../../context/AuthContext';
import { useFacility } from '../../context/FacilityContext';
import { UserType, normalizeUserTypeStrict } from '../../types/auth';
import { formatDateTime } from '../../utils/formatters';
import {
  CountryNormalizationStatusValue,
  PagedNormalizationQuarantineResponse,
  fetchNormalizationQuarantine,
} from '../../services/normalizationService';

const QUARANTINE_PERMISSIONS = [
  'global:normalization:view',
  'facilities:manage',
];

const ENTITY_TYPE_OPTIONS = [
  { value: '', label: 'All Entity Types' },
  { value: 'Facility', label: 'Facility' },
  { value: 'Customer', label: 'Customer' },
  { value: 'Supplier', label: 'Supplier' },
  { value: 'ShippingAddress', label: 'Shipping Address' },
  { value: 'ReturnShipment', label: 'Return Shipment' },
  { value: 'CarrierContractualSlaPolicy', label: 'Carrier SLA Policy' },
  { value: 'CarrierSlaComplianceRecord', label: 'Carrier SLA Record' },
];

export const NormalizationQuarantinePage: React.FC = () => {
  const { user, isSuperAdmin, hasPermission } = useAuth();
  const { activeFacility } = useFacility();

  const [statusFilter, setStatusFilter] = useState<CountryNormalizationStatusValue | 'All'>('PendingNormalization');
  const [entityType, setEntityType] = useState<string>('');
  const [search, setSearch] = useState<string>('');
  const [pageNumber, setPageNumber] = useState<number>(1);
  const [pageSize] = useState<number>(20);
  const [data, setData] = useState<PagedNormalizationQuarantineResponse | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  const normalizedType = user ? normalizeUserTypeStrict(user.userType) : null;
  const isStaff = normalizedType === UserType.Staff;
  const canViewQuarantine =
    isStaff &&
    (isSuperAdmin ||
      hasPermission('global:normalization:view') ||
      hasPermission('facilities:manage'));
  const isPlatformScope =
    isSuperAdmin ||
    hasPermission('global:normalization:view') ||
    hasPermission('analytics:multi-facility:view');
  const scopedFacilityIdParam = !isPlatformScope ? activeFacility?.id : undefined;

  const loadDiagnostics = useCallback(
    async (signal?: AbortSignal) => {
      if (!canViewQuarantine) {
        setIsLoading(false);
        return;
      }

      setIsLoading(true);
      setError(null);
      try {
        const res = await fetchNormalizationQuarantine(
          {
            status: statusFilter,
            entityType: entityType || undefined,
            facilityId: scopedFacilityIdParam,
            search: search || undefined,
            pageNumber,
            pageSize,
          },
          signal
        );
        setData(res);
      } catch (err: any) {
        if (err?.name === 'AbortError') return;
        setError(err?.message || 'Unable to load country normalization quarantine diagnostics.');
      } finally {
        setIsLoading(false);
      }
    },
    [canViewQuarantine, statusFilter, entityType, scopedFacilityIdParam, search, pageNumber, pageSize]
  );

  useEffect(() => {
    const controller = new AbortController();
    void loadDiagnostics(controller.signal);
    return () => controller.abort();
  }, [loadDiagnostics]);

  if (user && !isStaff) {
    return (
      <div className="forbidden-card" role="alert">
        <div className="forbidden-badge">403 FORBIDDEN</div>
        <h2>Staff Authorization Required</h2>
        <p className="forbidden-desc">
          Country normalization quarantine diagnostics are restricted to authorized warehouse Staff.
        </p>
      </div>
    );
  }

  return (
    <PermissionGuard requiredPermissions={QUARANTINE_PERMISSIONS}>
      <div className="quarantine-page">
        <div className="quarantine-kpi-row" aria-live="polite">
          <div className="kpi-card">
            <span className="kpi-label">Pending Quarantine</span>
            <strong className="kpi-value kpi-warn" data-testid="quarantine-pending-count">
              {data ? data.pendingCount : '—'}
            </strong>
          </div>
          <div className="kpi-card">
            <span className="kpi-label">Normalized Audited</span>
            <strong className="kpi-value kpi-ok" data-testid="quarantine-normalized-count">
              {data ? data.normalizedCount : '—'}
            </strong>
          </div>
          <div className="kpi-card">
            <span className="kpi-label">Diagnostic Scope</span>
            <strong className="kpi-scope">
              {isPlatformScope ? 'Platform-Wide (Authorized Staff)' : activeFacility?.facilityCode || 'Assigned Facility'}
            </strong>
          </div>
        </div>

        <div className="card">
          <div className="card-header quarantine-header">
            <div>
              <h2 className="card-title">Country Normalization & Quarantine Diagnostics</h2>
              <p className="card-subtitle">
                Read-only provenance and quarantine queue for legacy ISO 3166-1 country normalization without silent defaulting.
              </p>
            </div>
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => void loadDiagnostics()}
              disabled={isLoading}
            >
              <AppIcon name="RefreshCw" size="sm" />
              <span>Refresh</span>
            </button>
          </div>

          <div className="quarantine-filters" role="search" aria-label="Filter normalization diagnostics">
            <div className="form-field">
              <label htmlFor="quarantine-status-filter">Normalization Status</label>
              <select
                id="quarantine-status-filter"
                value={statusFilter}
                onChange={(e) => {
                  setStatusFilter(e.target.value as CountryNormalizationStatusValue | 'All');
                  setPageNumber(1);
                }}
              >
                <option value="PendingNormalization">Pending Normalization (Quarantine)</option>
                <option value="Normalized">Normalized (Audited)</option>
                <option value="All">All Statuses</option>
              </select>
            </div>

            <div className="form-field">
              <label htmlFor="quarantine-entity-filter">Entity Type</label>
              <select
                id="quarantine-entity-filter"
                value={entityType}
                onChange={(e) => {
                  setEntityType(e.target.value);
                  setPageNumber(1);
                }}
              >
                {ENTITY_TYPE_OPTIONS.map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </select>
            </div>

            <div className="form-field quarantine-search-field">
              <label htmlFor="quarantine-search">Search Entity / Verified Code / Reason Code</label>
              <input
                id="quarantine-search"
                type="search"
                placeholder="e.g. Facility, USA, CONFLICTING_GEOGRAPHIC_EVIDENCE"
                value={search}
                onChange={(e) => {
                  setSearch(e.target.value);
                  setPageNumber(1);
                }}
              />
            </div>
          </div>

          {error ? (
            <div className="error-banner" role="alert">
              <AppIcon name="AlertTriangle" size="sm" />
              <span>{error}</span>
              <button type="button" className="btn btn-secondary" onClick={() => void loadDiagnostics()}>
                Retry
              </button>
            </div>
          ) : isLoading ? (
            <div className="table-loading" role="status">
              Loading scoped normalization diagnostics...
            </div>
          ) : (
            <>
              <div className="table-responsive">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Entity</th>
                      <th>Original Value</th>
                      <th>Status</th>
                      <th>Target ISO</th>
                      <th>Provenance Flags</th>
                      <th>Quarantine / Normalization Reason</th>
                      <th>Scope Context</th>
                      <th>Recorded</th>
                    </tr>
                  </thead>
                  <tbody>
                    {!data || data.items.length === 0 ? (
                      <tr>
                        <td colSpan={8} className="empty-cell">
                          No country normalization diagnostic records match the active scope filters.
                        </td>
                      </tr>
                    ) : (
                      data.items.map((row) => (
                        <tr key={row.auditId}>
                          <td>
                            <div className="entity-cell">
                              <span className="entity-type-label">{row.entityType}</span>
                              <span className="entity-meta">
                                {row.fieldName} • {row.entityId.slice(0, 8)}
                              </span>
                            </div>
                          </td>
                          <td>
                            <code className="raw-value-chip">
                              {row.originalCountryValue === null || row.originalCountryValue === ''
                                ? '<blank>'
                                : row.originalCountryValue}
                            </code>
                          </td>
                          <td>
                            {row.normalizationStatus === 'Normalized' ? (
                              <span className="badge badge-success" aria-label="Status Normalized">
                                <AppIcon name="CheckCircle2" size="xs" /> Normalized
                              </span>
                            ) : (
                              <span className="badge badge-warning" aria-label="Status Pending Normalization">
                                <AppIcon name="AlertTriangle" size="xs" /> Pending Normalization
                              </span>
                            )}
                          </td>
                          <td>
                            {row.normalizedCountryCode ? (
                              <span className="code-badge">{row.normalizedCountryCode}</span>
                            ) : (
                              <span className="text-muted">Unresolved</span>
                            )}
                          </td>
                          <td>
                            <div className="flag-list">
                              {row.preservedHistoricalValue && (
                                <span className="flag-pill">Historical Preserved</span>
                              )}
                              {row.wasInferredFromParent && (
                                <span className="flag-pill">Parent Inferred</span>
                              )}
                              {!row.preservedHistoricalValue && !row.wasInferredFromParent && (
                                <span className="text-muted">{row.inferenceSource || 'Direct'}</span>
                              )}
                            </div>
                          </td>
                          <td className="reason-cell">
                            {row.reasonCode && (
                              <span className="entity-meta" style={{ display: 'block', fontFamily: 'monospace' }}>
                                {row.reasonCode}
                              </span>
                            )}
                            <span>{row.reason}</span>
                          </td>
                          <td>
                            <div className="entity-cell">
                              <span>{row.facilityCode || (row.facilityId ? row.facilityId.slice(0, 8) : 'Platform / Multi-Facility')}</span>
                              {row.clientId && (
                                <span className="entity-meta">Client: {row.clientId.slice(0, 8)}</span>
                              )}
                            </div>
                          </td>
                          <td>
                            {formatDateTime(row.recordedAtUtc, {
                              timeZone: activeFacility?.timezone,
                            })}
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>

              {data && data.totalCount > 0 && (
                <div className="quarantine-pagination">
                  <span className="pagination-meta">
                    Showing page {data.pageNumber} of {Math.max(1, data.totalPages)} ({data.totalCount} total records)
                  </span>
                  <div className="pagination-actions">
                    <button
                      type="button"
                      className="btn btn-secondary"
                      disabled={data.pageNumber <= 1 || isLoading}
                      onClick={() => setPageNumber((p) => Math.max(1, p - 1))}
                    >
                      Previous
                    </button>
                    <button
                      type="button"
                      className="btn btn-secondary"
                      disabled={data.pageNumber >= data.totalPages || isLoading}
                      onClick={() => setPageNumber((p) => p + 1)}
                    >
                      Next
                    </button>
                  </div>
                </div>
              )}
            </>
          )}
        </div>

        <style>{`
          .quarantine-page { display: flex; flex-direction: column; gap: 16px; min-width: 0; max-width: 100%; }
          .quarantine-kpi-row { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(200px, 100%), 1fr)); gap: 12px; }
          .kpi-card { background: #ffffff; border: 1px solid var(--border-subtle); border-radius: var(--radius-md); padding: 14px 16px; display: flex; flex-direction: column; gap: 4px; min-width: 0; }
          .kpi-label { font-size: 12px; color: var(--text-muted); font-weight: 600; text-transform: uppercase; letter-spacing: 0.04em; }
          .kpi-value { font-size: 24px; font-weight: 700; }
          .kpi-warn { color: #b45309; }
          .kpi-ok { color: #15803d; }
          .kpi-scope { font-size: 15px; font-weight: 600; color: var(--text-main); word-break: break-word; }
          .quarantine-filters { display: flex; flex-wrap: wrap; gap: 12px; padding: 14px 16px; border-bottom: 1px solid var(--border-subtle); background: #f8fafc; }
          .quarantine-filters .form-field { flex: 1 1 180px; min-width: 0; max-width: 100%; }
          .quarantine-search-field { flex: 2 1 200px; min-width: 0; max-width: 100%; }
          .table-responsive { width: 100%; max-width: 100%; overflow-x: auto; -webkit-overflow-scrolling: touch; }
          .entity-cell { display: flex; flex-direction: column; gap: 2px; }
          .entity-type-label { font-weight: 600; color: var(--text-main); }
          .entity-meta { font-size: 12px; color: var(--text-muted); }
          .raw-value-chip { background: #f1f5f9; padding: 2px 6px; border-radius: 4px; font-size: 12px; }
          .badge-warning { background: #fef3c7; color: #92400e; display: inline-flex; align-items: center; gap: 4px; padding: 3px 8px; border-radius: 999px; font-size: 12px; font-weight: 600; }
          .flag-list { display: flex; flex-wrap: wrap; gap: 4px; }
          .flag-pill { background: #e0e7ff; color: #3730a3; padding: 2px 6px; border-radius: 4px; font-size: 11px; font-weight: 600; }
          .reason-cell { max-width: 320px; font-size: 13px; color: var(--text-main); }
          .error-banner { margin: 16px; padding: 12px 16px; border-radius: var(--radius-sm); background: #fef2f2; color: #991b1b; border: 1px solid #fecaca; display: flex; flex-wrap: wrap; align-items: center; gap: 10px; justify-content: space-between; }
          .quarantine-pagination { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; padding: 12px 16px; border-top: 1px solid var(--border-subtle); }
          .pagination-meta { font-size: 13px; color: var(--text-muted); }
          .pagination-actions { display: flex; gap: 8px; flex-wrap: wrap; }
        `}</style>
      </div>
    </PermissionGuard>
  );
};
