import { describe, it, expect, vi, beforeEach } from 'vitest';
import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { AuthProvider } from '../context/AuthContext';
import { FacilityProvider } from '../context/FacilityContext';
import { RouterProvider } from '../context/RouterContext';
import { DashboardPage } from '../pages/DashboardPage';
import { tokenManager } from '../services/apiClient';
import { PutawayTaskStatus, PutawayPriority, ReceivingStatus } from '../types/inbound';

describe('Enterprise Control Tower & Operational Telemetry Tests', () => {
  const mockFacility = {
    id: 'fac-chi-01',
    facilityCode: 'FAC-CHI1',
    name: 'Chicago Central Logistics Hub',
    addressLine1: '400 Logistics Way',
    city: 'Chicago',
    state: 'IL',
    postalCode: '60601',
    country: 'USA',
    timezone: 'America/Chicago',
    isActive: true,
  };

  const executiveUser = {
    userId: 'usr-exec-01',
    email: 'ops.director@nexus3pl.com',
    firstName: 'Eleanor',
    lastName: 'Vance',
    userType: 1, // Staff
    tenantId: null,
    companyName: 'Nexus3PL Global Network',
    roles: ['WarehouseDirector', 'OperationsStaff'],
    permissions: [
      'analytics:control-tower:view',
      'inbound:view',
      'inbound:receive',
      'inbound:putaway',
      'inbound:putaway:execute',
      'inbound:putaway:manage',
      'inventory:view',
      'clients:view',
      'facilities:view',
      'users:view',
      'roles:view',
    ],
  };

  const floorOperatorUser = {
    userId: 'usr-op-02',
    email: 'operator@nexus3pl.com',
    firstName: 'Marcus',
    lastName: 'Chen',
    userType: 1,
    tenantId: null,
    companyName: 'Nexus3PL Global Network',
    roles: ['FloorOperator'],
    permissions: [
      'inbound:putaway:execute',
    ],
  };

  const mockSummary = {
    facilityId: 'fac-chi-01',
    asOfUtc: '2026-11-21T18:00:00Z',
    ordersReceivedToday: 120,
    ordersDispatchedToday: 95,
    packagesDispatchedToday: 210,
    unitsPickedToday: 1450,
    cartonsPackedToday: 320,
    inboundPalletsReceivedToday: 14,
    putawayCompletedToday: 11,
    returnsInspectedToday: 2,
    unitsPerLaborHourToday: 42.5,
    averageFulfillmentCycleTimeHours: 1.8,
    sameDayDispatchAdherencePercent: 98.5,
    totalBilledRevenueToday: 14500,
    netFreightMarginToday: 2300,
    onTimeDeliveryRatePercent: 99.2,
    activeDeliveryExceptionsCount: 1,
    palletPositionOccupancyPercent: 78.4,
    volumetricCubeFillPercent: 64.2,
  };

  const mockBottlenecks = {
    facilityId: 'fac-chi-01',
    evaluatedAtUtc: '2026-11-21T18:00:00Z',
    overallHealthScore: 96,
    receivingCongestion: {
      stage: 'Receiving',
      severity: 'Normal',
      queueDepth: 1,
      metricValue: 12,
      metricUnit: 'pallets',
      summaryMessage: 'Inflow clear',
      recommendedAction: 'None',
    },
    pickingBacklog: {
      stage: 'Picking',
      severity: 'Normal',
      queueDepth: 4,
      metricValue: 35,
      metricUnit: 'orders',
      summaryMessage: 'Wave dispatch balanced',
      recommendedAction: 'None',
    },
    packingDelays: {
      stage: 'Packing',
      severity: 'Normal',
      queueDepth: 2,
      metricValue: 18,
      metricUnit: 'cartons',
      summaryMessage: 'Pack stations operating at benchmark',
      recommendedAction: 'None',
    },
    dispatchDelays: {
      stage: 'Dispatch',
      severity: 'Normal',
      queueDepth: 0,
      metricValue: 0,
      metricUnit: 'pallets',
      summaryMessage: 'Staging lanes clear',
      recommendedAction: 'None',
    },
  };

  const mockDockYard = {
    totalYardSpotsCount: 20,
    occupiedYardSpotsCount: 4,
    yardOccupancyPercent: 20,
    trucksCheckedInCount: 12,
    trucksDepartedCount: 9,
    trucksWaitingInYardCount: 2,
    trucksAtDockCount: 3,
    averageYardWaitTimeMinutes: 14,
    averageDockServiceTimeMinutes: 28,
    averageTotalTurnTimeMinutes: 42,
  };

  const mockPutawayTask = {
    id: 'tsk-001-uuid',
    clientId: 'cli-001',
    facilityId: 'fac-chi-01',
    itemId: 'item-001',
    sku: 'SKU-ELEC-404',
    itemDescription: 'Industrial Barcode Terminal',
    sourceLocationId: 'loc-stg-01',
    sourceLocationCode: 'STG-STAGE-A',
    suggestedLocationId: 'loc-rack-01',
    suggestedLocationCode: 'RCK-01-02-B',
    suggestedLocationBarcode: 'LOC-RCK-01-02-B',
    quantity: 50,
    status: PutawayTaskStatus.Pending,
    priority: PutawayPriority.Normal,
    lpnCode: 'LPN-990011',
    createdAtUtc: '2026-11-21T17:00:00Z',
    version: 1,
  };

  const mockReceivingSession = {
    id: 'rec-001-uuid',
    facilityId: 'fac-chi-01',
    facilityCode: 'FAC-CHI1',
    sessionNumber: 'REC-2026-0881',
    doorNumber: 'DOCK-DOOR-04',
    trailerNumber: 'TRL-FEDEX-99',
    status: ReceivingStatus.Started,
    startedAtUtc: '2026-11-21T17:30:00Z',
    isBlindReceiving: false,
    requiresSupervisorApproval: false,
    isSupervisorApproved: false,
    version: 1,
    lines: [
      {
        id: 'line-01',
        receivingSessionId: 'rec-001-uuid',
        itemId: 'item-001',
        sku: 'SKU-ELEC-404',
        itemDescription: 'Industrial Barcode Terminal',
        lpn: 'LPN-990011',
        quantityReceived: 50,
        quantityExpected: 50,
        condition: 'Sound' as any,
        scannedAtUtc: '2026-11-21T17:35:00Z',
        version: 1,
      },
    ],
    discrepancies: [],
  };

  beforeEach(() => {
    tokenManager.clearSession();
    tokenManager.setTokens('mock-access-token', 'mock-refresh-token');
    tokenManager.setActiveFacilityId(mockFacility.id);
    vi.restoreAllMocks();
  });

  const setupFetchMock = (userProfile: any, customOverrides: Record<string, any> = {}) => {
    const recordedCalls: string[] = [];

    vi.stubGlobal('fetch', vi.fn().mockImplementation(async (input: any) => {
      const url = String(input);
      recordedCalls.push(url);

      if (url.includes('/api/v1/auth/me')) {
        return {
          ok: true,
          status: 200,
          json: async () => userProfile,
        };
      }

      if (url.includes('/api/v1/facilities')) {
        return {
          ok: true,
          status: 200,
          json: async () => [mockFacility],
        };
      }

      if (url.includes('/api/v1/control-tower/summary')) {
        if (customOverrides.summaryError) {
          return { ok: false, status: 500, json: async () => ({ message: 'Server telemetry down' }) };
        }
        if (!userProfile.permissions.includes('analytics:control-tower:view')) {
          return { ok: false, status: 403, json: async () => ({ message: 'Forbidden' }) };
        }
        return {
          ok: true,
          status: 200,
          json: async () => mockSummary,
        };
      }

      if (url.includes('/api/v1/control-tower/bottlenecks')) {
        if (customOverrides.bottlenecksError) {
          return { ok: false, status: 500, json: async () => ({ message: 'Bottleneck engine failed' }) };
        }
        return {
          ok: true,
          status: 200,
          json: async () => mockBottlenecks,
        };
      }

      if (url.includes('/api/v1/control-tower/dock-yard')) {
        if (customOverrides.dockYardError) {
          return { ok: false, status: 500, json: async () => ({ message: 'Dock yard service down' }) };
        }
        return {
          ok: true,
          status: 200,
          json: async () => mockDockYard,
        };
      }

      if (url.includes('/api/v1/putaway/tasks')) {
        // Backend PutawayTasksController.GetTasks strictly requires inbound:view
        if (!userProfile.permissions.includes('inbound:view')) {
          return { ok: false, status: 403, json: async () => ({ message: 'Forbidden: inbound:view required' }) };
        }
        const items = customOverrides.emptyQueues ? [] : [mockPutawayTask];
        const totalCount = customOverrides.putawayTotalCount ?? items.length;
        return {
          ok: true,
          status: 200,
          json: async () => ({
            items,
            pageNumber: 1,
            pageSize: 5,
            totalCount,
            totalPages: 1,
            hasPreviousPage: false,
            hasNextPage: false,
          }),
        };
      }

      if (url.includes('/api/v1/receiving/sessions')) {
        // Backend ReceivingController.GetSessions strictly requires inbound:view
        if (!userProfile.permissions.includes('inbound:view')) {
          return { ok: false, status: 403, json: async () => ({ message: 'Forbidden: inbound:view required' }) };
        }
        const items = customOverrides.emptyQueues ? [] : [mockReceivingSession];
        const totalCount = customOverrides.receivingTotalCount ?? items.length;
        return {
          ok: true,
          status: 200,
          json: async () => ({
            items,
            pageNumber: 1,
            pageSize: 5,
            totalCount,
            totalPages: 1,
            hasPreviousPage: false,
            hasNextPage: false,
          }),
        };
      }

      return {
        ok: true,
        status: 200,
        json: async () => ({}),
      };
    }));

    return { recordedCalls };
  };

  it('renders Control Tower with genuine backend KPI telemetry, live facility metadata, and queues when authorized', async () => {
    setupFetchMock(executiveUser);

    render(
      <RouterProvider initialPath="/app">
        <AuthProvider>
          <FacilityProvider>
            <DashboardPage />
          </FacilityProvider>
        </AuthProvider>
      </RouterProvider>
    );

    // 1. Facility & Header context with As-Of timestamp
    await waitFor(() => {
      expect(screen.getByText(/Enterprise Control Tower/i)).toBeDefined();
      expect(screen.getByText(/FAC-CHI1 — Chicago Central Logistics Hub/i)).toBeDefined();
      expect(screen.getByText(/Backend As-Of:/i)).toBeDefined();
    });

    // 2. Genuine KPI values
    await waitFor(() => {
      expect(screen.getByTestId('kpi-inbound-pallets')).toBeDefined();
      expect(screen.getByText('14')).toBeDefined(); // inbound pallets
      expect(screen.getByText('11')).toBeDefined(); // putaway completed
      expect(screen.getByText('1,450')).toBeDefined(); // units picked
      expect(screen.getByText('95')).toBeDefined(); // orders dispatched
      expect(screen.getByText('42.5')).toBeDefined(); // UPH
      expect(screen.getByText('78.4%')).toBeDefined(); // rack occupancy
    });

    // 3. Bottleneck diagnostics
    expect(screen.getByTestId('bottlenecks-card')).toBeDefined();
    expect(screen.getByText(/Facility Health: 96\/100/i)).toBeDefined();
    expect(screen.getByText(/Receiving Inflow/i)).toBeDefined();

    // 4. Operational Queues
    expect(screen.getByText('LPN-990011')).toBeDefined();
    expect(screen.getByText('SKU-ELEC-404')).toBeDefined();
    expect(screen.getByText('REC-2026-0881')).toBeDefined();
    expect(screen.getByText('DOCK-DOOR-04')).toBeDefined();

    // 5. Operations Launchpad links
    expect(screen.getByTestId('launchpad-putaway')).toBeDefined();
    expect(screen.getByTestId('launchpad-receiving')).toBeDefined();
    expect(screen.getByTestId('launchpad-inventory')).toBeDefined();
    expect(screen.getByTestId('launchpad-clients')).toBeDefined();
  });

  it('enforces RBAC alignment (P-CT-02): floor operator without inbound:view never calls listing endpoints but sees Launch RF Scanner', async () => {
    const { recordedCalls } = setupFetchMock(floorOperatorUser);

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
      expect(screen.getByText(/Executive Analytics Restricted/i)).toBeDefined();
      expect(screen.getByText(/analytics:control-tower:view/i)).toBeDefined();
    });

    // Assert that the frontend did NOT trigger 403-failing list queries for putaway or receiving
    const putawayCalls = recordedCalls.filter(c => c.includes('/api/v1/putaway/tasks'));
    const receivingCalls = recordedCalls.filter(c => c.includes('/api/v1/receiving/sessions'));
    expect(putawayCalls.length).toBe(0);
    expect(receivingCalls.length).toBe(0);

    // Floor operator sees the clean restricted message explaining inbound:view requirement
    const restrictedNotices = screen.getAllByText(/Queue listing requires inbound:view authorization/i);
    expect(restrictedNotices.length).toBeGreaterThan(0);

    // Floor operator retains execution navigation: "Launch RF Putaway Scanner" button is present
    expect(screen.getByRole('button', { name: /Launch RF Putaway Scanner/i })).toBeDefined();

    // Launchpad RF scanner link is accessible for operators with inbound:putaway:execute
    expect(screen.getByTestId('launchpad-putaway')).toBeDefined();

    // Unauthorized launchpad items (e.g. Clients, Users, Roles) are not visible
    expect(screen.queryByTestId('launchpad-clients')).toBeNull();
    expect(screen.queryByTestId('launchpad-users')).toBeNull();
  });

  it('handles operational queue status filtering and totalCount display (P-CT-01)', async () => {
    const { recordedCalls } = setupFetchMock(executiveUser, { putawayTotalCount: 38, receivingTotalCount: 17 });

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
      expect(screen.getByText('38 active')).toBeDefined();
      expect(screen.getByText('17 active')).toBeDefined();
    });

    // Verify preview row label
    expect(screen.getByText(/Showing top 1 of 38 active staging tasks/i)).toBeDefined();
    expect(screen.getByText(/Showing top 1 of 17 active sessions/i)).toBeDefined();

    // Verify status parameters were included in the query
    const putawayCall = recordedCalls.find(c => c.includes('/api/v1/putaway/tasks'));
    expect(putawayCall).toContain('statuses=Pending');
    expect(putawayCall).toContain('statuses=Assigned');
    expect(putawayCall).toContain('statuses=InTransit');

    const receivingCall = recordedCalls.find(c => c.includes('/api/v1/receiving/sessions'));
    expect(receivingCall).toContain('statuses=Started');
    expect(receivingCall).toContain('statuses=InProgress');
    expect(receivingCall).toContain('statuses=Flagged');
  });

  it('handles independent auxiliary error handling (P-CT-04): dock yard renders when bottlenecks fail', async () => {
    setupFetchMock(executiveUser, { bottlenecksError: true });

    render(
      <RouterProvider initialPath="/app">
        <AuthProvider>
          <FacilityProvider>
            <DashboardPage />
          </FacilityProvider>
        </AuthProvider>
      </RouterProvider>
    );

    // Bottlenecks card displays error without indefinite skeleton
    await waitFor(() => {
      expect(screen.getByText(/Bottleneck diagnostics error:/i)).toBeDefined();
    });

    // Dock yard telemetry still renders successfully in the same card
    expect(screen.getByText(/Trucks at Dock:/i)).toBeDefined();
    expect(screen.getByText(/Waiting in Yard:/i)).toBeDefined();
    expect(screen.getByText('4 / 20 (20%)')).toBeDefined();
  });

  it('renders clear empty states when staging buffer and dock receiving sessions are clear', async () => {
    setupFetchMock(executiveUser, { emptyQueues: true });

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
      expect(screen.getByText(/Staging buffer is clear/i)).toBeDefined();
      expect(screen.getByText(/No active sessions at dock doors/i)).toBeDefined();
    });
  });

  it('supports toggling bounded 60s auto-refresh polling (P-CT-03)', async () => {
    setupFetchMock(executiveUser);

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
      expect(screen.getByText(/Enterprise Control Tower/i)).toBeDefined();
    });

    // Auto-refresh button is initially 60s
    const autoRefreshBtn = screen.getByRole('button', { name: /Disable 60s auto-refresh/i });
    expect(autoRefreshBtn).toBeDefined();
    expect(screen.getByText('Auto: 60s')).toBeDefined();

    // Toggle off
    fireEvent.click(autoRefreshBtn);
    expect(screen.getByText('Auto: Off')).toBeDefined();

    // Toggle back on
    fireEvent.click(autoRefreshBtn);
    expect(screen.getByText('Auto: 60s')).toBeDefined();
  });

  it('renders retry state on network error and handles manual refresh button trigger', async () => {
    setupFetchMock(executiveUser, { summaryError: true });

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
      expect(screen.getByText(/Failed to load warehouse metrics/i)).toBeDefined();
    });

    // Click Sync Now
    const refreshBtn = screen.getByRole('button', { name: /Refresh operational metrics/i });
    expect(refreshBtn).toBeDefined();
    fireEvent.click(refreshBtn);
  });
});
