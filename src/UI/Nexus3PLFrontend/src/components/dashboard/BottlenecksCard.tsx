import React from 'react';
import { FacilityBottleneckAnalysisDto, DockYardOperationalStatusDto } from '../../types/controlTower';
import { AppIcon } from '../common/AppIcon';

export interface BottlenecksCardProps {
  bottlenecks: FacilityBottleneckAnalysisDto | null;
  dockYard: DockYardOperationalStatusDto | null;
  isLoading: boolean;
  isRestricted: boolean;
}

export const BottlenecksCard: React.FC<BottlenecksCardProps> = ({
  bottlenecks,
  dockYard,
  isLoading,
  isRestricted,
}) => {
  if (isRestricted || (!isLoading && !bottlenecks && !dockYard)) {
    return null;
  }

  if (isLoading || !bottlenecks) {
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

  const stages = [
    { label: 'Receiving Inflow', data: bottlenecks.receivingCongestion },
    { label: 'Picking Backlog', data: bottlenecks.pickingBacklog },
    { label: 'Packing Buffer', data: bottlenecks.packingDelays },
    { label: 'Dispatch Staging', data: bottlenecks.dispatchDelays },
  ];

  return (
    <div className="bottlenecks-card" role="region" aria-label="Operational Flow Diagnostics" data-testid="bottlenecks-card">
      <div className="bottlenecks-header">
        <h3 className="bottlenecks-title">
          <AppIcon name="Activity" size="sm" color="primary" />
          <span>Flow Velocity & Bottleneck Diagnostics</span>
        </h3>
        <span
          className={`badge ${bottlenecks.overallHealthScore >= 90 ? 'badge-success' : bottlenecks.overallHealthScore >= 70 ? 'badge-warning' : 'badge-danger'}`}
          style={{ fontSize: '12px' }}
        >
          Facility Health: {bottlenecks.overallHealthScore}/100
        </span>
      </div>

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

      {dockYard && (
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
