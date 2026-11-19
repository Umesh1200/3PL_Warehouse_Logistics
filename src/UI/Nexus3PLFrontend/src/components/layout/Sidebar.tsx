import React from 'react';
import { useAuth } from '../../context/AuthContext';
import { useRouter, Link } from '../../context/RouterContext';
import logoMark from '../../assets/logo-mark.svg';

interface NavItem {
  label: string;
  path: string;
  permission?: string;
  permissions?: string[];
  icon?: string;
}

interface NavSection {
  title: string;
  items: NavItem[];
}

export const Sidebar: React.FC = () => {
  const { hasPermission, hasAnyPermission, user } = useAuth();
  const { path } = useRouter();

  const sections: NavSection[] = [
    {
      title: 'INTELLIGENCE & OVERSIGHT',
      items: [
        {
          label: 'Control Tower',
          path: '/app/dashboard',
          permission: 'analytics:control-tower:view',
        },
      ],
    },
    {
      title: 'OPERATIONAL WORKFLOWS',
      items: [
        {
          label: 'Inbound & Receiving',
          path: '/app/inbound',
          permissions: ['inbound:view', 'inbound:asn:view', 'inbound:receive'],
        },
        {
          label: 'Inventory Balances',
          path: '/app/inventory',
          permission: 'inventory:view',
        },
        {
          label: 'Outbound & Fulfillment',
          path: '/app/outbound',
          permissions: ['outbound:orders:view', 'outbound:ship'],
        },
        {
          label: 'Transportation & Carriers',
          path: '/app/transportation',
          permissions: ['dispatch:view', 'analytics:transportation:view'],
        },
        {
          label: 'Billing & Invoicing',
          path: '/app/billing',
          permissions: ['billing:view', 'billing:manage-rates'],
        },
      ],
    },
    {
      title: 'ADMINISTRATION & RBAC',
      items: [
        {
          label: 'Users Directory',
          path: '/app/admin/users',
          permission: 'users:view',
        },
        {
          label: 'Roles & Permissions',
          path: '/app/admin/roles',
          permission: 'roles:view',
        },
        {
          label: 'Client Tenants',
          path: '/app/admin/clients',
          permission: 'clients:view',
        },
        {
          label: 'Facilities Overview',
          path: '/app/admin/facilities',
          permission: 'facilities:view',
        },
      ],
    },
  ];

  const isItemVisible = (item: NavItem): boolean => {
    if (!item.permission && !item.permissions) return true;
    if (user?.roles.some((r) => r.toLowerCase() === 'superadmin')) return true;
    if (item.permission && hasPermission(item.permission)) return true;
    if (item.permissions && hasAnyPermission(item.permissions)) return true;
    return false;
  };

  return (
    <aside className="app-sidebar" aria-label="Enterprise Navigation">
      <div className="sidebar-brand">
        <Link to="/app/dashboard" className="brand-lockup">
          <img src={logoMark} alt="Nexus3PL" className="brand-icon" />
          <div className="brand-text">
            <span className="brand-name">NEXUS<span className="brand-accent">3PL</span></span>
            <span className="brand-sub">ENTERPRISE PORTAL</span>
          </div>
        </Link>
      </div>

      <nav className="sidebar-nav">
        {sections.map((section, idx) => {
          const visibleItems = section.items.filter(isItemVisible);
          if (visibleItems.length === 0) return null;

          return (
            <div key={idx} className="nav-section">
              <div className="section-title">{section.title}</div>
              <ul className="nav-list">
                {visibleItems.map((item) => {
                  const isActive = path === item.path || path.startsWith(`${item.path}/`);
                  return (
                    <li key={item.path} className="nav-item">
                      <Link
                        to={item.path}
                        className={`nav-link ${isActive ? 'active' : ''}`}
                        aria-current={isActive ? 'page' : undefined}
                      >
                        <span className="nav-indicator" />
                        <span className="nav-label">{item.label}</span>
                      </Link>
                    </li>
                  );
                })}
              </ul>
            </div>
          );
        })}
      </nav>

      <div className="sidebar-footer">
        <div className="system-pill">
          <span className="status-dot" />
          <span className="system-text">API Online &bull; v1.0</span>
        </div>
      </div>

      <style>{`
        .app-sidebar {
          width: 260px;
          min-width: 260px;
          background: #0f172a;
          color: #cbd5e1;
          display: flex;
          flex-direction: column;
          height: 100vh;
          position: sticky;
          top: 0;
          border-right: 1px solid #1e293b;
          box-sizing: border-box;
          user-select: none;
        }
        .sidebar-brand {
          padding: 20px 18px;
          border-bottom: 1px solid #1e293b;
        }
        .brand-lockup {
          display: flex;
          align-items: center;
          gap: 12px;
          text-decoration: none;
        }
        .brand-icon {
          height: 32px;
          width: 32px;
        }
        .brand-text {
          display: flex;
          flex-direction: column;
        }
        .brand-name {
          font-size: 17px;
          font-weight: 800;
          letter-spacing: -0.02em;
          color: #ffffff;
        }
        .brand-accent {
          color: #5254e6;
          margin-left: 2px;
        }
        .brand-sub {
          font-size: 9.5px;
          font-weight: 700;
          letter-spacing: 0.1em;
          color: #94a3b8;
        }
        .sidebar-nav {
          flex: 1;
          overflow-y: auto;
          padding: 16px 10px;
        }
        .nav-section {
          margin-bottom: 22px;
        }
        .section-title {
          font-size: 10.5px;
          font-weight: 750;
          color: #64748b;
          letter-spacing: 0.08em;
          padding: 6px 12px;
          text-transform: uppercase;
        }
        .nav-list {
          list-style: none;
          padding: 0;
          margin: 4px 0 0 0;
          display: flex;
          flex-direction: column;
          gap: 3px;
        }
        .nav-link {
          display: flex;
          align-items: center;
          gap: 10px;
          padding: 8px 12px;
          border-radius: var(--radius-sm);
          color: #94a3b8;
          text-decoration: none;
          font-size: 13.5px;
          font-weight: 600;
          transition: background var(--transition-fast), color var(--transition-fast);
        }
        .nav-link:hover {
          background: #1e293b;
          color: #ffffff;
        }
        .nav-link.active {
          background: #1e293b;
          color: #ffffff;
          border-left: 3px solid #5254e6;
        }
        .nav-indicator {
          width: 6px;
          height: 6px;
          border-radius: 50%;
          background: #475569;
          transition: background var(--transition-fast);
        }
        .nav-link.active .nav-indicator {
          background: #5254e6;
          box-shadow: 0 0 8px rgba(82, 84, 230, 0.6);
        }
        .nav-label {
          flex: 1;
        }
        .sidebar-footer {
          padding: 14px 18px;
          border-top: 1px solid #1e293b;
        }
        .system-pill {
          display: flex;
          align-items: center;
          gap: 8px;
          font-size: 11.5px;
          color: #64748b;
        }
        .status-dot {
          width: 8px;
          height: 8px;
          border-radius: 50%;
          background: #10b981;
        }
      `}</style>
    </aside>
  );
};
