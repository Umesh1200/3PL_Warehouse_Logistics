import React from 'react';
import { useAuth } from '../context/AuthContext';
import { useFacility } from '../context/FacilityContext';
import { Link } from '../context/RouterContext';

export const DashboardPage: React.FC = () => {
  const { user, hasPermission } = useAuth();
  const { activeFacility } = useFacility();

  return (
    <div className="dashboard-page">
      <div className="welcome-banner">
        <div className="banner-content">
          <span className="banner-eyebrow">ENTERPRISE WAREHOUSE LOGISTICS PORTAL</span>
          <h2 className="banner-title">
            Welcome back, {user?.firstName} {user?.lastName}
          </h2>
          <p className="banner-desc">
            Active Workspace: <strong>{activeFacility ? `${activeFacility.facilityCode} — ${activeFacility.name}` : 'Default Hub'}</strong>{' '}
            {user?.companyName ? `(${user.companyName})` : ''}
          </p>
        </div>
      </div>

      <div className="quick-nav-grid">
        {hasPermission('users:view') && (
          <Link to="/app/admin/users" className="nav-card">
            <div className="nav-card-title">Users Directory</div>
            <p className="nav-card-desc">
              Inspect user identities, security statuses, and configure role assignments.
            </p>
            <span className="nav-card-link">Open Users &rarr;</span>
          </Link>
        )}

        {hasPermission('roles:view') && (
          <Link to="/app/admin/roles" className="nav-card">
            <div className="nav-card-title">Roles & Access Control</div>
            <p className="nav-card-desc">
              Review system roles and update fine-grained permission assignments.
            </p>
            <span className="nav-card-link">Configure Roles &rarr;</span>
          </Link>
        )}

        {hasPermission('clients:view') && (
          <Link to="/app/admin/clients" className="nav-card">
            <div className="nav-card-title">Client Tenants</div>
            <p className="nav-card-desc">
              Manage multi-tenant accounts, billing currencies, and SLA tiers.
            </p>
            <span className="nav-card-link">View Clients &rarr;</span>
          </Link>
        )}

        {hasPermission('facilities:view') && (
          <Link to="/app/admin/facilities" className="nav-card">
            <div className="nav-card-title">Authorized Facilities</div>
            <p className="nav-card-desc">
              Explore authorized physical warehouses and switch active facility scope.
            </p>
            <span className="nav-card-link">Manage Facilities &rarr;</span>
          </Link>
        )}
      </div>

      <style>{`
        .dashboard-page {
          display: flex;
          flex-direction: column;
          gap: 24px;
        }
        .welcome-banner {
          background: linear-gradient(135deg, #0f172a 0%, #1e293b 100%);
          border-radius: var(--radius-md);
          padding: 28px 32px;
          color: #ffffff;
          box-shadow: 0 4px 16px rgba(15, 23, 42, 0.1);
        }
        .banner-eyebrow {
          font-size: 11px;
          font-weight: 750;
          letter-spacing: 0.08em;
          color: #94a3b8;
          display: block;
          margin-bottom: 6px;
        }
        .banner-title {
          font-size: 24px;
          font-weight: 750;
          margin-bottom: 8px;
        }
        .banner-desc {
          font-size: 14.5px;
          color: #cbd5e1;
          margin: 0;
        }
        .banner-desc strong {
          color: #ffffff;
        }
        .quick-nav-grid {
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
          gap: 20px;
        }
        .nav-card {
          background: #ffffff;
          border: 1px solid var(--border-subtle);
          border-radius: var(--radius-md);
          padding: 24px;
          text-decoration: none;
          color: inherit;
          display: flex;
          flex-direction: column;
          transition: transform var(--transition-fast), box-shadow var(--transition-fast), border-color var(--transition-fast);
        }
        .nav-card:hover {
          transform: translateY(-2px);
          box-shadow: 0 8px 24px rgba(15, 23, 42, 0.08);
          border-color: var(--brand-primary);
        }
        .nav-card-title {
          font-size: 16px;
          font-weight: 750;
          color: var(--text-main);
          margin-bottom: 8px;
        }
        .nav-card-desc {
          font-size: 13.5px;
          color: var(--text-muted);
          line-height: 1.5;
          flex: 1;
          margin-bottom: 16px;
        }
        .nav-card-link {
          font-size: 13px;
          font-weight: 700;
          color: var(--brand-primary);
        }
      `}</style>
    </div>
  );
};
