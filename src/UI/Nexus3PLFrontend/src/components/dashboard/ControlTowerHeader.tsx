import React, { useState, useEffect } from 'react';
import { FacilityDto, CurrentUserDto } from '../../types/auth';
import { AppIcon } from '../common/AppIcon';

export interface ControlTowerHeaderProps {
  user: CurrentUserDto | null;
  activeFacility: FacilityDto | null;
  onRefresh: () => void;
  isRefreshing: boolean;
  lastUpdated: Date | null;
}

export const ControlTowerHeader: React.FC<ControlTowerHeaderProps> = ({
  user,
  activeFacility,
  onRefresh,
  isRefreshing,
  lastUpdated,
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

  return (
    <div className="control-tower-header" role="banner" aria-label="Control Tower Operational Telemetry">
      <div className="ct-header-left">
        <div className="ct-eyebrow">
          <span className="live-pulse-dot" aria-hidden="true" />
          <span>Real-Time Warehouse Telemetry</span>
          <span className="badge badge-success" style={{ textTransform: 'uppercase', fontSize: '10px', padding: '2px 8px' }}>
            Live
          </span>
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
          {lastUpdated && (
            <span style={{ color: '#94a3b8', fontSize: '12px' }}>
              Synchronized: {lastUpdated.toLocaleTimeString()}
            </span>
          )}
        </div>
      </div>

      <div className="ct-header-right">
        <div className="ct-clock-widget" aria-label="Current Operating Time">
          <span className="ct-clock-label">OPERATING SHIFT CLOCK</span>
          <span className="ct-clock-time">{currentTime || '--:--:-- UTC'}</span>
        </div>

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
  );
};
