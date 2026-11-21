import React from 'react';
import { useAuth } from '../../context/AuthContext';
import { useFacility } from '../../context/FacilityContext';
import { useRouter } from '../../context/RouterContext';
import { AppIcon } from '../common/AppIcon';

export interface HeaderProps {
  onToggleNav?: () => void;
}

export const Header: React.FC<HeaderProps> = ({ onToggleNav }) => {
  const { user, logout } = useAuth();
  const { facilities, activeFacility, setActiveFacility, isLoadingFacilities } = useFacility();
  const { navigate } = useRouter();

  const handleFacilityChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const selected = facilities.find((f) => f.id === e.target.value);
    if (selected) {
      setActiveFacility(selected);
    }
  };

  const handleSignOut = () => {
    logout();
    navigate('/login', { replace: true });
  };

  const primaryRole = user?.roles && user.roles.length > 0 ? user.roles[0] : 'User';

  return (
    <header className="app-header">
      <div className="header-left">
        {onToggleNav && (
          <button
            type="button"
            className="mobile-nav-toggle"
            onClick={onToggleNav}
            aria-label="Toggle navigation drawer"
            data-testid="mobile-nav-toggle"
          >
            <AppIcon name="Menu" size="md" color="muted" />
          </button>
        )}

        {/* Facility Selector Scoped by Tenant */}
        <div className="facility-picker" aria-label="Active Warehouse Facility">
          <span className="picker-label">ACTIVE WAREHOUSE:</span>
          {isLoadingFacilities ? (
            <span className="facility-loading">Loading facilities...</span>
          ) : facilities.length > 0 ? (
            <div className="select-wrapper">
              <span className="facility-status-dot" aria-hidden="true" />
              <select
                className="facility-select"
                value={activeFacility?.id || ''}
                onChange={handleFacilityChange}
                aria-label="Select Active Warehouse Facility"
              >
                {facilities.map((fac) => (
                  <option key={fac.id} value={fac.id}>
                    {fac.facilityCode} &mdash; {fac.name}
                  </option>
                ))}
              </select>
              {activeFacility && (
                <span className="facility-tz" title={`Timezone: ${activeFacility.timezone}`}>
                  {activeFacility.timezone}
                </span>
              )}
            </div>
          ) : (
            <span className="facility-empty">No Authorized Facilities</span>
          )}
        </div>
      </div>

      <div className="header-right">
        {/* Tenant Organization Tag */}
        {user?.companyName && (
          <div className="tenant-tag" title="Client Tenant Partition">
            <AppIcon name="Building" size="xs" color="primary" />
            <span className="tenant-name">{user.companyName}</span>
          </div>
        )}

        {/* User Identity & Roles */}
        <div className="user-profile-badge">
          <div className="avatar-circle">
            {user ? `${user.firstName?.[0] || 'U'}${user.lastName?.[0] || ''}` : 'U'}
          </div>
          <div className="user-info">
            <span className="user-name">{user ? `${user.firstName} ${user.lastName}` : 'User'}</span>
            <span className="role-pill">{primaryRole}</span>
          </div>
        </div>

        {/* Sign Out Button */}
        <button
          type="button"
          className="btn-signout"
          onClick={handleSignOut}
          aria-label="Sign out of operations portal"
        >
          <AppIcon name="LogOut" size="xs" />
          <span className="signout-text">Sign Out</span>
        </button>
      </div>

      <style>{`
        .app-header {
          height: 64px;
          background: #ffffff;
          border-bottom: 1px solid var(--border-subtle);
          display: flex;
          align-items: center;
          justify-content: space-between;
          padding: 0 24px;
          position: sticky;
          top: 0;
          z-index: 100;
          box-shadow: 0 1px 3px rgba(15, 23, 42, 0.04);
        }
        .header-left {
          display: flex;
          align-items: center;
          gap: 16px;
        }
        .mobile-nav-toggle {
          display: none;
          background: none;
          border: none;
          padding: 8px;
          cursor: pointer;
          border-radius: var(--radius-xs);
          color: var(--text-main);
        }
        .mobile-nav-toggle:hover {
          background: #f1f5f9;
        }
        .facility-picker {
          display: flex;
          align-items: center;
          gap: 8px;
          font-size: 13px;
        }
        .picker-label {
          font-size: 11px;
          font-weight: 750;
          color: var(--text-muted);
          letter-spacing: 0.05em;
        }
        .select-wrapper {
          position: relative;
          display: flex;
          align-items: center;
          gap: 8px;
        }
        .facility-status-dot {
          width: 8px;
          height: 8px;
          border-radius: 50%;
          background: #10b981;
          box-shadow: 0 0 6px rgba(16, 185, 129, 0.4);
          flex-shrink: 0;
        }
        .facility-select {
          padding: 6px 12px;
          border-radius: var(--radius-sm);
          border: 1px solid var(--border-actionable);
          font-size: 13px;
          font-weight: 600;
          color: var(--text-main);
          background: #ffffff;
          cursor: pointer;
        }
        .facility-select:focus {
          outline: none;
          border-color: var(--brand-primary);
          box-shadow: 0 0 0 2px rgba(82, 84, 230, 0.2);
        }
        .facility-tz {
          font-size: 11px;
          font-weight: 650;
          color: var(--text-muted);
          background: #f1f5f9;
          padding: 3px 6px;
          border-radius: var(--radius-xs);
        }
        .header-right {
          display: flex;
          align-items: center;
          gap: 16px;
        }
        .tenant-tag {
          display: flex;
          align-items: center;
          gap: 6px;
          font-size: 12px;
          font-weight: 650;
          color: var(--brand-primary);
          background: var(--brand-primary-light);
          padding: 4px 10px;
          border-radius: var(--radius-full);
          border: 1px solid #c7c9f8;
        }
        .user-profile-badge {
          display: flex;
          align-items: center;
          gap: 10px;
        }
        .avatar-circle {
          width: 34px;
          height: 34px;
          border-radius: 50%;
          background: #1e293b;
          color: #ffffff;
          display: flex;
          align-items: center;
          justify-content: center;
          font-size: 12.5px;
          font-weight: 700;
        }
        .user-info {
          display: flex;
          flex-direction: column;
        }
        .user-name {
          font-size: 13px;
          font-weight: 650;
          color: var(--text-main);
          line-height: 1.2;
        }
        .role-pill {
          font-size: 10.5px;
          font-weight: 700;
          color: var(--brand-primary);
          text-transform: uppercase;
          letter-spacing: 0.04em;
        }
        .btn-signout {
          display: inline-flex;
          align-items: center;
          gap: 6px;
          padding: 6px 12px;
          border: 1px solid var(--border-actionable);
          border-radius: var(--radius-sm);
          font-size: 12.5px;
          font-weight: 600;
          color: var(--text-muted);
          background: #ffffff;
        }
        .btn-signout:hover {
          color: var(--status-danger-text);
          border-color: var(--status-danger-border);
          background: var(--status-danger-bg);
        }
        @media (max-width: 768px) {
          .mobile-nav-toggle {
            display: flex;
          }
          .app-header {
            padding: 0 16px;
          }
          .picker-label, .facility-tz, .tenant-tag, .role-pill, .signout-text {
            display: none;
          }
        }
      `}</style>
    </header>
  );
};
