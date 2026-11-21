import React, { useState, useEffect } from 'react';
import { FacilityDto, CurrentUserDto } from '../../types/auth';
import { AppIcon } from '../common/AppIcon';

export type TelemetryFreshnessStatus = 'live' | 'refreshing' | 'stale' | 'error' | 'restricted';

export interface ControlTowerHeaderProps {
  user: CurrentUserDto | null;
  activeFacility: FacilityDto | null;
  onRefresh: () => void;
  isRefreshing: boolean;
  lastSuccessfulSync: Date | null;
  backendAsOfUtc: string | null;
  telemetryStatus: TelemetryFreshnessStatus;
  autoRefresh: boolean;
  onToggleAutoRefresh: () => void;
}

export const ControlTowerHeader: React.FC<ControlTowerHeaderProps> = ({
  user,
  activeFacility,
  onRefresh,
  isRefreshing,
  lastSuccessfulSync,
  backendAsOfUtc,
  telemetryStatus,
  autoRefresh,
  onToggleAutoRefresh,
}) => {
  const [currentTime, setCurrentTime] = useState<string>('');

  useEffect(() => {
    const updateTime = () => {
      const now = new Date();
      setCurrentTime(
        now.toISOString().substring(11, 19) + ' UTC'
      );
    };
    updateTime();
    const interval = setInterval(updateTime, 1000);
    return () => clearInterval(interval);
  }, []);

  const renderFreshnessBadge = () => {
    switch (telemetryStatus) {
      case 'refreshing':
        return (
          <span className="badge badge-info" style={{ fontSize: '11px', display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
            <AppIcon name="RefreshCw" size="xs" color="info" className="spinning" />
            <span>Refreshing Snapshot...</span>
          </span>
        );
      case 'live':
        return (
          <span className="badge badge-success" style={{ fontSize: '11px', display: 'inline-flex', alignItems: 'center', gap: '5px' }}>
            <span className="live-pulse-dot" aria-hidden="true" />
            <span>Live Snapshot</span>
          </span>
        );
      case 'stale':
        return (
          <span className="badge badge-warning" style={{ fontSize: '11px', display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
            <AppIcon name="Clock" size="xs" color="warning" />
            <span>Snapshot Stale (Sync Required)</span>
          </span>
        );
      case 'error':
        return (
          <span className="badge badge-danger" style={{ fontSize: '11px', display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
            <AppIcon name="AlertTriangle" size="xs" color="danger" />
            <span>Telemetry Offline</span>
          </span>
        );
      case 'restricted':
        return (
          <span className="badge badge-info" style={{ fontSize: '11px', display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
            <AppIcon name="Lock" size="xs" color="info" />
            <span>Operational Mode</span>
          </span>
        );
    }
  };

  return (
    <div className="control-tower-header" role="banner" aria-label="Control Tower Operational Telemetry">
      <div className="ct-header-left">
        <div className="ct-eyebrow">
          <span>Warehouse Operations Telemetry</span>
          {renderFreshnessBadge()}
        </div>
        <h1 className="ct-title">
          Enterprise Control Tower
        </h1>
        {user && (
          <h2 className="banner-title" style={{ fontSize: '15.5px', fontWeight: 600, color: '#e2e8f0', margin: '2px 0 6px 0' }}>
            Welcome back, {user.firstName} {user.lastName}
          </h2>
        )}
        <div className="ct-facility-meta">
          <span>
            Active Facility: <strong>{activeFacility ? `${activeFacility.facilityCode} — ${activeFacility.name}` : 'Default Hub'}</strong>
          </span>
          {activeFacility?.timezone && (
            <span>
              Timezone: <strong>{activeFacility.timezone}</strong>
            </span>
          )}
          {user?.companyName && (
            <span>
              Tenant: <strong>{user.companyName}</strong>
            </span>
          )}
          {backendAsOfUtc && (
            <span style={{ color: '#cbd5e1' }}>
              Backend As-Of: <strong>{new Date(backendAsOfUtc).toISOString().substring(11, 19)} UTC</strong>
            </span>
          )}
          {lastSuccessfulSync ? (
            <span style={{ color: '#94a3b8', fontSize: '12px' }}>
              Last Synchronized: {lastSuccessfulSync.toLocaleTimeString()}
            </span>
          ) : (
            <span style={{ color: '#f87171', fontSize: '12px' }}>
              Snapshot Not Synchronized
            </span>
          )}
        </div>
      </div>

      <div className="ct-header-right">
        <div className="ct-clock-widget" aria-label="Current Operating Time">
          <span className="ct-clock-label">OPERATING SHIFT CLOCK</span>
          <span className="ct-clock-time">{currentTime || '--:--:-- UTC'}</span>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <button
            type="button"
            className="ct-refresh-btn"
            onClick={onToggleAutoRefresh}
            title={autoRefresh ? 'Disable automatic polling' : 'Enable 60s automatic polling'}
            aria-label={autoRefresh ? 'Disable 60s auto-refresh' : 'Enable 60s auto-refresh'}
            style={{ fontSize: '12px' }}
          >
            <AppIcon name="Clock" size="xs" color="inverse" />
            <span>Auto: {autoRefresh ? '60s' : 'Off'}</span>
          </button>

          <button
            type="button"
            className="ct-refresh-btn"
            onClick={onRefresh}
            disabled={isRefreshing}
            aria-label="Refresh operational metrics"
          >
            <AppIcon name="RefreshCw" size="xs" color="inverse" className={isRefreshing ? 'spinning' : ''} />
            <span>{isRefreshing ? 'Updating...' : 'Sync Now'}</span>
          </button>
        </div>
      </div>
    </div>
  );
};
