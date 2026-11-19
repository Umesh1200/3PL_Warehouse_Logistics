import React from 'react';
import { useAuth } from '../../context/AuthContext';
import { useFacility } from '../../context/FacilityContext';
import { useRouter } from '../../context/RouterContext';

export const Header: React.FC = () => {
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
        {/* Facility Selector Scoped by Tenant */}
        <div className="facility-picker" aria-label="Active Warehouse Facility">
          <span className="picker-label">ACTIVE WAREHOUSE:</span>
          {isLoadingFacilities ? (
            <span className="facility-loading">Loading facilities...</span>
          ) : facilities.length > 0 ? (
            <div className="select-wrapper">
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
            <span className="tenant-icon">&bull;</span>
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
          Sign Out
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
          display: flex;
          align-items: center;
          gap: 6px;
        }
        .facility-select {
          height: 36px;
          padding: 4px 10px;
          border: 1px solid var(--border-actionable);
          border-radius: var(--radius-xs);
          background: #ffffff;
          color: var(--text-main);
          font-size: 13px;
          font-weight: 650;
          cursor: pointer;
          outline: none;
        }
        .facility-select:focus {
          border-color: var(--brand-primary);
          box-shadow: 0 0 0 2px rgba(82, 84, 230, 0.15);
        }
        .facility-tz {
          font-size: 11px;
          background: #f1f5f9;
          color: #475569;
          padding: 2px 6px;
          border-radius: var(--radius-xs);
          font-weight: 600;
        }
        .facility-empty, .facility-loading {
          font-size: 13px;
          color: var(--text-muted);
          font-style: italic;
        }
        .header-right {
          display: flex;
          align-items: center;
          gap: 18px;
        }
        .tenant-tag {
          display: flex;
          align-items: center;
          gap: 6px;
          background: #f8fafc;
          border: 1px solid var(--border-subtle);
          padding: 4px 10px;
          border-radius: var(--radius-xs);
          font-size: 12.5px;
          font-weight: 650;
          color: var(--text-main);
        }
        .tenant-icon {
          color: var(--brand-accent);
          font-size: 16px;
        }
        .user-profile-badge {
          display: flex;
          align-items: center;
          gap: 10px;
        }
        .avatar-circle {
          width: 36px;
          height: 36px;
          border-radius: 50%;
          background: var(--brand-primary-light);
          color: var(--brand-primary);
          display: flex;
          align-items: center;
          justify-content: center;
          font-weight: 750;
          font-size: 13.5px;
          border: 1px solid rgba(82, 84, 230, 0.2);
        }
        .user-info {
          display: flex;
          flex-direction: column;
          line-height: 1.25;
        }
        .user-name {
          font-size: 13.5px;
          font-weight: 700;
          color: var(--text-main);
        }
        .role-pill {
          font-size: 10.5px;
          font-weight: 700;
          color: #4338ca;
          background: #e0e7ff;
          padding: 1px 6px;
          border-radius: 10px;
          align-self: flex-start;
          margin-top: 2px;
        }
        .btn-signout {
          background: none;
          border: 1px solid var(--border-actionable);
          border-radius: var(--radius-xs);
          padding: 6px 12px;
          font-size: 12.5px;
          font-weight: 650;
          color: var(--text-main);
          cursor: pointer;
          transition: background var(--transition-fast), color var(--transition-fast);
        }
        .btn-signout:hover {
          background: #ffe0db;
          border-color: #b02a37;
          color: #b02a37;
        }
      `}</style>
    </header>
  );
};
