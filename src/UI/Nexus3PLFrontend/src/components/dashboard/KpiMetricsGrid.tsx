import React from 'react';
import { ControlTowerSummaryDto } from '../../types/controlTower';
import { AppIcon } from '../common/AppIcon';

export interface KpiMetricsGridProps {
  summary: ControlTowerSummaryDto | null;
  isLoading: boolean;
  isRestricted: boolean;
  error: string | null;
  onRetry?: () => void;
}

export const KpiMetricsGrid: React.FC<KpiMetricsGridProps> = ({
  summary,
  isLoading,
  isRestricted,
  error,
  onRetry,
}) => {
  if (isRestricted) {
    return (
      <div className="kpi-permission-banner" role="region" aria-label="KPI Metrics Permission Required">
        <AppIcon name="ShieldCheck" size="lg" color="muted" />
        <h4>Executive Analytics Restricted</h4>
        <p>
          High-level warehouse throughput and capacity KPIs require <code>analytics:control-tower:view</code> authorization.
          Floor execution queues and assigned tasks remain fully accessible below.
        </p>
      </div>
    );
  }

  if (error) {
    return (
      <div className="kpi-permission-banner" role="region" aria-label="KPI Metrics Load Error" style={{ borderColor: 'var(--status-danger-border)', background: 'var(--status-danger-bg)' }}>
        <AppIcon name="AlertTriangle" size="lg" color="danger" />
        <h4 style={{ color: 'var(--status-danger-text)' }}>Failed to load warehouse metrics</h4>
        <p style={{ color: 'var(--status-danger-text)' }}>{error}</p>
        {onRetry && (
          <button type="button" className="btn btn-secondary" onClick={onRetry} style={{ marginTop: '8px' }}>
            <AppIcon name="RefreshCw" size="xs" /> Retry Metrics
          </button>
        )}
      </div>
    );
  }

  if (isLoading || !summary) {
    return (
      <div className="kpi-grid" aria-label="Loading KPI Metrics">
        {[1, 2, 3, 4, 5, 6].map((i) => (
          <div key={i} className="kpi-card">
            <div className="kpi-card-header">
              <div className="skeleton-line" style={{ width: '40%' }} />
              <div className="skeleton-line" style={{ width: '36px', height: '36px', borderRadius: 'var(--radius-sm)' }} />
            </div>
            <div className="skeleton-line skeleton-kpi" />
            <div className="skeleton-line" style={{ width: '70%' }} />
          </div>
        ))}
      </div>
    );
  }

  return (
    <div className="kpi-grid" role="region" aria-label="Operational KPI Metrics">
      {/* 1. Inbound Pallets */}
      <div className="kpi-card" data-testid="kpi-inbound-pallets">
        <div className="kpi-card-header">
          <span className="kpi-label">Inbound Pallets</span>
          <div className="kpi-icon-pill primary" aria-hidden="true">
            <AppIcon name="ArrowDownToLine" size="sm" color="primary" />
          </div>
        </div>
        <div className="kpi-value-row">
          <span className="kpi-value">{summary.inboundPalletsReceivedToday.toLocaleString()}</span>
          <span className="kpi-unit">pallets</span>
        </div>
        <p className="kpi-subtitle">
          {summary.returnsInspectedToday > 0
            ? `${summary.returnsInspectedToday} returns inspected today`
            : 'Receiving docks active today'}
        </p>
      </div>

      {/* 2. Putaway Completed */}
      <div className="kpi-card" data-testid="kpi-putaway-completed">
        <div className="kpi-card-header">
          <span className="kpi-label">Directed Putaway</span>
          <div className="kpi-icon-pill success" aria-hidden="true">
            <AppIcon name="PackageCheck" size="sm" color="success" />
          </div>
        </div>
        <div className="kpi-value-row">
          <span className="kpi-value">{summary.putawayCompletedToday.toLocaleString()}</span>
          <span className="kpi-unit">tasks</span>
        </div>
        <p className="kpi-subtitle">Completed and confirmed to primary racks</p>
      </div>

      {/* 3. Units Picked & Cartons Packed */}
      <div className="kpi-card" data-testid="kpi-units-picked">
        <div className="kpi-card-header">
          <span className="kpi-label">Fulfillment Picking</span>
          <div className="kpi-icon-pill info" aria-hidden="true">
            <AppIcon name="Boxes" size="sm" color="info" />
          </div>
        </div>
        <div className="kpi-value-row">
          <span className="kpi-value">{summary.unitsPickedToday.toLocaleString()}</span>
          <span className="kpi-unit">units</span>
        </div>
        <p className="kpi-subtitle">
          {summary.cartonsPackedToday.toLocaleString()} cartons packed
        </p>
      </div>

      {/* 4. Outbound Orders Dispatched */}
      <div className="kpi-card" data-testid="kpi-orders-dispatched">
        <div className="kpi-card-header">
          <span className="kpi-label">Outbound Dispatch</span>
          <div className="kpi-icon-pill primary" aria-hidden="true">
            <AppIcon name="Truck" size="sm" color="primary" />
          </div>
        </div>
        <div className="kpi-value-row">
          <span className="kpi-value">{summary.ordersDispatchedToday.toLocaleString()}</span>
          <span className="kpi-unit">orders</span>
        </div>
        <p className="kpi-subtitle">
          {summary.packagesDispatchedToday.toLocaleString()} packages carrier-manifested
        </p>
      </div>

      {/* 5. Productivity Velocity */}
      <div className="kpi-card" data-testid="kpi-labor-velocity">
        <div className="kpi-card-header">
          <span className="kpi-label">Labor Velocity</span>
          <div className="kpi-icon-pill success" aria-hidden="true">
            <AppIcon name="TrendingUp" size="sm" color="success" />
          </div>
        </div>
        <div className="kpi-value-row">
          <span className="kpi-value">{summary.unitsPerLaborHourToday}</span>
          <span className="kpi-unit">UPH</span>
        </div>
        <p className="kpi-subtitle">
          Avg cycle time: {summary.averageFulfillmentCycleTimeHours}h ({summary.sameDayDispatchAdherencePercent}% same-day)
        </p>
      </div>

      {/* 6. Capacity & Cube Fill */}
      <div className="kpi-card" data-testid="kpi-pallet-occupancy">
        <div className="kpi-card-header">
          <span className="kpi-label">Rack Utilization</span>
          <div className="kpi-icon-pill warning" aria-hidden="true">
            <AppIcon name="Warehouse" size="sm" color="warning" />
          </div>
        </div>
        <div className="kpi-value-row">
          <span className="kpi-value">{summary.palletPositionOccupancyPercent}%</span>
          <span className="kpi-unit">positions</span>
        </div>
        <p className="kpi-subtitle">
          Volumetric cube fill: {summary.volumetricCubeFillPercent}%
        </p>
      </div>
    </div>
  );
};
