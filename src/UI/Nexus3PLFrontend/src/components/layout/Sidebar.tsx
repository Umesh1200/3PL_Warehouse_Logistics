import React, { useEffect } from 'react';
import { useAuth } from '../../context/AuthContext';
import { useRouter, Link } from '../../context/RouterContext';
import { UserType, normalizeUserTypeStrict } from '../../types/auth';
import logoMark from '../../assets/logo-mark.svg';
import { AppIcon, AppIconName } from '../common/AppIcon';

interface NavItem {
  label: string;
  path: string;
  activePaths?: string[];
  permission?: string;
  permissions?: string[];
  allowedUserTypes?: UserType[];
  icon: AppIconName;
}

interface NavSection {
  title: string;
  items: NavItem[];
}

export interface SidebarProps {
  isOpen?: boolean;
  onClose?: () => void;
}

export const Sidebar: React.FC<SidebarProps> = ({ isOpen = false, onClose }) => {
  const { hasPermission, hasAnyPermission, isSuperAdmin, user } = useAuth();
  const { path } = useRouter();

  // Escape key listener to close mobile sidebar drawer
  useEffect(() => {
    if (!isOpen || !onClose) return;

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose();
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isOpen, onClose]);

  const sections: NavSection[] = [
    {
      title: 'INTELLIGENCE & OVERSIGHT',
      items: [
        {
          label: 'Control Tower',
          path: '/app/dashboard',
          permission: 'analytics:control-tower:view',
          icon: 'LayoutDashboard',
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
          icon: 'ArrowDownToLine',
        },
        {
          label: 'Directed Putaway',
          path: '/app/inbound/putaway',
          permissions: ['inbound:putaway', 'inbound:putaway:execute', 'inbound:putaway:manage', 'inbound:view'],
          icon: 'PackageCheck',
        },
        {
          label: 'Inventory Balances',
          path: '/app/inventory',
          permission: 'inventory:view',
          icon: 'Boxes',
        },
        {
          label: 'Outbound Orders',
          path: '/app/outbound/orders',
          activePaths: ['/app/outbound', '/app/outbound/orders'],
          permissions: ['outbound:orders:view', 'outbound:orders:create'],
          allowedUserTypes: [UserType.Staff, UserType.Client],
          icon: 'Send',
        },
        {
          label: 'Pick Waves',
          path: '/app/outbound/waves',
          permissions: ['outbound:waves:view', 'outbound:waves:manage'],
          allowedUserTypes: [UserType.Staff],
          icon: 'Layers',
        },
        {
          label: 'Directed Picking',
          path: '/app/outbound/picking',
          permissions: ['outbound:tasks:view', 'outbound:tasks:manage', 'outbound:pick'],
          allowedUserTypes: [UserType.Staff],
          icon: 'Barcode',
        },
        {
          label: 'Transportation & Carriers',
          path: '/app/transportation',
          permissions: ['dispatch:view', 'analytics:transportation:view'],
          icon: 'Truck',
        },
        {
          label: 'Billing & Invoicing',
          path: '/app/billing',
          permissions: ['billing:view', 'billing:manage-rates'],
          icon: 'Receipt',
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
          icon: 'Users',
        },
        {
          label: 'Roles & Permissions',
          path: '/app/admin/roles',
          permission: 'roles:view',
          icon: 'ShieldCheck',
        },
        {
          label: 'Client Tenants',
          path: '/app/admin/clients',
          permission: 'clients:view',
          icon: 'Building',
        },
        {
          label: 'Facilities Overview',
          path: '/app/admin/facilities',
          permission: 'facilities:view',
          icon: 'Building2',
        },
      ],
    },
  ];

  const isItemVisible = (item: NavItem): boolean => {
    if (item.allowedUserTypes && item.allowedUserTypes.length > 0) {
      const userType = user ? normalizeUserTypeStrict(user.userType) : null;
      if (!userType || !item.allowedUserTypes.includes(userType)) {
        return false;
      }
    }
    if (!item.permission && !item.permissions) return true;
    if (isSuperAdmin) return true;
    if (item.permission && hasPermission(item.permission)) return true;
    if (item.permissions && hasAnyPermission(item.permissions)) return true;
    return false;
  };

  return (
    <>
      {/* Mobile Backdrop Overlay */}
      {isOpen && (
        <div
          className="sidebar-backdrop"
          onClick={onClose}
          role="presentation"
          aria-hidden="true"
        />
      )}

      <aside
        className={`app-sidebar ${isOpen ? 'mobile-open' : ''}`}
        aria-label="Enterprise Navigation"
      >
        <div className="sidebar-brand">
          <Link to="/app/dashboard" className="brand-lockup" onClick={onClose}>
            <img src={logoMark} alt="Nexus3PL" className="brand-icon" />
            <div className="brand-text">
              <span className="brand-name">
                NEXUS<span className="brand-accent">3PL</span>
              </span>
              <span className="brand-sub">ENTERPRISE PORTAL</span>
            </div>
          </Link>
          {onClose && (
            <button
              type="button"
              className="sidebar-mobile-close"
              onClick={onClose}
              aria-label="Close navigation menu"
            >
              <AppIcon name="X" size="sm" color="muted" />
            </button>
          )}
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
                    const isActive =
                      path === item.path ||
                      path.startsWith(`${item.path}/`) ||
                      Boolean(item.activePaths?.includes(path));
                    return (
                      <li key={item.path} className="nav-item">
                        <Link
                          to={item.path}
                          className={`nav-link ${isActive ? 'active' : ''}`}
                          aria-current={isActive ? 'page' : undefined}
                          onClick={onClose}
                        >
                          <AppIcon
                            name={item.icon}
                            size="sm"
                            className={`nav-item-icon ${isActive ? 'icon-active' : ''}`}
                          />
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
            <span>Nexus3PL Enterprise WMS</span>
          </div>
        </div>

        <style>{`
          .sidebar-backdrop {
            position: fixed;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background: rgba(15, 23, 42, 0.6);
            backdrop-filter: blur(2px);
            z-index: 998;
            animation: fadeIn 0.15s ease-out;
          }
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
            z-index: 999;
            transition: transform var(--transition-normal);
          }
          .sidebar-brand {
            padding: 20px 18px;
            border-bottom: 1px solid #1e293b;
            display: flex;
            align-items: center;
            justify-content: space-between;
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
          .sidebar-mobile-close {
            display: none;
            background: none;
            border: none;
            color: #94a3b8;
            padding: 6px;
            cursor: pointer;
            border-radius: var(--radius-xs);
          }
          .sidebar-mobile-close:hover {
            color: #ffffff;
            background: #1e293b;
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
            gap: 12px;
            padding: 9px 12px;
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
          .nav-item-icon {
            flex-shrink: 0;
            color: #64748b;
            transition: color var(--transition-fast);
          }
          .nav-link:hover .nav-item-icon,
          .nav-link.active .nav-item-icon {
            color: #5254e6;
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
          @media (max-width: 768px) {
            .app-sidebar {
              position: fixed;
              top: 0;
              left: 0;
              bottom: 0;
              transform: translateX(-100%);
              box-shadow: 0 0 30px rgba(0, 0, 0, 0.4);
            }
            .app-sidebar.mobile-open {
              transform: translateX(0);
            }
            .sidebar-mobile-close {
              display: flex;
            }
          }
        `}</style>
      </aside>
    </>
  );
};
