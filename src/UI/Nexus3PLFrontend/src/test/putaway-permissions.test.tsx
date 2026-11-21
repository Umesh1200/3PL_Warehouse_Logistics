import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { AuthProvider } from '../context/AuthContext';
import { FacilityProvider } from '../context/FacilityContext';
import { RouterProvider } from '../context/RouterContext';
import { DashboardPage } from '../pages/DashboardPage';
import { PutawayPage } from '../pages/inbound/PutawayPage';
import { tokenManager } from '../services/apiClient';
import { PutawayTaskStatus, PutawayPriority } from '../types/inbound';

describe('Directed Putaway & Control Tower RF Permission Matrix Tests', () => {
  const mockFacility = {
    id: 'fac-chi-01',
    facilityCode: 'FAC-CHI1',
    name: 'Chicago Central Hub',
    addressLine1: '100 Logistics Blvd',
    city: 'Chicago',
    state: 'IL',
    postalCode: '60601',
    country: 'USA',
    timezone: 'America/Chicago',
    isActive: true,
  };

  const mockTask = {
    id: 'ptw-task-1',
    facilityId: 'fac-chi-01',
    facilityCode: 'FAC-CHI1',
    receiptLineId: 'line-1',
    lpnCode: 'LPN-MATRIX-01',
    sku: 'SKU-ELEC-01',
    itemDescription: 'Electronic Component',
    quantity: 25,
    sourceLocationCode: 'STAGE-01',
    sourceLocationBarcode: 'LOC-STAGE-01',
    suggestedLocationCode: 'RCK-01-A',
    suggestedLocationBarcode: 'LOC-RCK-01-A',
    status: PutawayTaskStatus.Pending,
    priority: PutawayPriority.Normal,
    createdAtUtc: new Date().toISOString(),
    assignedUserId: null,
    actualLocationCode: null,
    completedAtUtc: null,
    version: 1,
  };

  const createMockUser = (permissions: string[], roles: string[] = ['WarehouseStaff']) => ({
    userId: 'usr-test-matrix',
    email: 'test@nexus3pl.com',
    firstName: 'Matrix',
    lastName: 'User',
    userType: 1,
    tenantId: null,
    companyName: 'Nexus3PL',
    roles,
    permissions,
  });

  const setupMockFetch = (userProfile: any) => {
    const recordedCalls: string[] = [];

    const fetchSpy = vi.fn().mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
      const method = init?.method || 'GET';
      recordedCalls.push(`${method} ${url}`);

      // Auth /me
      if (url.includes('/api/v1/auth/me')) {
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () => Promise.resolve(userProfile),
        } as Response);
      }

      // Facilities
      if (url.includes('/api/v1/facilities')) {
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () => Promise.resolve([mockFacility]),
        } as Response);
      }

      // Control tower summary
      if (url.includes('/api/v1/control-tower/summary')) {
        const canView = userProfile.permissions.includes('analytics:control-tower:view') ||
          userProfile.roles.some((r: string) => r.toLowerCase() === 'superadmin');
        if (!canView) {
          return Promise.resolve({ ok: false, status: 403, json: () => Promise.resolve({ error: 'Forbidden' }) } as Response);
        }
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () => Promise.resolve({
            facilityId: 'fac-chi-01',
            facilityCode: 'FAC-CHI1',
            asOfUtc: new Date().toISOString(),
            ordersReceivedToday: 120,
            ordersDispatchedToday: 95,
            packagesDispatchedToday: 210,
            unitsPickedToday: 1450,
            cartonsPackedToday: 320,
            inboundPalletsReceivedToday: 14,
            putawayCompletedToday: 11,
            returnsInspectedToday: 2,
            unitsPerLaborHourToday: 42.5,
            laborVelocityUnitsPerHour: 42.5,
            averageFulfillmentCycleTimeHours: 1.8,
            sameDayDispatchAdherencePercent: 98.5,
            totalBilledRevenueToday: 14500,
            netFreightMarginToday: 2300,
            onTimeDeliveryRatePercent: 99.2,
            activeDeliveryExceptionsCount: 1,
            palletPositionOccupancyPercent: 78.4,
            rackOccupancyPercentage: 78.4,
            volumetricCubeFillPercent: 64.2,
          }),
        } as Response);
      }

      // Bottlenecks
      if (url.includes('/api/v1/control-tower/bottlenecks')) {
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () => Promise.resolve({
            facilityId: 'fac-chi-01',
            facilityHealthScore: 90,
            stages: [],
          }),
        } as Response);
      }

      // Dock Yard
      if (url.includes('/api/v1/control-tower/dock-yard')) {
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () => Promise.resolve({
            facilityId: 'fac-chi-01',
            totalDockDoors: 10,
            occupiedDockDoors: 3,
            doorsInReceiving: 2,
            doorsInShipping: 1,
            averageTurnTimeMinutes: 45,
            activeYardTrailers: 4,
          }),
        } as Response);
      }

      // Putaway Task list
      if (url.includes('/api/v1/putaway/tasks') && !url.includes('/next') && !url.includes('/generate') && !url.includes('/confirm') && !url.includes('/exception')) {
        const canView = userProfile.permissions.includes('inbound:view') ||
          userProfile.roles.some((r: string) => r.toLowerCase() === 'superadmin');
        if (!canView) {
          return Promise.resolve({ ok: false, status: 403, json: () => Promise.resolve({ error: 'Forbidden' }) } as Response);
        }
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () => Promise.resolve({
            items: [mockTask],
            totalCount: 1,
            pageNumber: 1,
            pageSize: 10,
          }),
        } as Response);
      }

      // Putaway Task next
      if (url.includes('/api/v1/putaway/tasks/next')) {
        const canExec = userProfile.permissions.includes('inbound:putaway:execute') ||
          userProfile.roles.some((r: string) => r.toLowerCase() === 'superadmin');
        if (!canExec) {
          return Promise.resolve({ ok: false, status: 403, json: () => Promise.resolve({ error: 'Forbidden' }) } as Response);
        }
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () => Promise.resolve(mockTask),
        } as Response);
      }

      // Putaway Task generate
      if (url.includes('/api/v1/putaway/tasks/generate')) {
        const canManage = userProfile.permissions.includes('inbound:putaway:manage') ||
          userProfile.roles.some((r: string) => r.toLowerCase() === 'superadmin');
        if (!canManage) {
          return Promise.resolve({ ok: false, status: 403, json: () => Promise.resolve({ error: 'Forbidden' }) } as Response);
        }
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () => Promise.resolve([mockTask]),
        } as Response);
      }

      // Receiving sessions
      if (url.includes('/api/v1/receiving/sessions')) {
        const canView = userProfile.permissions.includes('inbound:view') ||
          userProfile.roles.some((r: string) => r.toLowerCase() === 'superadmin');
        if (!canView) {
          return Promise.resolve({ ok: false, status: 403, json: () => Promise.resolve({ error: 'Forbidden' }) } as Response);
        }
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () => Promise.resolve({
            items: [],
            totalCount: 0,
            pageNumber: 1,
            pageSize: 10,
          }),
        } as Response);
      }

      return Promise.resolve({ ok: false, status: 404 } as Response);
    });

    vi.stubGlobal('fetch', fetchSpy);
    return { recordedCalls };
  };

  beforeEach(() => {
    tokenManager.clearSession();
    tokenManager.setTokens('valid-jwt-token', 'valid-refresh-token');
    tokenManager.setActiveFacilityId(mockFacility.id);
    vi.restoreAllMocks();
  });

  afterEach(() => {
    tokenManager.clearSession();
    vi.restoreAllMocks();
  });

  // 1. VIEW-ONLY USER
  describe('View-Only User (has inbound:view only)', () => {
    const viewOnlyUser = createMockUser(['inbound:view', 'analytics:control-tower:view']);

    it('in Dashboard: views queue without seeing RF launcher link or execution launchpad item', async () => {
      const { recordedCalls } = setupMockFetch(viewOnlyUser);

      render(
        <RouterProvider initialPath="/app">
          <AuthProvider>
            <FacilityProvider>
              <DashboardPage />
            </FacilityProvider>
          </AuthProvider>
        </RouterProvider>
      );

      // Queue list successfully renders
      await waitFor(() => {
        expect(screen.getByText('LPN-MATRIX-01')).toBeDefined();
        expect(screen.getByText('SKU-ELEC-01')).toBeDefined();
      });

      // Does NOT render "Launch RF Scanner" link or "Launch RF Putaway Scanner" button
      expect(screen.queryByRole('link', { name: /Launch RF Scanner/i })).toBeNull();
      expect(screen.queryByRole('button', { name: /Launch RF Putaway Scanner/i })).toBeNull();

      // Launchpad does NOT render the execution-specific "Directed Putaway RF" card
      expect(screen.queryByTestId('launchpad-putaway')).toBeNull();

      // No execution endpoints called
      const nextCalls = recordedCalls.filter(c => c.includes('/api/v1/putaway/tasks/next'));
      expect(nextCalls.length).toBe(0);
    });

    it('in PutawayPage: defaults to task queue, hides RF tab and generate button, never invokes /next', async () => {
      const { recordedCalls } = setupMockFetch(viewOnlyUser);

      render(
        <RouterProvider initialPath="/app/inbound/putaway">
          <AuthProvider>
            <FacilityProvider>
              <PutawayPage />
            </FacilityProvider>
          </AuthProvider>
        </RouterProvider>
      );

      // Queue view renders
      await waitFor(() => {
        expect(screen.getByTestId('putaway-queue-view')).toBeDefined();
        expect(screen.getByText('LPN-MATRIX-01')).toBeDefined();
      });

      // tab-queue is present, but tab-rf is NOT present
      expect(screen.getByTestId('tab-queue')).toBeDefined();
      expect(screen.queryByTestId('tab-rf')).toBeNull();

      // "Generate Directed Tasks" button is NOT rendered
      expect(screen.queryByTestId('generate-tasks-btn')).toBeNull();

      // RF execution endpoint was never invoked
      const nextCalls = recordedCalls.filter(c => c.includes('/api/v1/putaway/tasks/next'));
      expect(nextCalls.length).toBe(0);
    });
  });

  // 2. MANAGE-ONLY USER
  describe('Manage-Only User (has inbound:putaway:manage only)', () => {
    const manageOnlyUser = createMockUser(['inbound:putaway:manage']);

    it('in Dashboard: queue listing restricted, cannot see RF launcher or RF launchpad item', async () => {
      const { recordedCalls } = setupMockFetch(manageOnlyUser);

      render(
        <RouterProvider initialPath="/app">
          <AuthProvider>
            <FacilityProvider>
              <DashboardPage />
            </FacilityProvider>
          </AuthProvider>
        </RouterProvider>
      );

      await waitFor(() => {
        const notices = screen.getAllByText(/Queue listing requires inbound:view authorization/i);
        expect(notices.length).toBeGreaterThan(0);
      });

      // Does NOT render "Launch RF Putaway Scanner" button because user lacks inbound:putaway:execute
      expect(screen.queryByRole('button', { name: /Launch RF Putaway Scanner/i })).toBeNull();
      expect(screen.queryByRole('link', { name: /Launch RF Scanner/i })).toBeNull();

      // Launchpad does NOT render the execution-specific "Directed Putaway RF" card
      expect(screen.queryByTestId('launchpad-putaway')).toBeNull();

      // No list or execution calls
      const taskCalls = recordedCalls.filter(c => c.includes('/api/v1/putaway/tasks'));
      expect(taskCalls.length).toBe(0);
    });

    it('in PutawayPage: renders generate button, hides RF tab, never invokes /next', async () => {
      const { recordedCalls } = setupMockFetch(manageOnlyUser);

      render(
        <RouterProvider initialPath="/app/inbound/putaway">
          <AuthProvider>
            <FacilityProvider>
              <PutawayPage />
            </FacilityProvider>
          </AuthProvider>
        </RouterProvider>
      );

      // "Generate Directed Tasks" button IS rendered for management
      await waitFor(() => {
        expect(screen.getByTestId('generate-tasks-btn')).toBeDefined();
      });

      // tab-rf is NOT rendered because user lacks execution permission
      expect(screen.queryByTestId('tab-rf')).toBeNull();
      // tab-queue is NOT rendered because user lacks inbound:view
      expect(screen.queryByTestId('tab-queue')).toBeNull();

      // Never invokes /next
      const nextCalls = recordedCalls.filter(c => c.includes('/api/v1/putaway/tasks/next'));
      expect(nextCalls.length).toBe(0);
    });
  });

  // 3. EXECUTE-ONLY USER
  describe('Execute-Only User (has inbound:putaway:execute only)', () => {
    const executeOnlyUser = createMockUser(['inbound:putaway:execute']);

    it('in Dashboard: sees restricted queue notice, sees RF button, sees launchpad RF card', async () => {
      setupMockFetch(executeOnlyUser);

      render(
        <RouterProvider initialPath="/app">
          <AuthProvider>
            <FacilityProvider>
              <DashboardPage />
            </FacilityProvider>
          </AuthProvider>
        </RouterProvider>
      );

      await waitFor(() => {
        const notices = screen.getAllByText(/Queue listing requires inbound:view authorization/i);
        expect(notices.length).toBeGreaterThan(0);
      });

      // Renders "Launch RF Putaway Scanner" button for execution-authorized operator
      expect(screen.getByRole('button', { name: /Launch RF Putaway Scanner/i })).toBeDefined();

      // Launchpad renders "Directed Putaway RF" card
      expect(screen.getByTestId('launchpad-putaway')).toBeDefined();
    });

    it('in PutawayPage: defaults directly to RF mode, never lists tasks, executes /next', async () => {
      const { recordedCalls } = setupMockFetch(executeOnlyUser);

      render(
        <RouterProvider initialPath="/app/inbound/putaway">
          <AuthProvider>
            <FacilityProvider>
              <PutawayPage />
            </FacilityProvider>
          </AuthProvider>
        </RouterProvider>
      );

      // Defaults directly to RF execution view
      await waitFor(() => {
        expect(screen.getByTestId('rf-execution-view')).toBeDefined();
        expect(screen.getByTestId('dual-scan-form')).toBeDefined();
        expect(screen.getByText('LPN-MATRIX-01')).toBeDefined();
      });

      // Queue tab and generate tasks button are NOT rendered
      expect(screen.queryByTestId('tab-queue')).toBeNull();
      expect(screen.queryByTestId('generate-tasks-btn')).toBeNull();

      // Assert zero calls to task list query
      const listCalls = recordedCalls.filter(c => c.includes('/api/v1/putaway/tasks') && !c.includes('/next'));
      expect(listCalls.length).toBe(0);

      // Assert /next was invoked
      const nextCalls = recordedCalls.filter(c => c.includes('/api/v1/putaway/tasks/next'));
      expect(nextCalls.length).toBeGreaterThanOrEqual(1);
    });
  });

  // 4. SUPERADMIN USER
  describe('SuperAdmin User (has SuperAdmin role)', () => {
    const superAdminUser = createMockUser([], ['SuperAdmin']);

    it('in Dashboard: has full access to queue and RF launchers', async () => {
      setupMockFetch(superAdminUser);

      render(
        <RouterProvider initialPath="/app">
          <AuthProvider>
            <FacilityProvider>
              <DashboardPage />
            </FacilityProvider>
          </AuthProvider>
        </RouterProvider>
      );

      await waitFor(() => {
        expect(screen.getByText('LPN-MATRIX-01')).toBeDefined();
      });

      // Sees Launch RF Scanner link
      expect(screen.getByRole('link', { name: /Launch RF Scanner/i })).toBeDefined();

      // Sees launchpad putaway card
      expect(screen.getByTestId('launchpad-putaway')).toBeDefined();
    });

    it('in PutawayPage: has full access to queue, generate button, and RF tab', async () => {
      const { recordedCalls } = setupMockFetch(superAdminUser);

      render(
        <RouterProvider initialPath="/app/inbound/putaway">
          <AuthProvider>
            <FacilityProvider>
              <PutawayPage />
            </FacilityProvider>
          </AuthProvider>
        </RouterProvider>
      );

      // Task queue renders
      await waitFor(() => {
        expect(screen.getByTestId('putaway-queue-view')).toBeDefined();
        expect(screen.getByTestId('generate-tasks-btn')).toBeDefined();
        expect(screen.getByTestId('tab-queue')).toBeDefined();
        expect(screen.getByTestId('tab-rf')).toBeDefined();
      });

      // Switch to RF tab
      fireEvent.click(screen.getByTestId('tab-rf'));

      await waitFor(() => {
        expect(screen.getByTestId('rf-execution-view')).toBeDefined();
        expect(screen.getByTestId('dual-scan-form')).toBeDefined();
      });

      const nextCalls = recordedCalls.filter(c => c.includes('/api/v1/putaway/tasks/next'));
      expect(nextCalls.length).toBeGreaterThanOrEqual(1);
    });
  });

  // 5. ORDINARY ADMIN USER (Role: Admin without SuperAdmin privilege)
  describe('Ordinary Admin User (Role: Admin without SuperAdmin)', () => {
    it('without explicit grants: cannot view restricted telemetry, cannot access RF execution, cannot see unauthorized launchpad items', async () => {
      const ordinaryAdminWithoutGrants = createMockUser([], ['Admin']);
      const { recordedCalls } = setupMockFetch(ordinaryAdminWithoutGrants);

      render(
        <RouterProvider initialPath="/app">
          <AuthProvider>
            <FacilityProvider>
              <DashboardPage />
            </FacilityProvider>
          </AuthProvider>
        </RouterProvider>
      );

      // Executive Analytics Restricted banner is shown
      await waitFor(() => {
        expect(screen.getByText(/Executive Analytics Restricted/i)).toBeDefined();
        const notices = screen.getAllByText(/Queue listing requires inbound:view authorization/i);
        expect(notices.length).toBeGreaterThan(0);
      });

      // Cannot see RF execution button or launch link
      expect(screen.queryByRole('button', { name: /Launch RF Putaway Scanner/i })).toBeNull();
      expect(screen.queryByRole('link', { name: /Launch RF Scanner/i })).toBeNull();

      // Cannot see unauthorized launchpad items (putaway RF, clients, users, roles)
      expect(screen.queryByTestId('launchpad-putaway')).toBeNull();
      expect(screen.queryByTestId('launchpad-clients')).toBeNull();
      expect(screen.queryByTestId('launchpad-users')).toBeNull();
      expect(screen.queryByTestId('launchpad-roles')).toBeNull();

      // Zero calls to task list query
      const taskCalls = recordedCalls.filter(c => c.includes('/api/v1/putaway/tasks'));
      expect(taskCalls.length).toBe(0);
    });

    it('in PutawayPage without explicit grants: shows authorization required, hides queue, generate button, and RF tab', async () => {
      const ordinaryAdminWithoutGrants = createMockUser([], ['Admin']);
      const { recordedCalls } = setupMockFetch(ordinaryAdminWithoutGrants);

      render(
        <RouterProvider initialPath="/app/inbound/putaway">
          <AuthProvider>
            <FacilityProvider>
              <PutawayPage />
            </FacilityProvider>
          </AuthProvider>
        </RouterProvider>
      );

      // Shows authorization required card
      await waitFor(() => {
        expect(screen.getByText(/Directed Putaway Authorization Required/i)).toBeDefined();
      });

      // No queue tab, no generate button, no RF tab
      expect(screen.queryByTestId('tab-queue')).toBeNull();
      expect(screen.queryByTestId('tab-rf')).toBeNull();
      expect(screen.queryByTestId('generate-tasks-btn')).toBeNull();

      // Zero calls to next or tasks
      const nextCalls = recordedCalls.filter(c => c.includes('/api/v1/putaway/tasks/next'));
      expect(nextCalls.length).toBe(0);
    });

    it('with explicit grants (e.g. inbound:view, users:view): can access corresponding features only', async () => {
      const ordinaryAdminWithGrants = createMockUser(['inbound:view', 'users:view'], ['Admin']);
      const { recordedCalls } = setupMockFetch(ordinaryAdminWithGrants);

      render(
        <RouterProvider initialPath="/app/inbound/putaway">
          <AuthProvider>
            <FacilityProvider>
              <PutawayPage />
            </FacilityProvider>
          </AuthProvider>
        </RouterProvider>
      );

      // Can view task queue
      await waitFor(() => {
        expect(screen.getByTestId('putaway-queue-view')).toBeDefined();
        expect(screen.getByText('LPN-MATRIX-01')).toBeDefined();
      });

      // But still cannot access RF tab or generate tasks
      expect(screen.queryByTestId('tab-rf')).toBeNull();
      expect(screen.queryByTestId('generate-tasks-btn')).toBeNull();

      // Never invokes /next
      const nextCalls = recordedCalls.filter(c => c.includes('/api/v1/putaway/tasks/next'));
      expect(nextCalls.length).toBe(0);
    });
  });
});
