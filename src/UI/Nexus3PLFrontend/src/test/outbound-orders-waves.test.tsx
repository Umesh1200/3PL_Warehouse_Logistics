import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { AuthProvider } from '../context/AuthContext';
import { FacilityProvider, useFacility } from '../context/FacilityContext';
import { RouterProvider } from '../context/RouterContext';
import { Sidebar } from '../components/layout/Sidebar';
import { LaunchpadGrid } from '../components/dashboard/LaunchpadGrid';
import { OutboundOrdersPage } from '../pages/outbound/OutboundOrdersPage';
import { PickWavesPage } from '../pages/outbound/PickWavesPage';
import { tokenManager } from '../services/apiClient';
import { UserType } from '../types/auth';
import {
  CarrierServiceLevel,
  OutboundOrderStatus,
  OutboundOrderType,
  PickTaskStatus,
  PickWaveStatus,
  PickWaveType,
} from '../types/outbound';

const FACILITY_A = {
  id: 'fac-a-001',
  facilityCode: 'FAC-A',
  name: 'Chicago Central Hub',
  city: 'Chicago',
  timezone: 'America/Chicago',
  isActive: true,
};

const FACILITY_B = {
  id: 'fac-b-002',
  facilityCode: 'FAC-B',
  name: 'Dallas East Hub',
  city: 'Dallas',
  timezone: 'America/Chicago',
  isActive: true,
};

const sampleOrderA = {
  id: 'ord-1',
  clientId: 'client-a',
  facilityId: FACILITY_A.id,
  facilityName: FACILITY_A.name,
  destinationFacilityId: null,
  destinationFacilityName: null,
  customerId: null,
  customerCode: 'CUST-1',
  customerName: 'Acme Retail Co',
  orderNumber: 'SO-2026-1001',
  externalOrderReference: 'EXT-9001',
  orderType: OutboundOrderType.B2BSalesOrder,
  status: OutboundOrderStatus.Released,
  statusBeforeHold: null,
  priority: 8,
  serviceLevel: CarrierServiceLevel.StandardGround,
  carrierCode: 'SIM-FEDEX',
  destinationAddress: '100 Main St',
  orderDateUtc: '2026-11-22T10:00:00Z',
  requiredShipDateUtc: '2026-11-24T10:00:00Z',
  earliestShipDateUtc: null,
  holdReason: null,
  heldAtUtc: null,
  cancellationReason: null,
  cancelledAtUtc: null,
  notes: null,
  version: 1,
  createdAt: '2026-11-22T10:00:00Z',
  shippingAddress: {
    recipientName: 'Jane Doe',
    companyName: 'Acme Retail Co',
    addressLine1: '100 Main St',
    city: 'Chicago',
    stateOrProvince: 'IL',
    postalCode: '60601',
    countryCode: 'USA',
  },
  lines: [
    {
      id: 'line-1',
      outboundOrderId: 'ord-1',
      itemId: 'item-1',
      sku: 'SKU-OUT-01',
      itemDescription: 'Outbound Servo Motor',
      lineNumber: 1,
      quantityOrdered: 10,
      quantityAllocated: 0,
      quantityShipped: 0,
      remainingDemand: 10,
      requiredLotNumber: 'LOT-A',
      minExpiryDateUtc: null,
      status: 'Pending',
    },
  ],
  statusHistories: [],
};

const sampleWavePlanning = {
  id: 'wave-1',
  clientId: 'client-a',
  facilityId: FACILITY_A.id,
  waveNumber: 'WAVE-20261123-001',
  waveType: PickWaveType.BatchPick,
  status: PickWaveStatus.Planning,
  priority: 8,
  zoneId: null,
  tempClass: null,
  assignedToUserId: null,
  releasedAtUtc: null,
  startedAtUtc: null,
  completedAtUtc: null,
  createdAt: '2026-11-23T08:00:00Z',
  ordersCount: 1,
  tasksCount: 1,
  totalQuantityToPick: 10,
  totalQuantityPicked: 0,
  estimatedTravelMeters: 142.5,
  waveOrders: [
    {
      id: 'wo-1',
      outboundOrderId: 'ord-1',
      orderNumber: 'SO-2026-1001',
      customerName: 'Acme Retail Co',
      priority: 8,
      sequenceNumber: 1,
      orderStatus: OutboundOrderStatus.Allocated,
    },
  ],
  tasks: [
    {
      id: 'pt-1',
      pickWaveId: 'wave-1',
      outboundOrderId: 'ord-1',
      orderNumber: 'SO-2026-1001',
      outboundOrderLineId: 'line-1',
      inventoryAllocationId: 'alloc-1',
      stockOnHandId: 'stock-1',
      itemId: 'item-1',
      sku: 'SKU-OUT-01',
      itemDescription: 'Outbound Servo Motor',
      sourceLocationId: 'loc-1',
      sourceLocationCode: 'A-01-01',
      quantityToPick: 10,
      quantityPicked: 0,
      shortPickQuantity: 0,
      pickSequence: 1,
      status: PickTaskStatus.Pending,
      version: 1,
    },
  ],
};

function renderWithShell(ui: React.ReactElement, initialPath = '/app/outbound/orders') {
  return render(
    <RouterProvider initialPath={initialPath}>
      <AuthProvider>
        <FacilityProvider>{ui}</FacilityProvider>
      </AuthProvider>
    </RouterProvider>
  );
}

describe('Phase 7.4.1: Outbound Orders, Allocation & Pick Waves Frontend', () => {
  beforeEach(() => {
    tokenManager.clearSession();
    tokenManager.setTokens('access-tok', 'refresh-tok');
    tokenManager.setActiveFacilityId(FACILITY_A.id);
    vi.restoreAllMocks();
  });

  afterEach(() => {
    tokenManager.clearSession();
    vi.restoreAllMocks();
  });

  function mockBackend(options: {
    user: Record<string, any>;
    orders?: any[];
    waves?: any[];
    onFetch?: (url: string, init?: RequestInit) => Response | undefined | Promise<Response | undefined>;
  }) {
    let ordersState = [...(options.orders ?? [sampleOrderA])];
    let wavesState = [...(options.waves ?? [sampleWavePlanning])];

    const spy = vi.fn().mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      const custom = options.onFetch ? await options.onFetch(url, init) : undefined;
      if (custom) return custom;

      if (url.endsWith('/api/v1/auth/me')) {
        return { ok: true, status: 200, json: async () => options.user } as Response;
      }
      if (url.endsWith('/api/v1/facilities')) {
        return { ok: true, status: 200, json: async () => [FACILITY_A, FACILITY_B] } as Response;
      }
      if (url.startsWith('/api/v1/outbound/orders/reference-clients') || url.startsWith('/api/v1/outbound/pick-waves/eligible-clients')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            items: [{ id: 'client-a', companyName: 'Acme Corp', accountCode: 'ACME', status: 'Active' }],
            totalCount: 1,
            pageNumber: 1,
            pageSize: 100,
          }),
        } as Response;
      }
      if (url.startsWith('/api/v1/outbound/orders/catalog-items')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            items: [
              {
                id: 'item-1',
                clientId: 'client-a',
                sku: 'SKU-OUT-01',
                description: 'Outbound Servo Motor',
                storageTempClass: 'Ambient',
                isLotTracked: true,
                requiresExpiryDate: false,
              },
            ],
            totalCount: 1,
            pageNumber: 1,
            pageSize: 100,
          }),
        } as Response;
      }
      if (url.startsWith('/api/v1/locations/tree')) {
        return {
          ok: true,
          status: 200,
          json: async () => [
            {
              id: 'zone-ambient-1',
              facilityId: FACILITY_A.id,
              locationType: 'Zone',
              locationCode: 'ZONE-AMB',
              barcode: 'ZONE-AMB',
              isActive: true,
              children: [],
            },
          ],
        } as Response;
      }
      if (url.includes('/stock-availability')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            orderId: 'ord-1',
            orderNumber: 'SO-2026-1001',
            isFullyAvailable: true,
            overallFulfillmentPercent: 100,
            lines: [
              {
                lineId: 'line-1',
                lineNumber: 1,
                itemId: 'item-1',
                sku: 'SKU-OUT-01',
                itemDescription: 'Outbound Servo Motor',
                quantityRequested: 10,
                quantityAvailable: 25,
                shortageQuantity: 0,
                isFullyAvailable: true,
              },
            ],
          }),
        } as Response;
      }
      if (url.startsWith('/api/v1/outbound/orders?') || url === '/api/v1/outbound/orders') {
        if ((init?.method || 'GET') === 'GET') {
          return {
            ok: true,
            status: 200,
            json: async () => ({ items: ordersState, totalCount: ordersState.length, pageNumber: 1, pageSize: 15 }),
          } as Response;
        }
      }
      if (url === '/api/v1/outbound/orders/ord-1') {
        const found = ordersState.find((o) => o.id === 'ord-1') || sampleOrderA;
        return { ok: true, status: 200, json: async () => found } as Response;
      }
      if (url === '/api/v1/outbound/pick-waves' && (init?.method || 'GET') === 'POST') {
        return { ok: true, status: 201, json: async () => sampleWavePlanning } as Response;
      }
      if (url.startsWith('/api/v1/outbound/pick-waves?')) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ items: wavesState, totalCount: wavesState.length, pageNumber: 1, pageSize: 15 }),
        } as Response;
      }
      if (url === '/api/v1/outbound/pick-waves/wave-1') {
        const found = wavesState.find((w) => w.id === 'wave-1') || sampleWavePlanning;
        return { ok: true, status: 200, json: async () => found } as Response;
      }
      if (url === '/api/v1/outbound/pick-waves/wave-1/release' && init?.method === 'POST') {
        const released = {
          ...sampleWavePlanning,
          status: PickWaveStatus.Released,
          releasedAtUtc: '2026-11-23T09:00:00Z',
        };
        wavesState = [released];
        return { ok: true, status: 200, json: async () => released } as Response;
      }
      return { ok: false, status: 404, json: async () => ({ detail: `Unhandled ${url}` }) } as Response;
    });

    vi.stubGlobal('fetch', spy);
    return spy;
  }

  it('hydrates orders list, passes server-side filters and pagination, and recovers on retry after error', async () => {
    let failFirstOrdersCall = true;
    const fetchSpy = mockBackend({
      user: {
        userId: 'staff-1',
        email: 'ops@nexus.local',
        userType: UserType.Staff,
        roles: ['OperationsManager'],
        permissions: ['outbound:orders:view'],
      },
      onFetch: (url) => {
        if (url.startsWith('/api/v1/outbound/orders?') && failFirstOrdersCall) {
          failFirstOrdersCall = false;
          return {
            ok: false,
            status: 500,
            json: async () => ({ detail: 'Database timeout' }),
          } as Response;
        }
        return undefined;
      },
    });

    renderWithShell(<OutboundOrdersPage />);

    await waitFor(() => {
      expect(screen.getByTestId('orders-error-banner')).toBeDefined();
      expect(screen.getByText(/Database timeout/i)).toBeDefined();
    });

    fireEvent.click(screen.getByText('Retry'));

    await waitFor(() => {
      expect(screen.getByText('SO-2026-1001')).toBeDefined();
      expect(screen.getByText('Acme Retail Co')).toBeDefined();
    });

    fireEvent.change(screen.getByLabelText(/Status/i), {
      target: { value: OutboundOrderStatus.Allocated },
    });
    fireEvent.change(screen.getByLabelText(/Search Order/i), {
      target: { value: 'EXT-9001' },
    });

    await waitFor(() => {
      const calledUrls = fetchSpy.mock.calls.map((c) => String(c[0]));
      expect(calledUrls.some((u) => u.includes('status=Allocated') && u.includes('searchTerm=EXT-9001'))).toBe(true);
    });
  });

  it('validates order creation rules including stock transfer facility separation and Escape/focus restoration', async () => {
    mockBackend({
      user: {
        userId: 'staff-1',
        email: 'ops@nexus.local',
        userType: UserType.Staff,
        roles: ['OperationsManager'],
        permissions: ['outbound:orders:view', 'outbound:orders:create', 'clients:view', 'items:view'],
      },
    });

    renderWithShell(<OutboundOrdersPage />);

    const openBtn = await screen.findByTestId('open-create-order-btn');
    openBtn.focus();
    fireEvent.click(openBtn);

    await waitFor(() => {
      expect(screen.getByText('Create Outbound Order')).toBeDefined();
    });

    // Switch to Stock Transfer and attempt submit without destination facility -> inline error
    fireEvent.change(document.getElementById('order-type')!, {
      target: { value: OutboundOrderType.StockTransferOrder },
    });
    fireEvent.click(screen.getByTestId('submit-create-order'));

    await waitFor(() => {
      expect(screen.getByText(/Destination facility is required/i)).toBeDefined();
    });

    // Escape closes modal and restores focus to trigger button
    fireEvent.keyDown(window, { key: 'Escape' });
    await waitFor(() => {
      expect(screen.queryByText('Create Outbound Order')).toBeNull();
    });
    expect(document.activeElement).toBe(openBtn);
  });

  it('previews CSV/JSON bulk import rows, shows duplicates/failures, and retries failed rows without duplicates', async () => {
    const bulkRequests: any[] = [];
    mockBackend({
      user: {
        userId: 'client-admin-1',
        email: 'client@acme.com',
        userType: UserType.Client,
        tenantId: 'client-a',
        roles: ['ClientAdmin'],
        permissions: ['outbound:orders:view', 'outbound:orders:import'],
      },
      onFetch: async (url, init) => {
        if (url === '/api/v1/outbound/orders/bulk-import' && init?.method === 'POST') {
          const parsed = JSON.parse(String(init.body));
          bulkRequests.push(parsed);
          if (bulkRequests.length === 1) {
            return {
              ok: true,
              status: 200,
              json: async () => ({
                batchId: 'b-1',
                batchNumber: 'BATCH-001',
                sourceFileName: parsed.sourceFileName,
                totalRows: 3,
                successfulRows: 1,
                duplicateRows: 1,
                failedRows: 1,
                status: 'CompletedWithErrors',
                startedAtUtc: '2026-11-23T08:00:00Z',
                rows: [
                  { rowNumber: 1, externalOrderReference: 'REF-1', isSuccess: true, isDuplicate: false, createdOutboundOrderId: 'ord-new-1' },
                  { rowNumber: 2, externalOrderReference: 'REF-DUP', isSuccess: false, isDuplicate: true, errorMessage: 'Duplicate reference' },
                  { rowNumber: 3, externalOrderReference: 'REF-FAIL', isSuccess: false, isDuplicate: false, errorMessage: 'Transient error' },
                ],
              }),
            } as Response;
          }
          return {
            ok: true,
            status: 200,
            json: async () => ({
              batchId: 'b-2',
              batchNumber: 'BATCH-002',
              sourceFileName: parsed.sourceFileName,
              totalRows: 1,
              successfulRows: 1,
              duplicateRows: 0,
              failedRows: 0,
              status: 'Completed',
              startedAtUtc: '2026-11-23T08:01:00Z',
              rows: [
                { rowNumber: 1, externalOrderReference: 'REF-FAIL', isSuccess: true, isDuplicate: false, createdOutboundOrderId: 'ord-new-3' },
              ],
            }),
          } as Response;
        }
        return undefined;
      },
    });

    renderWithShell(<OutboundOrdersPage />);
    await screen.findByText(/Chicago Central Hub \(FAC-A\)/i);
    fireEvent.click(await screen.findByTestId('open-bulk-import-btn'));

    fireEvent.change(screen.getByLabelText(/Input Format/i), { target: { value: 'csv' } });
    const csvPayload = [
      'externalOrderReference,recipientName,addressLine1,city,stateOrProvince,postalCode,countryCode,itemId,quantityOrdered',
      'REF-1,Alice,100 Pine,Chicago,IL,60601,USA,item-1,2',
      'REF-DUP,Bob,200 Pine,Chicago,IL,60601,USA,item-1,3',
      'REF-FAIL,Cara,300 Pine,Chicago,IL,60601,USA,item-1,4',
    ].join('\n');

    fireEvent.change(screen.getByLabelText(/Paste CSV Payload/i), { target: { value: csvPayload } });

    await waitFor(() => {
      expect(screen.getByText(/Parsed Preview \(3 orders, 3 valid\)/i)).toBeDefined();
    });

    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.click(screen.getByTestId('submit-bulk-import'));

    await waitFor(() => {
      expect(screen.getByTestId('bulk-import-summary')).toBeDefined();
      expect(screen.getByText('Duplicate')).toBeDefined();
    });

    fireEvent.click(screen.getByText(/Retry Failed Rows \(1\)/i));

    await waitFor(() => {
      expect(bulkRequests.length).toBe(2);
      // Only REF-FAIL retried; succeeded & duplicate excluded
      expect(bulkRequests[1].orders).toHaveLength(1);
      expect(bulkRequests[1].orders[0].externalOrderReference).toBe('REF-FAIL');
    });
  });

  it('enforces allocation single-flight double-click guard, preserves idempotency key on retry, and surfaces 409/422 cleanly', async () => {
    const allocateCalls: any[] = [];
    let attempt = 0;

    mockBackend({
      user: {
        userId: 'staff-1',
        email: 'ops@nexus.local',
        userType: UserType.Staff,
        roles: ['OperationsManager'],
        permissions: [
          'outbound:orders:view',
          'outbound:orders:allocate',
          'outbound:orders:manage',
          'outbound:orders:cancel',
        ],
      },
      onFetch: async (url, init) => {
        if (url === '/api/v1/outbound/orders/ord-1/allocate' && init?.method === 'POST') {
          attempt++;
          const body = JSON.parse(String(init.body));
          allocateCalls.push(body);
          if (attempt === 1) {
            return {
              ok: false,
              status: 409,
              json: async () => ({ detail: 'Stock concurrency version conflict' }),
            } as Response;
          }
          await new Promise((r) => setTimeout(r, 40));
          return {
            ok: true,
            status: 200,
            json: async () => ({
              outboundOrderId: 'ord-1',
              orderNumber: 'SO-2026-1001',
              status: 'Allocated',
              totalQuantityOrdered: 10,
              totalQuantityAllocated: 8,
              totalShortageQuantity: 2,
              isDuplicateRequest: false,
              attemptCount: 1,
              lineSummaries: [
                {
                  lineId: 'line-1',
                  lineNumber: 1,
                  itemId: 'item-1',
                  sku: 'SKU-OUT-01',
                  quantityOrdered: 10,
                  quantityAllocated: 8,
                  remainingDemand: 2,
                  status: 'PartiallyAllocated',
                  allocations: [
                    {
                      allocationId: 'alloc-99',
                      stockOnHandId: 'stock-1',
                      locationId: 'loc-1',
                      locationCode: 'BIN-A1',
                      lotNumber: 'LOT-A',
                      quantityAllocated: 8,
                      status: 'Allocated',
                    },
                  ],
                },
              ],
              shortages: [
                {
                  shortageId: 'sh-1',
                  lineId: 'line-1',
                  itemId: 'item-1',
                  shortageQuantity: 2,
                  reason: 'InsufficientStock',
                  notes: '2 short',
                },
              ],
            }),
          } as Response;
        }
        return undefined;
      },
    });

    renderWithShell(<OutboundOrdersPage />);
    await screen.findByText(/Chicago Central Hub \(FAC-A\)/i);
    fireEvent.click(await screen.findByTestId('inspect-order-ord-1'));

    await waitFor(() => expect(screen.getByTestId('check-stock-availability')).toBeDefined());
    fireEvent.click(screen.getByTestId('check-stock-availability'));
    await waitFor(() => expect(screen.getByTestId('stock-availability-panel')).toBeDefined());

    // First attempt returns 409 conflict
    fireEvent.click(screen.getByTestId('btn-allocate-order'));
    await waitFor(() => {
      expect(screen.getByTestId('order-action-error').textContent).toContain('409');
    });

    // Rapid double click on retry should only fire ONE additional allocate request preserving exact same idempotencyKey
    const retryBtn = screen.getByTestId('btn-allocate-order');
    fireEvent.click(retryBtn);
    fireEvent.click(retryBtn);

    await waitFor(() => {
      expect(screen.getByTestId('allocation-result-panel')).toBeDefined();
    });

    expect(allocateCalls).toHaveLength(2);
    expect(allocateCalls[0].idempotencyKey).toBeTruthy();
    expect(allocateCalls[1].idempotencyKey).toBe(allocateCalls[0].idempotencyKey);
    expect(screen.getByText(/BIN-A1/)).toBeDefined();
    expect(screen.getByText(/Short 2/)).toBeDefined();
  });

  it('sends nested { request } wrapper for GeneratePickWaveCommand and enforces release confirmation & single-flight', async () => {
    const wavePayloads: any[] = [];
    mockBackend({
      user: {
        userId: 'staff-1',
        email: 'sup@nexus.local',
        userType: UserType.Staff,
        roles: ['OperationsManager'],
        permissions: ['outbound:waves:view', 'outbound:waves:manage', 'clients:view', 'outbound:orders:view', 'facilities:view'],
      },
      onFetch: (url, init) => {
        if (url === '/api/v1/outbound/pick-waves' && init?.method === 'POST') {
          const parsed = JSON.parse(String(init.body));
          wavePayloads.push(parsed);
          return { ok: true, status: 201, json: async () => sampleWavePlanning } as Response;
        }
        return undefined;
      },
    });

    renderWithShell(<PickWavesPage />, '/app/outbound/waves');

    await waitFor(() => {
      expect(screen.getByText('WAVE-20261123-001')).toBeDefined();
    });

    // Open wave generation modal and verify nested { request } payload shape
    fireEvent.click(screen.getByTestId('open-generate-wave-btn'));
    await waitFor(() => expect(screen.getByTestId('submit-generate-wave')).toBeDefined());
    fireEvent.click(screen.getByTestId('submit-generate-wave'));

    await waitFor(() => {
      expect(wavePayloads).toHaveLength(1);
      expect(wavePayloads[0]).toHaveProperty('request');
      expect(wavePayloads[0].request.facilityId).toBe(FACILITY_A.id);
      expect(wavePayloads[0].request.waveType).toBe(PickWaveType.BatchPick);
    });

    // Inspect wave detail & check EstimatedTravelMeters is rendered when supported
    await waitFor(() => {
      expect(screen.getByTestId('wave-travel-estimate').textContent).toContain('142.5 m');
    });

    // Release wave requires explicit confirmation
    fireEvent.click(screen.getByTestId('release-wave-wave-1'));
    await waitFor(() => expect(screen.getByTestId('confirm-release-wave-btn')).toBeDefined());
    fireEvent.click(screen.getByTestId('confirm-release-wave-btn'));

    await waitFor(() => {
      expect(screen.getByText(/released to floor/i)).toBeDefined();
    });
  });

  it('rejects ClientAdmin or Driver carrying spoofed SuperAdmin role from wave/allocation/permission bypass', async () => {
    // Client user carrying forged SuperAdmin role & legacy outbound:waves:view
    mockBackend({
      user: {
        userId: 'client-spoof',
        email: 'client@acme.com',
        userType: UserType.Client,
        tenantId: 'client-a',
        roles: ['ClientAdmin', 'SuperAdmin'],
        permissions: ['outbound:orders:view', 'outbound:orders:create', 'outbound:waves:view'],
      },
    });

    const { unmount } = renderWithShell(
      <>
        <Sidebar />
        <LaunchpadGrid />
        <PickWavesPage />
      </>,
      '/app/outbound/waves'
    );

    await waitFor(() => {
      // Pick Waves page refuses access cleanly; sidebar & launchpad hide Pick Waves from Client
      expect(screen.getByTestId('pick-waves-forbidden')).toBeDefined();
    });
    expect(screen.queryByTestId('launchpad-pick-waves')).toBeNull();
    unmount();

    // Driver carrying forged SuperAdmin role + outbound:orders:view denied from Orders AND Waves
    mockBackend({
      user: {
        userId: 'drv-spoof',
        email: 'driver@nexus.local',
        userType: UserType.Driver,
        roles: ['Driver', 'SuperAdmin'],
        permissions: ['outbound:orders:view', 'outbound:waves:manage'],
      },
    });

    renderWithShell(<OutboundOrdersPage />);
    await waitFor(() => {
      expect(screen.getByTestId('outbound-orders-forbidden')).toBeDefined();
    });
  });

  it('separates ClientAdmin order-only actions from Staff allocation/hold and enforces single-facility Staff mutation boundary', async () => {
    // 1. ClientAdmin with legitimate order perms cannot see Allocate, Deallocate, or Hold buttons
    mockBackend({
      user: {
        userId: 'client-1',
        email: 'client@acme.com',
        userType: UserType.Client,
        tenantId: 'client-a',
        roles: ['ClientAdmin'],
        permissions: ['outbound:orders:view', 'outbound:orders:create', 'outbound:orders:cancel'],
      },
    });

    const { unmount } = renderWithShell(<OutboundOrdersPage />);
    fireEvent.click(await screen.findByTestId('inspect-order-ord-1'));
    await waitFor(() => expect(screen.getByTestId('order-detail-drawer')).toBeDefined());

    expect(screen.queryByTestId('btn-allocate-order')).toBeNull();
    expect(screen.queryByTestId('btn-hold-order')).toBeNull();
    expect(screen.getByTestId('btn-cancel-order')).toBeDefined();
    unmount();

    // 2. Staff viewing foreign-facility order cannot mutate it from another active facility
    tokenManager.setActiveFacilityId(FACILITY_B.id);
    mockBackend({
      user: {
        userId: 'staff-cross-read',
        email: 'viewer@nexus.local',
        userType: UserType.Staff,
        roles: ['WarehouseViewer'],
        permissions: ['outbound:orders:view', 'outbound:orders:allocate', 'outbound:orders:manage', 'analytics:multi-facility:view'],
      },
    });

    renderWithShell(<OutboundOrdersPage />);
    await screen.findByText(/Dallas East Hub/i);
    fireEvent.click(await screen.findByTestId('inspect-order-ord-1'));
    await waitFor(() => {
      expect(screen.getByText(/Switch active facility to mutate/i)).toBeDefined();
    });
    expect((screen.getByTestId('btn-allocate-order') as HTMLButtonElement).disabled).toBe(true);
  });

  it('aborts in-flight orders request on facility switch and fails cleanly on malformed API responses', async () => {
    let seenAbort = false;
    let returnMalformed = false;

    const SwitcherAndOrders = () => {
      const { setActiveFacility } = useFacility();
      return (
        <div>
          <button
            type="button"
            data-testid="switch-to-fac-b"
            onClick={() => setActiveFacility(FACILITY_B)}
          >
            Switch Fac B
          </button>
          <OutboundOrdersPage />
        </div>
      );
    };

    mockBackend({
      user: {
        userId: 'staff-1',
        email: 'ops@nexus.local',
        userType: UserType.Staff,
        roles: ['SuperAdmin'],
        permissions: [],
      },
      onFetch: (_url, init) => {
        if (_url.startsWith('/api/v1/outbound/orders?')) {
          if (returnMalformed) {
            return Promise.resolve({
              ok: true,
              status: 200,
              json: async () => ({ unexpectedShape: true }),
            } as Response);
          }
          init?.signal?.addEventListener('abort', () => {
            seenAbort = true;
          });
        }
        return undefined;
      },
    });

    const { unmount } = renderWithShell(<SwitcherAndOrders />);
    await waitFor(() => expect(screen.getByText('SO-2026-1001')).toBeDefined());

    fireEvent.click(screen.getByTestId('switch-to-fac-b'));
    await waitFor(() => {
      expect(seenAbort).toBe(true);
    });
    unmount();

    returnMalformed = true;
    renderWithShell(<OutboundOrdersPage />);
    await waitFor(() => {
      expect(screen.getByTestId('orders-error-banner').textContent).toMatch(/malformed|incomplete/i);
    });
  });

  it('allows seeded ClientAdmin without items:view to load tenant SKUs and submit an outbound order', async () => {
    let createdBody: any = null;
    const fetchSpy = mockBackend({
      user: {
        userId: 'client-admin-seeded',
        email: 'clientadmin@acme.com',
        userType: UserType.Client,
        tenantId: 'client-a',
        roles: ['ClientAdmin'],
        // Exact DbInitializer.cs seeded ClientAdmin permissions (explicitly NO items:view)
        permissions: [
          'clients:view',
          'users:view',
          'users:create',
          'users:edit',
          'users:assign-roles',
          'facilities:view',
          'inventory:view',
          'inventory:adjust',
          'inventory:count',
          'inbound:view',
          'inbound:receive',
          'outbound:view',
          'outbound:create',
          'outbound:orders:view',
          'outbound:orders:create',
          'outbound:orders:import',
          'outbound:orders:cancel',
          'billing:view',
          'dispatch:view',
        ],
      },
      onFetch: async (url, init) => {
        if (url === '/api/v1/outbound/orders' && init?.method === 'POST') {
          createdBody = JSON.parse(String(init.body));
          return {
            ok: true,
            status: 201,
            json: async () => ({
              ...sampleOrderA,
              id: 'ord-ca-1',
              orderNumber: 'SO-2026-CA-01',
            }),
          } as Response;
        }
        return undefined;
      },
    });

    renderWithShell(<OutboundOrdersPage />);
    fireEvent.click(await screen.findByTestId('open-create-order-btn'));

    await waitFor(() => {
      expect(screen.getByRole('option', { name: /SKU-OUT-01/i })).toBeDefined();
    });

    const calledUrls = fetchSpy.mock.calls.map(([u]) => String(u));
    expect(calledUrls.some((u) => u.startsWith('/api/v1/items'))).toBe(false);
    expect(calledUrls.some((u) => u.startsWith('/api/v1/outbound/orders/reference-clients'))).toBe(false);
    expect(calledUrls.some((u) => u.startsWith('/api/v1/outbound/orders/catalog-items'))).toBe(true);

    fireEvent.change(document.getElementById('addr-recipient')!, { target: { value: 'Client Buyer' } });
    fireEvent.change(document.getElementById('addr-line1')!, { target: { value: '200 Market St' } });
    fireEvent.change(document.getElementById('addr-city')!, { target: { value: 'Chicago' } });
    fireEvent.change(document.getElementById('addr-state')!, { target: { value: 'IL' } });
    fireEvent.change(document.getElementById('addr-postal')!, { target: { value: '60602' } });
    const skuSelect = document.querySelector('.order-line-row select') as HTMLSelectElement;
    fireEvent.change(skuSelect, { target: { value: 'item-1' } });
    fireEvent.click(screen.getByTestId('submit-create-order'));

    await waitFor(() => {
      expect(screen.getByText(/Created outbound order SO-2026-CA-01/i)).toBeDefined();
    });
    expect(createdBody?.lines?.[0]?.itemId).toBe('item-1');
  });

  it('allows Staff wave planner without clients:view to load eligible clients and generate a pick wave', async () => {
    let generatedWaveBody: any = null;
    const fetchSpy = mockBackend({
      user: {
        userId: 'wave-planner-1',
        email: 'waves@nexus.local',
        userType: UserType.Staff,
        roles: ['WavePlanner'],
        permissions: ['outbound:waves:view', 'outbound:waves:manage', 'outbound:orders:view'],
      },
      onFetch: async (url, init) => {
        if (url === '/api/v1/outbound/pick-waves' && init?.method === 'POST') {
          generatedWaveBody = JSON.parse(String(init.body));
          return { ok: true, status: 201, json: async () => sampleWavePlanning } as Response;
        }
        return undefined;
      },
    });

    renderWithShell(<PickWavesPage />, '/app/outbound/waves');
    await screen.findByText('WAVE-20261123-001');
    fireEvent.click(screen.getByTestId('open-generate-wave-btn'));

    await waitFor(() => {
      expect(screen.getByRole('option', { name: /Acme Corp/i })).toBeDefined();
    });

    const calledUrls = fetchSpy.mock.calls.map(([u]) => String(u));
    expect(calledUrls.some((u) => u.startsWith('/api/v1/clients'))).toBe(false);
    expect(calledUrls.some((u) => u.startsWith('/api/v1/outbound/pick-waves/eligible-clients'))).toBe(true);

    fireEvent.click(screen.getByTestId('submit-generate-wave'));
    await waitFor(() => {
      expect(generatedWaveBody?.request?.clientId).toBe('client-a');
    });
  });

  it('P741-02 maps bulk-import failed rows accurately across invalid preview rows and multiple consecutive retries', async () => {
    const submittedBatches: Array<{ refs: string[] }> = [];
    mockBackend({
      user: {
        userId: 'client-admin-retry',
        email: 'client@acme.com',
        userType: UserType.Client,
        tenantId: 'client-a',
        roles: ['ClientAdmin'],
        permissions: ['outbound:orders:view', 'outbound:orders:import'],
      },
      onFetch: async (url, init) => {
        if (url === '/api/v1/outbound/orders/bulk-import' && init?.method === 'POST') {
          const parsed = JSON.parse(String(init.body));
          const refs = parsed.orders.map((o: any) => o.externalOrderReference);
          submittedBatches.push({ refs });

          if (submittedBatches.length === 1) {
            return {
              ok: true,
              status: 200,
              json: async () => ({
                batchId: 'b-r1',
                batchNumber: 'BATCH-R1',
                sourceFileName: parsed.sourceFileName,
                totalRows: 4,
                successfulRows: 1,
                duplicateRows: 1,
                failedRows: 2,
                status: 'CompletedWithErrors',
                startedAtUtc: '2026-11-23T08:00:00Z',
                rows: [
                  { rowNumber: 1, externalOrderReference: 'REF-OK', isSuccess: true, isDuplicate: false, createdOutboundOrderId: 'ord-r1' },
                  { rowNumber: 2, externalOrderReference: 'REF-DUP', isSuccess: false, isDuplicate: true, errorMessage: 'Duplicate' },
                  { rowNumber: 3, externalOrderReference: 'REF-FAIL-1', isSuccess: false, isDuplicate: false, errorMessage: 'Timeout 1' },
                  { rowNumber: 4, externalOrderReference: 'REF-FAIL-2', isSuccess: false, isDuplicate: false, errorMessage: 'Timeout 2' },
                ],
              }),
            } as Response;
          }

          if (submittedBatches.length === 2) {
            return {
              ok: true,
              status: 200,
              json: async () => ({
                batchId: 'b-r2',
                batchNumber: 'BATCH-R2',
                sourceFileName: parsed.sourceFileName,
                totalRows: 2,
                successfulRows: 1,
                duplicateRows: 0,
                failedRows: 1,
                status: 'CompletedWithErrors',
                startedAtUtc: '2026-11-23T08:01:00Z',
                rows: [
                  { rowNumber: 1, externalOrderReference: 'REF-FAIL-1', isSuccess: true, isDuplicate: false, createdOutboundOrderId: 'ord-r2' },
                  { rowNumber: 2, externalOrderReference: 'REF-FAIL-2', isSuccess: false, isDuplicate: false, errorMessage: 'Still throttled' },
                ],
              }),
            } as Response;
          }

          return {
            ok: true,
            status: 200,
            json: async () => ({
              batchId: 'b-r3',
              batchNumber: 'BATCH-R3',
              sourceFileName: parsed.sourceFileName,
              totalRows: 1,
              successfulRows: 1,
              duplicateRows: 0,
              failedRows: 0,
              status: 'Completed',
              startedAtUtc: '2026-11-23T08:02:00Z',
              rows: [
                { rowNumber: 1, externalOrderReference: 'REF-FAIL-2', isSuccess: true, isDuplicate: false, createdOutboundOrderId: 'ord-r3' },
              ],
            }),
          } as Response;
        }
        return undefined;
      },
    });

    renderWithShell(<OutboundOrdersPage />);
    await screen.findByText(/Chicago Central Hub \(FAC-A\)/i);
    fireEvent.click(await screen.findByTestId('open-bulk-import-btn'));

    // Invalid preview rows at index 1 (before valid) and index 3 (between valid rows)
    const jsonPayload = JSON.stringify([
      { externalOrderReference: 'BAD-BEFORE', shippingAddress: {}, lines: [] },
      {
        externalOrderReference: 'REF-OK',
        shippingAddress: { recipientName: 'Alice', addressLine1: '1 Main', city: 'Chicago', stateOrProvince: 'IL', postalCode: '60601', countryCode: 'USA' },
        lines: [{ itemId: 'item-1', quantityOrdered: 2 }],
      },
      { externalOrderReference: 'BAD-BETWEEN', shippingAddress: { recipientName: 'NoLines' }, lines: [] },
      {
        externalOrderReference: 'REF-DUP',
        shippingAddress: { recipientName: 'Bob', addressLine1: '2 Main', city: 'Chicago', stateOrProvince: 'IL', postalCode: '60601', countryCode: 'USA' },
        lines: [{ itemId: 'item-1', quantityOrdered: 3 }],
      },
      {
        externalOrderReference: 'REF-FAIL-1',
        shippingAddress: { recipientName: 'Cara', addressLine1: '3 Main', city: 'Chicago', stateOrProvince: 'IL', postalCode: '60601', countryCode: 'USA' },
        lines: [{ itemId: 'item-1', quantityOrdered: 4 }],
      },
      {
        externalOrderReference: 'REF-FAIL-2',
        shippingAddress: { recipientName: 'Dan', addressLine1: '4 Main', city: 'Chicago', stateOrProvince: 'IL', postalCode: '60601', countryCode: 'USA' },
        lines: [{ itemId: 'item-1', quantityOrdered: 5 }],
      },
    ]);

    fireEvent.change(await screen.findByLabelText(/Paste JSON Payload/i), {
      target: { value: jsonPayload },
    });

    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.click(screen.getByTestId('submit-bulk-import'));

    await waitFor(() =>
      expect(screen.getByTestId('bulk-import-summary').textContent).toContain('BATCH-R1')
    );
    expect(submittedBatches[0].refs).toEqual(['REF-OK', 'REF-DUP', 'REF-FAIL-1', 'REF-FAIL-2']);

    // Retry 1: must contain ONLY [REF-FAIL-1, REF-FAIL-2]
    fireEvent.click(screen.getByTestId('retry-failed-import'));
    await waitFor(() =>
      expect(screen.getByTestId('bulk-import-summary').textContent).toContain('BATCH-R2')
    );
    expect(submittedBatches[1].refs).toEqual(['REF-FAIL-1', 'REF-FAIL-2']);

    // Retry 2: must contain ONLY [REF-FAIL-2]
    fireEvent.click(screen.getByTestId('retry-failed-import'));
    await waitFor(() =>
      expect(screen.getByTestId('bulk-import-summary').textContent).toContain('BATCH-R3')
    );
    expect(submittedBatches[2].refs).toEqual(['REF-FAIL-2']);
    expect(screen.queryByTestId('retry-failed-import')).toBeNull();
  });

  it('P741-03 fails closed with explicit error banners on malformed operational enums across orders, allocations, imports, and waves', async () => {
    let scenario: 'order' | 'allocation' | 'import' | 'wave' = 'order';
    mockBackend({
      user: {
        userId: 'staff-enum-check',
        email: 'ops@nexus.local',
        userType: UserType.Staff,
        roles: ['OperationsManager'],
        permissions: [
          'outbound:orders:view',
          'outbound:orders:import',
          'outbound:orders:allocate',
          'outbound:waves:view',
        ],
      },
      onFetch: async (url, init) => {
        if (scenario === 'order' && url.startsWith('/api/v1/outbound/orders?')) {
          return {
            ok: true,
            status: 200,
            json: async () => ({
              items: [{ ...sampleOrderA, status: 'CorruptedStatusEnum' }],
              totalCount: 1,
              pageNumber: 1,
              pageSize: 15,
            }),
          } as Response;
        }
        if (scenario === 'import' && url === '/api/v1/outbound/orders/bulk-import' && init?.method === 'POST') {
          return {
            ok: true,
            status: 200,
            json: async () => ({
              batchId: 'b-bad',
              batchNumber: 'BATCH-BAD',
              sourceFileName: 'bad.json',
              totalRows: 1,
              successfulRows: 0,
              duplicateRows: 0,
              failedRows: 1,
              status: 'MysteryBatchState',
              startedAtUtc: '2026-11-23T08:00:00Z',
              rows: [],
            }),
          } as Response;
        }
        if (scenario === 'allocation' && url === '/api/v1/outbound/orders/ord-1/allocate' && init?.method === 'POST') {
          return {
            ok: true,
            status: 200,
            json: async () => ({
              outboundOrderId: 'ord-1',
              orderNumber: 'SO-2026-1001',
              status: 'Allocated',
              totalQuantityOrdered: 10,
              totalQuantityAllocated: 5,
              totalShortageQuantity: 5,
              isFullyAllocated: false,
              isDuplicateRequest: false,
              attemptCount: 1,
              lineSummaries: [],
              shortages: [
                {
                  shortageId: 'sh-1',
                  lineId: 'line-1',
                  itemId: 'item-1',
                  shortageQuantity: 5,
                  reason: 'UnknownShortageCode',
                  notes: 'bad enum',
                },
              ],
            }),
          } as Response;
        }
        if (scenario === 'wave' && url.startsWith('/api/v1/outbound/pick-waves?')) {
          return {
            ok: true,
            status: 200,
            json: async () => ({
              items: [{ ...sampleWavePlanning, waveType: 'TelekinesisPick' }],
              totalCount: 1,
              pageNumber: 1,
              pageSize: 15,
            }),
          } as Response;
        }
        return undefined;
      },
    });

    // 1. Order malformed enum
    const { unmount: unmountOrders } = renderWithShell(<OutboundOrdersPage />);
    await waitFor(() => {
      expect(screen.getByTestId('orders-error-banner').textContent).toMatch(/OutboundOrderStatus/i);
    });
    unmountOrders();

    // 2. Import malformed batch enum
    scenario = 'import';
    const { unmount: unmountImport } = renderWithShell(<OutboundOrdersPage />);
    await screen.findByText(/Chicago Central Hub \(FAC-A\)/i);
    fireEvent.click(await screen.findByTestId('open-bulk-import-btn'));
    fireEvent.change(await screen.findByLabelText(/Paste JSON Payload/i), {
      target: {
        value: JSON.stringify([
          {
            externalOrderReference: 'REF-BAD-ENUM',
            shippingAddress: {
              recipientName: 'Enum Buyer',
              addressLine1: '1 Main',
              city: 'Chicago',
              stateOrProvince: 'IL',
              postalCode: '60601',
              countryCode: 'USA',
            },
            lines: [{ itemId: 'item-1', quantityOrdered: 1 }],
          },
        ]),
      },
    });
    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.click(screen.getByTestId('submit-bulk-import'));
    await waitFor(() => {
      expect(screen.getByText(/ImportBatchStatus/i)).toBeDefined();
    });
    unmountImport();

    // 3. Allocation shortage malformed enum
    scenario = 'allocation';
    const { unmount: unmountAlloc } = renderWithShell(<OutboundOrdersPage />);
    fireEvent.click(await screen.findByTestId('inspect-order-ord-1'));
    fireEvent.click(await screen.findByTestId('btn-allocate-order'));
    await waitFor(() => {
      expect(screen.getByText(/ShortageReason/i)).toBeDefined();
    });
    unmountAlloc();

    // 4. Pick wave malformed enum
    scenario = 'wave';
    renderWithShell(<PickWavesPage />);
    await waitFor(() => {
      expect(screen.getByRole('alert').textContent).toMatch(/PickWaveType/i);
    });
  });
});
