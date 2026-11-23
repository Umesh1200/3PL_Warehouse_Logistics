import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { AuthProvider } from '../context/AuthContext';
import { FacilityProvider } from '../context/FacilityContext';
import { RouterProvider } from '../context/RouterContext';
import { Sidebar } from '../components/layout/Sidebar';
import { LaunchpadGrid } from '../components/dashboard/LaunchpadGrid';
import { PickingWorkstationPage } from '../pages/outbound/PickingWorkstationPage';
import { tokenManager } from '../services/apiClient';
import { UserType } from '../types/auth';
import { PickTaskStatus, ShortPickReason } from '../types/outbound';

const FACILITY_A = {
  id: 'fac-pick-a',
  facilityCode: 'FAC-A',
  name: 'Chicago Central Hub',
  city: 'Chicago',
  timezone: 'America/Chicago',
  isActive: true,
};

const samplePendingTask = {
  id: 'pt-101',
  pickWaveId: 'wave-1',
  outboundOrderId: 'ord-1',
  orderNumber: 'SO-2026-PICK-1',
  outboundOrderLineId: 'line-1',
  inventoryAllocationId: 'alloc-1',
  stockOnHandId: 'stock-1',
  itemId: 'item-1',
  sku: 'SKU-MOTOR-742',
  itemDescription: 'Servo Drive Unit',
  sourceLocationId: 'loc-1',
  sourceLocationCode: 'A-04-02',
  destinationLocationId: null,
  destinationLocationCode: null,
  lotId: 'lot-1',
  lotNumber: 'LOT-742-Z',
  expectedLpnCode: 'LPN-PAL-1',
  targetHandlingUnitId: null,
  targetToteBarcode: 'TOTE-742',
  quantityToPick: 10,
  quantityPicked: 0,
  shortPickQuantity: 0,
  pickSequence: 1,
  status: PickTaskStatus.Pending,
  shortPickReason: null,
  assignedUserId: null,
  startedAtUtc: null,
  completedAtUtc: null,
  version: 1,
};

function renderWithShell(ui: React.ReactElement, initialPath = '/app/outbound/picking') {
  return render(
    <AuthProvider>
      <FacilityProvider>
        <RouterProvider initialPath={initialPath}>{ui}</RouterProvider>
      </FacilityProvider>
    </AuthProvider>
  );
}

describe('Phase 7.4.2: RF Directed Pick & Short-Pick Workstation', () => {
  beforeEach(() => {
    tokenManager.clearSession();
    tokenManager.setTokens('token-742', 'refresh-742');
    tokenManager.setActiveFacilityId(FACILITY_A.id);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    tokenManager.clearSession();
  });

  function mockBackend(options: {
    user: Record<string, any>;
    tasks?: any[];
    nextTask?: any;
    onFetch?: (url: string, init?: RequestInit) => Response | undefined | Promise<Response | undefined>;
  }) {
    let tasksState = [...(options.tasks ?? [samplePendingTask])];
    let nextTaskState = options.nextTask !== undefined ? options.nextTask : samplePendingTask;

    const spy = vi.fn().mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      const custom = options.onFetch ? await options.onFetch(url, init) : undefined;
      if (custom) return custom;

      if (url.endsWith('/api/v1/auth/me')) {
        return { ok: true, status: 200, json: async () => options.user } as Response;
      }
      if (url.endsWith('/api/v1/facilities')) {
        return { ok: true, status: 200, json: async () => [FACILITY_A] } as Response;
      }
      if (url.startsWith('/api/v1/outbound/pick-tasks/next')) {
        if (!nextTaskState) {
          return { ok: true, status: 204, json: async () => null } as Response;
        }
        return { ok: true, status: 200, json: async () => nextTaskState } as Response;
      }
      if (url.startsWith('/api/v1/outbound/pick-tasks/eligible-operators')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            items: [
              {
                id: 'op-fac-a',
                firstName: 'Pat',
                lastName: 'Picker',
                email: 'pat@nexus.local',
                assignedFacilityId: FACILITY_A.id,
              },
            ],
            totalCount: 1,
            pageNumber: 1,
            pageSize: 100,
          }),
        } as Response;
      }
      if (url.startsWith('/api/v1/outbound/pick-tasks?') || url === '/api/v1/outbound/pick-tasks') {
        return {
          ok: true,
          status: 200,
          json: async () => ({ items: tasksState, totalCount: tasksState.length, pageNumber: 1, pageSize: 15 }),
        } as Response;
      }
      if (url === '/api/v1/outbound/pick-tasks/pt-101/claim' && init?.method === 'POST') {
        const claimed = {
          ...samplePendingTask,
          status: PickTaskStatus.Assigned,
          assignedUserId: options.user.userId,
          version: 2,
        };
        nextTaskState = claimed;
        return { ok: true, status: 200, json: async () => claimed } as Response;
      }
      if (url === '/api/v1/outbound/pick-tasks/pt-101/assign' && init?.method === 'POST') {
        const parsed = JSON.parse(String(init.body));
        const assigned = {
          ...samplePendingTask,
          status: PickTaskStatus.Assigned,
          assignedUserId: parsed.operatorUserId,
          version: 2,
        };
        tasksState = [assigned];
        return { ok: true, status: 200, json: async () => assigned } as Response;
      }
      return { ok: false, status: 404, json: async () => ({ detail: `Unhandled ${url}` }) } as Response;
    });

    vi.stubGlobal('fetch', spy);
    return spy;
  }

  it('View-Only Staff sees Queue only, hides RF tab & Assign buttons, and never calls /next', async () => {
    const fetchSpy = mockBackend({
      user: {
        userId: 'view-only-1',
        email: 'viewer@nexus.local',
        userType: UserType.Staff,
        roles: ['WarehouseViewer'],
        permissions: ['outbound:tasks:view'],
      },
    });

    renderWithShell(<PickingWorkstationPage />);

    expect(await screen.findByTestId('pick-queue-view')).toBeDefined();
    expect(await screen.findByText('SKU-MOTOR-742')).toBeDefined();
    expect(screen.queryByTestId('tab-pick-rf')).toBeNull();
    expect(screen.queryByTestId('assign-task-btn-pt-101')).toBeNull();

    const calledUrls = fetchSpy.mock.calls.map(([u]) => String(u));
    expect(calledUrls.some((u) => u.includes('/api/v1/outbound/pick-tasks/next'))).toBe(false);
  });

  it('Manage-Only Staff renders manage banner and never calls queue list or /next', async () => {
    const fetchSpy = mockBackend({
      user: {
        userId: 'manage-only-1',
        email: 'lead@nexus.local',
        userType: UserType.Staff,
        roles: ['PickLead'],
        permissions: ['outbound:tasks:manage'],
      },
    });

    renderWithShell(<PickingWorkstationPage />);

    expect(await screen.findByTestId('pick-manage-workstation')).toBeDefined();
    expect(screen.queryByTestId('pick-queue-view')).toBeNull();
    expect(screen.queryByTestId('pick-rf-view')).toBeNull();

    const calledUrls = fetchSpy.mock.calls.map(([u]) => String(u));
    expect(calledUrls.some((u) => u.startsWith('/api/v1/outbound/pick-tasks'))).toBe(false);
  });

  it('Supervisor without users:view assigns task via eligible-operators endpoint', async () => {
    const fetchSpy = mockBackend({
      user: {
        userId: 'sup-1',
        email: 'sup@nexus.local',
        userType: UserType.Staff,
        roles: ['WarehouseSupervisor'],
        permissions: ['outbound:tasks:view', 'outbound:tasks:manage'],
      },
    });

    renderWithShell(<PickingWorkstationPage />);
    fireEvent.click(await screen.findByTestId('assign-task-btn-pt-101'));

    await waitFor(() => {
      expect(screen.getByRole('option', { name: /Pat Picker/i })).toBeDefined();
    });
    fireEvent.click(screen.getByTestId('submit-assign-task-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('picking-feedback-banner').textContent).toMatch(/Assigned task SKU-MOTOR-742/i);
    });

    const calledUrls = fetchSpy.mock.calls.map(([u]) => String(u));
    expect(calledUrls.some((u) => u.startsWith('/api/v1/users'))).toBe(false);
    expect(calledUrls.some((u) => u.startsWith('/api/v1/outbound/pick-tasks/eligible-operators'))).toBe(true);
  });

  it('Pick-Only operator defaults to RF mode without queue calls, enforces Claim gate, and protects double-scan with idempotency', async () => {
    const confirmBodies: any[] = [];
    let failFirstConfirm = true;

    const fetchSpy = mockBackend({
      user: {
        userId: 'picker-1',
        email: 'picker@nexus.local',
        userType: UserType.Staff,
        roles: ['WarehouseOperator'],
        permissions: ['outbound:pick'],
      },
      onFetch: async (url, init) => {
        if (url === '/api/v1/outbound/pick-tasks/pt-101/confirm' && init?.method === 'POST') {
          const body = JSON.parse(String(init.body));
          confirmBodies.push(body);
          if (failFirstConfirm) {
            failFirstConfirm = false;
            return {
              ok: false,
              status: 409,
              json: async () => ({ detail: 'Transient stock contention' }),
            } as Response;
          }
          await new Promise((r) => setTimeout(r, 35));
          return {
            ok: true,
            status: 200,
            json: async () => ({
              isSuccess: true,
              pickTaskId: 'pt-101',
              taskStatus: PickTaskStatus.Completed,
              quantityPicked: 10,
              shortPickQuantity: 0,
              destinationStockId: 'st-2',
              destinationHandlingUnitId: 'hu-2',
              destinationToteBarcode: 'TOTE-742',
              inventoryTransactionId: 501,
              waveCompleted: true,
              orderCompleted: false,
              message: 'Pick confirmed into TOTE-742.',
            }),
          } as Response;
        }
        return undefined;
      },
    });

    renderWithShell(<PickingWorkstationPage />);

    // Defaults directly to RF mode; Claim gate visible; Confirm disabled before claiming
    expect(await screen.findByTestId('rf-claim-gate')).toBeDefined();
    expect((screen.getByTestId('confirm-pick-btn') as HTMLButtonElement).disabled).toBe(true);

    // Must never call supervisor queue endpoint or pass operatorUserId on /next
    const initialUrls = fetchSpy.mock.calls.map(([u]) => String(u));
    expect(initialUrls.some((u) => u === '/api/v1/outbound/pick-tasks' || u.startsWith('/api/v1/outbound/pick-tasks?'))).toBe(false);
    expect(initialUrls.some((u) => u.includes('operatorUserId'))).toBe(false);

    // Atomic claim unlocks scan inputs
    fireEvent.click(screen.getByTestId('claim-pick-task-btn'));
    await waitFor(() => {
      expect(screen.queryByTestId('rf-claim-gate')).toBeNull();
      expect((screen.getByTestId('rf-scan-location') as HTMLInputElement).disabled).toBe(false);
    });

    fireEvent.change(screen.getByTestId('rf-scan-location'), { target: { value: 'A-04-02' } });
    fireEvent.change(screen.getByTestId('rf-scan-item'), { target: { value: 'SKU-MOTOR-742' } });
    fireEvent.change(screen.getByTestId('rf-scan-lot'), { target: { value: 'LOT-742-Z' } });
    fireEvent.change(screen.getByTestId('rf-scan-tote'), { target: { value: 'TOTE-742' } });

    // First submit gets 409 conflict
    fireEvent.click(screen.getByTestId('confirm-pick-btn'));
    await waitFor(() => {
      expect(screen.getByTestId('rf-action-error').textContent).toContain('409');
    });

    // Rapid double-click retry fires ONLY ONE extra confirm request preserving exact idempotencyKey
    const confirmBtn = screen.getByTestId('confirm-pick-btn');
    fireEvent.click(confirmBtn);
    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(screen.getByTestId('picking-feedback-banner').textContent).toContain('TOTE-742');
    });

    expect(confirmBodies).toHaveLength(2);
    expect(confirmBodies[0].idempotencyKey).toBeTruthy();
    expect(confirmBodies[1].idempotencyKey).toBe(confirmBodies[0].idempotencyKey);
  });

  it('handles 409 concurrent claim race cleanly and submits short-pick exception modal', async () => {
    let claimCount = 0;
    const assignedToMeTask = {
      ...samplePendingTask,
      status: PickTaskStatus.Assigned,
      assignedUserId: 'picker-race',
    };
    let shortPickBody: any = null;

    mockBackend({
      user: {
        userId: 'picker-race',
        email: 'picker2@nexus.local',
        userType: UserType.Staff,
        roles: ['WarehouseOperator'],
        permissions: ['outbound:pick'],
      },
      onFetch: async (url, init) => {
        if (url.startsWith('/api/v1/outbound/pick-tasks/next')) {
          return {
            ok: true,
            status: 200,
            json: async () => (claimCount === 0 ? samplePendingTask : assignedToMeTask),
          } as Response;
        }
        if (url === '/api/v1/outbound/pick-tasks/pt-101/claim' && init?.method === 'POST') {
          claimCount++;
          return {
            ok: false,
            status: 409,
            json: async () => ({ detail: 'Task already claimed by another operator' }),
          } as Response;
        }
        if (url === '/api/v1/outbound/pick-tasks/pt-101/short-pick' && init?.method === 'POST') {
          shortPickBody = JSON.parse(String(init.body));
          return {
            ok: true,
            status: 200,
            json: async () => ({
              isSuccess: true,
              pickTaskId: 'pt-101',
              taskStatus: PickTaskStatus.ShortPicked,
              quantityPicked: 3,
              discrepancyQuantity: 7,
              pickExceptionId: 'exc-1',
              generatedCycleCountId: 'cc-1',
              locationLockApplied: true,
              message: 'Short-pick processed.',
            }),
          } as Response;
        }
        return undefined;
      },
    });

    renderWithShell(<PickingWorkstationPage />);
    fireEvent.click(await screen.findByTestId('claim-pick-task-btn'));

    // After 409 conflict, refreshes /next and surfaces assigned task
    await waitFor(() => {
      expect(screen.queryByTestId('rf-claim-gate')).toBeNull();
    });

    // Open Short-Pick Modal & report DamagedStock
    fireEvent.click(screen.getByTestId('open-short-pick-btn'));
    fireEvent.change(await screen.findByTestId('short-pick-qty-input'), { target: { value: '3' } });
    fireEvent.change(screen.getByTestId('short-pick-reason-select'), { target: { value: ShortPickReason.DamagedStock } });
    fireEvent.change(screen.getByTestId('short-pick-notes-input'), { target: { value: 'Crushed carton' } });
    fireEvent.click(screen.getByTestId('submit-short-pick-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('picking-feedback-banner').textContent).toMatch(/Short-pick recorded \(7 unit\(s\) short\)/i);
      expect(screen.getByTestId('picking-feedback-banner').textContent).toMatch(/Source bin locked/i);
    });

    expect(shortPickBody?.reason).toBe(ShortPickReason.DamagedStock);
    expect(shortPickBody?.quantityPicked).toBe(3);
    expect(shortPickBody?.idempotencyKey).toBeTruthy();
  });

  it('rejects ClientAdmin and Driver identities cleanly from shell navigation and direct route even with legacy claims', async () => {
    for (const userType of [UserType.Client, UserType.Driver]) {
      const fetchSpy = mockBackend({
        user: {
          userId: `${userType}-legacy`,
          email: `${userType}@external.local`,
          userType,
          tenantId: 'cli-1',
          roles: [userType === UserType.Client ? 'ClientAdmin' : 'Driver'],
          permissions: ['outbound:pick', 'outbound:tasks:view', 'outbound:tasks:manage'],
        },
      });

      const { unmount } = renderWithShell(
        <div>
          <Sidebar />
          <LaunchpadGrid />
          <PickingWorkstationPage />
        </div>
      );

      await waitFor(() => {
        expect(screen.getByTestId('picking-forbidden-card')).toBeDefined();
      });
      expect(screen.queryByTestId('launchpad-picking')).toBeNull();
      expect(screen.queryByText('Directed Picking')).toBeNull();

      const calledUrls = fetchSpy.mock.calls.map(([u]) => String(u));
      expect(calledUrls.some((u) => u.includes('/api/v1/outbound/pick-tasks'))).toBe(false);
      unmount();
    }
  });

  it('distinguishes RF load errors from empty queue (204) and fails closed on malformed pick DTOs', async () => {
    let mode: 'http500' | 'malformed' | 'empty' = 'http500';
    mockBackend({
      user: {
        userId: 'picker-err',
        email: 'picker@nexus.local',
        userType: UserType.Staff,
        roles: ['WarehouseOperator'],
        permissions: ['outbound:pick'],
      },
      onFetch: async (url) => {
        if (url.startsWith('/api/v1/outbound/pick-tasks/next')) {
          if (mode === 'http500') {
            return { ok: false, status: 500, json: async () => ({ detail: 'Pick engine unavailable' }) } as Response;
          }
          if (mode === 'malformed') {
            return {
              ok: true,
              status: 200,
              json: async () => ({ ...samplePendingTask, status: 'PhantomTaskStatus' }),
            } as Response;
          }
          return { ok: true, status: 204, json: async () => null } as Response;
        }
        return undefined;
      },
    });

    renderWithShell(<PickingWorkstationPage />);
    expect(await screen.findByTestId('rf-error-banner')).toBeDefined();
    expect(screen.queryByTestId('rf-empty-state')).toBeNull();

    mode = 'malformed';
    fireEvent.click(screen.getByTestId('rf-retry-next-btn'));
    await waitFor(() => {
      expect(screen.getByTestId('rf-error-banner').textContent).toMatch(/PickTaskStatus/i);
    });

    mode = 'empty';
    fireEvent.click(screen.getByTestId('rf-retry-next-btn'));
    expect(await screen.findByTestId('rf-empty-state')).toBeDefined();
  });
});
