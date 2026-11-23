import React from 'react';
import { useAuth } from '../../context/AuthContext';
import { Link } from '../../context/RouterContext';
import { UserType, normalizeUserTypeStrict } from '../../types/auth';
import { AppIcon, AppIconName } from '../common/AppIcon';

interface LaunchpadItem {
  id: string;
  title: string;
  description: string;
  path: string;
  icon: AppIconName;
  permission?: string;
  permissions?: string[];
  allowedUserTypes?: UserType[];
  badge?: string;
}

export const LaunchpadGrid: React.FC = () => {
  const { hasPermission, hasAnyPermission, isSuperAdmin, user } = useAuth();

  const items: LaunchpadItem[] = [
    {
      id: 'outbound-orders',
      title: 'Outbound Orders & Allocation',
      description: 'Order entry, bulk import, stock availability checks, and FIFO/FEFO inventory reservations.',
      path: '/app/outbound/orders',
      icon: 'Send',
      permissions: ['outbound:orders:view', 'outbound:orders:create'],
      allowedUserTypes: [UserType.Staff, UserType.Client],
      badge: 'Fulfillment',
    },
    {
      id: 'pick-waves',
      title: 'Pick Wave Planning',
      description: 'Group allocated orders into discrete, batch, zone, or bulk pick waves and release to floor.',
      path: '/app/outbound/waves',
      icon: 'Layers',
      permissions: ['outbound:waves:view', 'outbound:waves:manage'],
      allowedUserTypes: [UserType.Staff],
      badge: 'Wave Planner',
    },
    {
      id: 'putaway',
      title: 'Directed Putaway RF',
      description: 'Mobile-first barcode scanning, staging verification, and directed shelf confirmation.',
      path: '/app/inbound/putaway',
      icon: 'PackageCheck',
      permission: 'inbound:putaway:execute',
      badge: 'RF Floor',
    },
    {
      id: 'receiving',
      title: 'Inbound Receiving',
      description: 'Dock arrival check-in, PO and ASN reconciliation, LPN generation, and discrepancy intake.',
      path: '/app/inbound',
      icon: 'ArrowDownToLine',
      permissions: ['inbound:view', 'inbound:receive', 'inbound:asn:view'],
      badge: 'Dock Hub',
    },
    {
      id: 'inventory',
      title: 'Stock on Hand & Ledger',
      description: 'Real-time double-entry perpetual inventory balances, lot aging, and stock reservations.',
      path: '/app/inventory',
      icon: 'Boxes',
      permission: 'inventory:view',
      badge: 'Perpetual',
    },
    {
      id: 'clients',
      title: 'Client Tenants',
      description: 'Multi-tenant partition accounts, SLA tier configurations, and billing currencies.',
      path: '/app/admin/clients',
      icon: 'Building',
      permission: 'clients:view',
      badge: 'Metadata Pilot',
    },
    {
      id: 'facilities',
      title: 'Physical Facilities',
      description: 'Physical warehouse nodes, dock doors, staging zones, and rack topography.',
      path: '/app/admin/facilities',
      icon: 'Warehouse',
      permission: 'facilities:view',
    },
    {
      id: 'users',
      title: 'Users & Identities',
      description: 'Staff and client portal user credentials, active statuses, and facility assignments.',
      path: '/app/admin/users',
      icon: 'Users',
      permission: 'users:view',
    },
    {
      id: 'roles',
      title: 'Roles & RBAC Matrix',
      description: 'Fine-grained permission assignments, claim scopes, and role hierarchy definitions.',
      path: '/app/admin/roles',
      icon: 'ShieldCheck',
      permission: 'roles:view',
    },
  ];

  const authorizedItems = items.filter((item) => {
    if (item.allowedUserTypes && item.allowedUserTypes.length > 0) {
      const userType = user ? normalizeUserTypeStrict(user.userType) : null;
      if (!userType || !item.allowedUserTypes.includes(userType)) {
        return false;
      }
    }
    if (isSuperAdmin) return true;
    if (item.permission) return hasPermission(item.permission);
    if (item.permissions) return hasAnyPermission(item.permissions);
    return true;
  });

  if (authorizedItems.length === 0) {
    return null;
  }

  return (
    <section className="launchpad-section" aria-label="Operations & Administration Launchpad">
      <h3 className="launchpad-title">Operations & Administration Launchpad</h3>
      <div className="launchpad-grid">
        {authorizedItems.map((item) => (
          <Link
            key={item.id}
            to={item.path}
            className="launchpad-card"
            data-testid={`launchpad-${item.id}`}
          >
            <div className="launchpad-icon" aria-hidden="true">
              <AppIcon name={item.icon} size="md" color="primary" />
            </div>
            <div className="launchpad-body">
              <div className="launchpad-card-title">
                <span>{item.title}</span>
                {item.badge && (
                  <span className="badge badge-info" style={{ fontSize: '10.5px' }}>
                    {item.badge}
                  </span>
                )}
              </div>
              <p className="launchpad-card-desc">{item.description}</p>
            </div>
          </Link>
        ))}
      </div>
    </section>
  );
};
