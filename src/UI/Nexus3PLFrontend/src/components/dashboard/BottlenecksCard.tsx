import React from 'react';
import { FacilityBottleneckAnalysisDto, DockYardOperationalStatusDto } from '../../types/controlTower';
import { AppIcon } from '../common/AppIcon';

export interface BottlenecksCardProps {
  bottlenecks: FacilityBottleneckAnalysisDto | null;
  loadingBottlenecks: boolean;
  bottlenecksError: string | null;
  dockYard: DockYardOperationalStatusDto | null;
  loadingDockYard: boolean;
  dockYardError: string | null;
  isRestricted: boolean;
  onRetryBottlenecks?: () => void;
  onRetryDockYard?: () => void;
}

export const BottlenecksCard: React.FC<BottlenecksCardProps> = ({
  bottlenecks,
  loadingBottlenecks,
  bottlenecksError,
  dockYard,
  loadingDockYard,
  dockYardError,
  isRestricted,
  onRetryBottlenecks,
  onRetryDockYard,
}) => {
  if (isRestricted) {
    return null;
  }

  // If both are done loading and both failed with no data, show a combined retry banner
  if (!loadingBottlenecks && !loadingDockYard && !bottlenecks && !dockYard) {
    if (bottlenecksError || dockYardError) {
      return (
        <div className="bottlenecks-card" role="region" aria-label="Operational Flow Diagnostics Error" style={{ borderColor: 'var(--status-danger-border)', background: 'var(--status-danger-bg)' }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: '10px' }}>
            <div>
              <h4 style={{ color: 'var(--status-danger-text)', margin: '0 0 4px 0', display: 'flex', alignItems: 'center', gap: '6px' }}>
                <AppIcon name="AlertTriangle" size="xs" color="danger" />
                Auxiliary Telemetry Unavailable
              </h4>
              <p style={{ color: 'var(--status-danger-text)', margin: 0, fontSize: '13px' }}>
                {bottlenecksError || dockYardError}
              </p>
            </div>
            <div style={{ display: 'flex', gap: '8px' }}>
              {onRetryBottlenecks && (
                <button type="button" className="btn btn-secondary" onClick={onRetryBottlenecks} style={{ fontSize: '12px', padding: '6px 12px' }}>
                  <AppIcon name="RefreshCw" size="xs" /> Retry Telemetry
                </button>
              )}
            </div>
          </div>
        </div>
      );
    }
    return null;
  }

  // If both are initial loading
  if (loadingBottlenecks && loadingDockYard && !bottlenecks && !dockYard) {
    return (
      <div className="bottlenecks-card" aria-label="Loading Bottleneck Diagnostics">
        <div className="skeleton-line" style={{ width: '30%', marginBottom: '14px' }} />
        <div className="bottlenecks-stages-grid">
          {[1, 2, 3, 4].map((i) => (
            <div key={i} className="stage-pill Normal">
              <div className="skeleton-line" style={{ width: '50%' }} />
              <div className="skeleton-line" style={{ width: '80%' }} />
            </div>
          ))}
        </div>
      </div>
    );
  }

  const stages = bottlenecks ? [
    { label: 'Receiving Inflow', data: bottlenecks.receivingCongestion },
    { label: 'Picking Backlog', data: bottlenecks.pickingBacklog },
    { label: 'Packing Buffer', data: bottlenecks.packingDelays },
    { label: 'Dispatch Staging', data: bottlenecks.dispatchDelays },
  ] : [];

  return (
    <div className="bottlenecks-card" role="region" aria-label="Operational Flow Diagnostics" data-testid="bottlenecks-card">
      <div className="bottlenecks-header">
        <h3 className="bottlenecks-title">
          <AppIcon name="Activity" size="sm" color="primary" />
          <span>Flow Velocity & Bottleneck Diagnostics</span>
        </h3>
        {bottlenecks && (
          <span
            className={`badge ${bottlenecks.overallHealthScore >= 90 ? 'badge-success' : bottlenecks.overallHealthScore >= 70 ? 'badge-warning' : 'badge-danger'}`}
            style={{ fontSize: '12px' }}
          >
            Facility Health: {bottlenecks.overallHealthScore}/100
          </span>
        )}
      </div>

      {loadingBottlenecks && (
        <div className="bottlenecks-stages-grid">
          {[1, 2, 3, 4].map((i) => (
            <div key={i} className="stage-pill Normal">
              <div className="skeleton-line" style={{ width: '50%' }} />
              <div className="skeleton-line" style={{ width: '80%' }} />
            </div>
          ))}
        </div>
      )}

      {bottlenecksError && !loadingBottlenecks && (
        <div style={{ padding: '12px', background: '#fef2f2', border: '1px dashed #fca5a5', borderRadius: 'var(--radius-sm)', marginBottom: '14px', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
          <span style={{ fontSize: '12.5px', color: '#991b1b' }}>
            Bottleneck diagnostics error: {bottlenecksError}
          </span>
          {onRetryBottlenecks && (
            <button type="button" className="btn btn-secondary" onClick={onRetryBottlenecks} style={{ fontSize: '11.5px', padding: '4px 10px' }}>
              Retry
            </button>
          )}
        </div>
      )}

      {bottlenecks && !loadingBottlenecks && (
        <div className="bottlenecks-stages-grid">
          {stages.map((st, idx) => {
            const sev = st.data?.severity || 'Normal';
            return (
              <div key={idx} className={`stage-pill ${sev}`}>
                <span className="stage-name">{st.label}</span>
                <div className="stage-status-row">
                  <span className="stage-status-label">{sev}</span>
                  <span className="stage-queue">
                    Queue: <strong>{st.data?.queueDepth ?? 0}</strong>
                  </span>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {loadingDockYard && (
        <div style={{ paddingTop: '12px', borderTop: '1px solid var(--border-subtle)' }}>
          <div className="skeleton-line" style={{ width: '70%' }} />
        </div>
      )}

      {dockYardError && !loadingDockYard && (
        <div style={{ paddingTop: '10px', borderTop: '1px solid var(--border-subtle)', display: 'flex', alignItems: 'center', justifyContent: 'space-between', fontSize: '12px', color: '#b91c1c' }}>
          <span>Dock & Yard telemetry: {dockYardError}</span>
          {onRetryDockYard && (
            <button type="button" className="btn btn-secondary" onClick={onRetryDockYard} style={{ fontSize: '11px', padding: '3px 8px' }}>
              Retry Yard
            </button>
          )}
        </div>
      )}

      {dockYard && !loadingDockYard && (
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            paddingTop: '12px',
            borderTop: '1px solid var(--border-subtle)',
            fontSize: '12.5px',
            color: 'var(--text-muted)',
            flexWrap: 'wrap',
            gap: '12px',
          }}
        >
          <span>
            Trucks at Dock: <strong style={{ color: 'var(--text-main)' }}>{dockYard.trucksAtDockCount}</strong>
          </span>
          <span>
            Waiting in Yard: <strong style={{ color: 'var(--text-main)' }}>{dockYard.trucksWaitingInYardCount}</strong>
          </span>
          <span>
            Yard Spots Occupied: <strong style={{ color: 'var(--text-main)' }}>{dockYard.occupiedYardSpotsCount} / {dockYard.totalYardSpotsCount} ({dockYard.yardOccupancyPercent}%)</strong>
          </span>
          <span>
            Avg Turn Time: <strong style={{ color: 'var(--text-main)' }}>{dockYard.averageTotalTurnTimeMinutes}m</strong>
          </span>
        </div>
      )}
    </div>
  );
};
