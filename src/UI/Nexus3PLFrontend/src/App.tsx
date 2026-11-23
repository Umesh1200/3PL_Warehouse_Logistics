import React from 'react';
import { RouterProvider, useRouter } from './context/RouterContext';
import { AuthProvider, useAuth } from './context/AuthContext';
import { FacilityProvider } from './context/FacilityContext';
import { LandingPage } from './pages/LandingPage';
import { LoginPage } from './pages/auth/LoginPage';
import { AppShell } from './components/layout/AppShell';
import { ProtectedRoute } from './components/auth/ProtectedRoute';
import { DashboardPage } from './pages/DashboardPage';
import { UsersPage } from './pages/admin/UsersPage';
import { RolesPage } from './pages/admin/RolesPage';
import { ClientsPage } from './pages/admin/ClientsPage';
import { FacilitiesPage } from './pages/admin/FacilitiesPage';
import { ReceivingPage } from './pages/inbound/ReceivingPage';
import { PutawayPage } from './pages/inbound/PutawayPage';
import { StockOnHandPage } from './pages/inventory/StockOnHandPage';
import { OutboundOrdersPage } from './pages/outbound/OutboundOrdersPage';
import { PickWavesPage } from './pages/outbound/PickWavesPage';
import './styles/base.css';

const AppRoutes: React.FC = () => {
  const { path } = useRouter();

  if (path === '/' || path === '') {
    return <LandingPage />;
  }

  if (path === '/login') {
    return <LoginPage />;
  }

  if (path.startsWith('/app')) {
    let content: React.ReactNode = <DashboardPage />;
    let pageTitle = 'Operations Dashboard & Control Tower';
    let breadcrumb = 'INTELLIGENCE & OVERSIGHT / CONTROL TOWER';

    if (path === '/app/admin/users') {
      content = <UsersPage />;
      pageTitle = 'Users & Security Identities';
      breadcrumb = 'ADMINISTRATION & RBAC / USERS';
    } else if (path === '/app/admin/roles') {
      content = <RolesPage />;
      pageTitle = 'Roles & Fine-Grained Permissions';
      breadcrumb = 'ADMINISTRATION & RBAC / ROLES';
    } else if (path === '/app/admin/clients') {
      content = <ClientsPage />;
      pageTitle = 'Client Tenants & Multi-Tenancy';
      breadcrumb = 'ADMINISTRATION & RBAC / CLIENTS';
    } else if (path === '/app/admin/facilities') {
      content = <FacilitiesPage />;
      pageTitle = 'Authorized Warehouse Facilities';
      breadcrumb = 'ADMINISTRATION & RBAC / FACILITIES';
    } else if (path === '/app/inbound' || path === '/app/inbound/receiving') {
      content = <ReceivingPage />;
      pageTitle = 'Inbound Goods Receiving';
      breadcrumb = 'OPERATIONAL WORKFLOWS / INBOUND RECEIVING';
    } else if (path === '/app/inbound/putaway') {
      content = <PutawayPage />;
      pageTitle = 'Directed Putaway & Relocation';
      breadcrumb = 'OPERATIONAL WORKFLOWS / DIRECTED PUTAWAY';
    } else if (path === '/app/inventory' || path === '/app/inventory/stock') {
      content = <StockOnHandPage />;
      pageTitle = 'Stock on Hand & Inventory Ledger';
      breadcrumb = 'OPERATIONAL WORKFLOWS / INVENTORY BALANCES';
    } else if (path === '/app/outbound' || path === '/app/outbound/orders') {
      content = <OutboundOrdersPage />;
      pageTitle = 'Outbound Orders & Allocation';
      breadcrumb = 'OPERATIONAL WORKFLOWS / OUTBOUND ORDERS';
    } else if (path === '/app/outbound/waves') {
      content = <PickWavesPage />;
      pageTitle = 'Pick Wave Planning & Release';
      breadcrumb = 'OPERATIONAL WORKFLOWS / PICK WAVES';
    } else if (
      path === '/app/transportation' ||
      path === '/app/billing'
    ) {
      content = (
        <div className="card-placeholder" role="region" aria-label="Workflow module placeholder">
          <h3>Workflow Integration Pending</h3>
          <p>
            This module will be connected to live operational endpoints during Subphases 7.3 &ndash; 7.5.
          </p>
          <style>{`
            .card-placeholder {
              background: #ffffff;
              padding: 40px;
              border-radius: var(--radius-md);
              border: 1px solid var(--border-subtle);
              text-align: center;
              color: var(--text-muted);
            }
            .card-placeholder h3 {
              color: var(--text-main);
              margin-bottom: 8px;
            }
          `}</style>
        </div>
      );
      pageTitle = 'Operational Module';
      breadcrumb = 'OPERATIONAL WORKFLOWS';
    }

    return (
      <ProtectedRoute>
        <AppShell pageTitle={pageTitle} breadcrumb={breadcrumb}>
          {content}
        </AppShell>
      </ProtectedRoute>
    );
  }

  // Fallback to landing page for unmatched route
  return <LandingPage />;
};

export const App: React.FC = () => {
  return (
    <RouterProvider>
      <AuthProvider>
        <FacilityProvider>
          <AppRoutes />
        </FacilityProvider>
      </AuthProvider>
    </RouterProvider>
  );
};
